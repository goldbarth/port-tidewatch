using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Tidewatch.Contracts;
using Tidewatch.Ingestion.Alerting;
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
    private readonly IAlertPublisher _alerts;
    private readonly ConcurrentDictionary<string, GaugeState> _gauges = new();

    private const string NormalStage = "normal";

    public GaugeStateHolder(IOptions<SurgeThresholdOptions> options, IAlertPublisher alerts)
    {
        _trendWindow = options.Value.TrendWindow;
        _alerts = alerts;
    }

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
            // Arrival time on our clock — distinct from reading.Timestamp, which carries the
            // source's (often minutes-old) measurement time. Freshness keys off arrival so a
            // source with publication lag is not flagged stale while data keeps flowing.
            state.LastReceivedAt = DateTimeOffset.UtcNow;
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
                result.Add(new GaugeSnapshot(id, state.Window.ToArray(), state.Alert, state.LastReceivedAt));
        }
        return result;
    }

    /// <summary>
    /// Isolated point called when the evaluator detects a stage change. Updates the
    /// gauge's alert state and, on a genuine transition, publishes an
    /// <see cref="AlertEvent"/> — the single chokepoint where both happen (ADR-001).
    /// The previous stage is captured and the state mutated under the per-gauge lock;
    /// publishing happens afterwards, off the lock (no I/O while holding it). The first
    /// establishment of <c>normal</c> (no prior stage) is not a transition, so no event
    /// is published — only the state is stamped.
    /// </summary>
    public async Task ApplyStageChange(
        string gaugeId, string newStage, decimal level, DateTimeOffset at)
    {
        var state = _gauges.GetOrAdd(gaugeId, _ => new GaugeState());

        string previousStage;
        lock (state.Gate)
        {
            previousStage = state.Alert?.Stage ?? NormalStage;
            state.Alert = new AlertState(gaugeId, newStage, at);
        }

        if (!string.Equals(previousStage, newStage, StringComparison.Ordinal))
            await _alerts.PublishAsync(
                new AlertEvent(gaugeId, previousStage, newStage, level, at), CancellationToken.None);
    }

    private sealed class GaugeState
    {
        public object Gate { get; } = new();
        public List<Reading> Window { get; } = [];
        public AlertState? Alert { get; set; }
        public DateTimeOffset? LastReceivedAt { get; set; }
    }
}

/// <summary>A gauge's raw reading window, current alert, and last arrival time at snapshot time.</summary>
public sealed record GaugeSnapshot(
    string GaugeId, IReadOnlyList<Reading> Window, AlertState? Alert, DateTimeOffset? LastReceivedAt = null);
