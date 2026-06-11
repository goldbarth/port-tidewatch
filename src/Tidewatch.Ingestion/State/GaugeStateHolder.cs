using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Tidewatch.Contracts;
using Tidewatch.Ingestion.Configuration;

namespace Tidewatch.Ingestion.State;

/// <summary>
/// Holds live per-gauge state: the reading window (readings within the trend window)
/// and the current stage. Registered as a singleton. Thread-safe — messages may be
/// processed in parallel.
/// </summary>
public sealed class GaugeStateHolder
{
    private readonly TimeSpan _trendWindow;
    private readonly ConcurrentDictionary<string, GaugeState> _gauges = new();

    public GaugeStateHolder(IOptions<SurgeThresholdOptions> options) =>
        _trendWindow = options.Value.TrendWindow;

    /// <summary>
    /// Adds a reading to the gauge's window and discards readings older than the
    /// trend window relative to the new reading.
    /// </summary>
    public void Add(Reading reading)
    {
        var state = _gauges.GetOrAdd(reading.GaugeId, _ => new GaugeState());
        lock (state.Gate)
        {
            state.Window.Add(reading);
            var cutoff = reading.Timestamp - _trendWindow;
            state.Window.RemoveAll(r => r.Timestamp < cutoff);
        }
    }

    /// <summary>Returns a snapshot of the current reading window for a gauge.</summary>
    public IReadOnlyList<Reading> GetWindow(string gaugeId)
    {
        if (!_gauges.TryGetValue(gaugeId, out var state))
            return [];
        lock (state.Gate)
            return state.Window.ToArray();
    }

    /// <summary>The current alert state for a gauge, if any reading has arrived.</summary>
    public AlertState? GetAlertState(string gaugeId) =>
        _gauges.TryGetValue(gaugeId, out var state) ? state.Alert : null;

    /// <summary>
    /// Point-in-time snapshot of every gauge: its current reading window and alert
    /// state. Read-only — for the dashboard API. Each gauge is copied under its own
    /// lock, so the snapshot is consistent per gauge. No view concerns here; trend
    /// shaping happens in the API mapper.
    /// </summary>
    public IReadOnlyList<GaugeSnapshot> Snapshot()
    {
        var result = new List<GaugeSnapshot>(_gauges.Count);
        foreach (var (id, state) in _gauges)
        {
            lock (state.Gate)
                result.Add(new GaugeSnapshot(id, state.Window.ToArray(), state.Alert));
        }
        return result;
    }

    /// <summary>
    /// Isolated point called when the evaluator detects a stage change. For now it
    /// only updates state; in v1.1.0 this same point also publishes the alert event.
    /// </summary>
    public void ApplyStageChange(string gaugeId, string newStage, DateTimeOffset at)
    {
        var state = _gauges.GetOrAdd(gaugeId, _ => new GaugeState());
        lock (state.Gate)
            state.Alert = new AlertState(gaugeId, newStage, at);

        // v1.1.0: publish alert event here.
    }

    private sealed class GaugeState
    {
        public object Gate { get; } = new();
        public List<Reading> Window { get; } = [];
        public AlertState? Alert { get; set; }
    }
}

/// <summary>A gauge's raw reading window and current alert at snapshot time.</summary>
public sealed record GaugeSnapshot(
    string GaugeId, IReadOnlyList<Reading> Window, AlertState? Alert);
