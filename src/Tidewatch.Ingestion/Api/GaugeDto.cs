using Tidewatch.Contracts;
using Tidewatch.Ingestion.State;

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
    DateTimeOffset? MeasuredAt,
    double? CadenceSeconds,
    DateTimeOffset? LastReadingAt);

/// <summary>A single point on a gauge's recent-history trend.</summary>
public sealed record TrendPointDto(DateTimeOffset T, decimal V);

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

    public static GaugeDto ToDto(GaugeSnapshot snapshot, DateTimeOffset now)
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
            window.Count > 0 ? window[^1].Timestamp : null,
            CadenceSeconds(window),
            snapshot.LastReceivedAt);
    }

    /// <summary>
    /// Expected source cadence in seconds, inferred as the median gap between consecutive
    /// reading timestamps. The median — not the mean — so one missed poll (a single large
    /// gap) does not inflate the cadence, consistent with the evaluator's robustness
    /// (ADR-004). The client derives the per-tile stale threshold from this, so freshness
    /// adapts to whichever source is active rather than a hard-coded value. Null when
    /// fewer than two readings span any time.
    /// </summary>
    private static double? CadenceSeconds(IReadOnlyList<Reading> window)
    {
        if (window.Count < 2)
            return null;

        var gaps = new List<double>(window.Count - 1);
        for (var i = 1; i < window.Count; i++)
        {
            var seconds = (window[i].Timestamp - window[i - 1].Timestamp).TotalSeconds;
            if (seconds > 0)
                gaps.Add(seconds);
        }

        if (gaps.Count == 0)               // all readings share one timestamp
            return null;

        gaps.Sort();
        var mid = gaps.Count / 2;
        return gaps.Count % 2 == 1
            ? gaps[mid]
            : (gaps[mid - 1] + gaps[mid]) / 2;
    }

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
