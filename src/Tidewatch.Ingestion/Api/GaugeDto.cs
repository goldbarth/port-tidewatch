using Tidewatch.Contracts;
using Tidewatch.Ingestion.State;
using Tidewatch.Ingestion.Telemetry;

namespace Tidewatch.Ingestion.Api;

/// <summary>Dashboard view of one gauge. Shaped for the read-only client.</summary>
public sealed record GaugeDto(
    string GaugeId,
    decimal? Level,
    string Stage,
    DateTimeOffset? ChangedAt,
    IReadOnlyList<TrendPointDto> Trend,
    decimal? RateMetersPerMin,
    long? TimeInStageSeconds,
    decimal? WindowMin,
    decimal? WindowMax,
    LatencyDto Latency);

/// <summary>A single point on a gauge's recent-history trend.</summary>
public sealed record TrendPointDto(DateTimeOffset T, decimal V);

/// <summary>
/// Processing-latency pulse for a gauge, derived from the ingest span's duration (M8).
/// Figures are null and <see cref="Trend"/> empty when no telemetry has been observed
/// yet; <see cref="LastAt"/> lets the client mark stale data degraded, mirroring the
/// dashboard's connection indicator. Health thresholds are a client/view concern.
/// </summary>
public sealed record LatencyDto(
    double? LastMs,
    double? P50Ms,
    double? P95Ms,
    DateTimeOffset? LastAt,
    IReadOnlyList<double> Trend);

/// <summary>
/// Maps the raw <see cref="GaugeSnapshot"/> to the dashboard DTO. Derived monitoring
/// signals (rate-of-change, time-in-stage, window extent) and the downsampled trend are
/// computed here — view concerns kept out of the state holder (ADR-002). The window is
/// assumed chronological (insert order under the consumer's single-dispatch processing),
/// so no re-sorting is done.
/// </summary>
public static class GaugeMapper
{
    private const int TrendBuckets = 24;
    private const string NormalStage = "normal";

    public static GaugeDto ToDto(
        GaugeSnapshot snapshot,
        DateTimeOffset now,
        IReadOnlyList<LatencySample>? latency = null)
    {
        var window = snapshot.Window;
        var level = window.Count > 0 ? window[^1].Value : (decimal?)null;
        var timeInStage = snapshot.Alert is { } alert
            ? (long)Math.Max(0, (now - alert.ChangedAt).TotalSeconds)
            : (long?)null;
        return new GaugeDto(
            snapshot.GaugeId,
            level,
            snapshot.Alert?.Stage ?? NormalStage,
            snapshot.Alert?.ChangedAt,
            Downsample(window),
            RatePerMinute(window),
            timeInStage,
            window.Count > 0 ? window.Min(r => r.Value) : null,
            window.Count > 0 ? window.Max(r => r.Value) : null,
            MapLatency(latency ?? []));
    }

    /// <summary>
    /// Shapes the raw latency samples into the pulse DTO: most-recent value, p50/p95 over
    /// the window (nearest-rank), the time of the last sample for staleness, and the
    /// ordered series for a sparkline. Empty in, empty out.
    /// </summary>
    private static LatencyDto MapLatency(IReadOnlyList<LatencySample> samples)
    {
        if (samples.Count == 0)
            return new LatencyDto(null, null, null, null, []);

        var sorted = samples.Select(s => s.Milliseconds).OrderBy(ms => ms).ToArray();
        return new LatencyDto(
            Round(samples[^1].Milliseconds),
            Round(Percentile(sorted, 50)),
            Round(Percentile(sorted, 95)),
            samples[^1].At,
            samples.Select(s => Round(s.Milliseconds)).ToArray());
    }

    /// <summary>Nearest-rank percentile over an ascending array.</summary>
    private static double Percentile(IReadOnlyList<double> ascending, int percentile)
    {
        var rank = (int)Math.Ceiling(percentile / 100.0 * ascending.Count);
        var index = Math.Clamp(rank - 1, 0, ascending.Count - 1);
        return ascending[index];
    }

    private static double Round(double ms) => Math.Round(ms, 1);

    /// <summary>
    /// Rate-of-change in m/min as the least-squares slope of the window (value against
    /// minutes from the first reading). A fitted slope, not an endpoint difference, so a
    /// single outlier does not swing the trend — consistent with the evaluator's
    /// robustness (ADR-004). Null when fewer than two readings span any time.
    /// </summary>
    private static decimal? RatePerMinute(IReadOnlyList<Reading> window)
    {
        if (window.Count < 2)
            return null;

        var t0 = window[0].Timestamp;
        double sumX = 0, sumY = 0, sumXy = 0, sumXx = 0;
        foreach (var r in window)
        {
            var x = (r.Timestamp - t0).TotalMinutes;
            var y = (double)r.Value;
            sumX += x;
            sumY += y;
            sumXy += x * y;
            sumXx += x * x;
        }

        var n = window.Count;
        var denominator = n * sumXx - sumX * sumX;
        if (denominator == 0)              // all readings share one timestamp
            return null;

        var slope = (n * sumXy - sumX * sumY) / denominator;
        return decimal.Round((decimal)slope, 3);
    }

    /// <summary>
    /// Reduces the window to at most <see cref="TrendBuckets"/> points, taking the last
    /// (most recent) reading of each evenly sized bucket. Windows already at or below
    /// the bucket count pass through unchanged.
    /// </summary>
    private static IReadOnlyList<TrendPointDto> Downsample(IReadOnlyList<Reading> window)
    {
        if (window.Count <= TrendBuckets)
            return window.Select(r => new TrendPointDto(r.Timestamp, r.Value)).ToArray();

        var result = new List<TrendPointDto>(TrendBuckets);
        for (var b = 0; b < TrendBuckets; b++)
        {
            // Last index falling in this bucket → the most recent point it contains.
            var idx = (int)(((long)(b + 1) * window.Count) / TrendBuckets) - 1;
            result.Add(new TrendPointDto(window[idx].Timestamp, window[idx].Value));
        }
        return result;
    }
}
