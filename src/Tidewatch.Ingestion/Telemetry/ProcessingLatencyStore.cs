using System.Collections.Concurrent;

namespace Tidewatch.Ingestion.Telemetry;

/// <summary>One processing-latency sample for a gauge: how long the ingest span ran,
/// and when it was recorded.</summary>
public sealed record LatencySample(double Milliseconds, DateTimeOffset At);

/// <summary>
/// Holds a short per-gauge window of processing-latency samples, fed by
/// <see cref="ProcessingLatencyListener"/> from the existing ingest span's duration —
/// not a separate measurement. Registered as a singleton and read by the gauge API.
/// Raw store: percentile and trend shaping happen in the API mapper (ADR-002), the same
/// split <see cref="State.GaugeStateHolder"/> follows. Thread-safe via a per-gauge lock —
/// samples may be recorded from parallel message processing.
/// </summary>
public sealed class ProcessingLatencyStore
{
    /// <summary>Most-recent samples kept per gauge; enough for a short-window p50/p95
    /// and a sparkline, bounded so memory stays flat.</summary>
    private const int Capacity = 64;

    private readonly ConcurrentDictionary<string, GaugeLatency> _gauges = new();

    public void Record(string gaugeId, double milliseconds, DateTimeOffset at)
    {
        var gauge = _gauges.GetOrAdd(gaugeId, _ => new GaugeLatency());
        lock (gauge.Gate)
        {
            gauge.Samples.Add(new LatencySample(milliseconds, at));
            if (gauge.Samples.Count > Capacity)
                gauge.Samples.RemoveAt(0);
        }
    }

    /// <summary>Snapshot of a gauge's recent latency samples in record order, or empty
    /// when none have been observed yet.</summary>
    public IReadOnlyList<LatencySample> Samples(string gaugeId)
    {
        if (!_gauges.TryGetValue(gaugeId, out var gauge))
            return [];
        lock (gauge.Gate)
            return gauge.Samples.ToArray();
    }

    private sealed class GaugeLatency
    {
        public object Gate { get; } = new();
        public List<LatencySample> Samples { get; } = [];
    }
}
