using Tidewatch.Contracts;
using Tidewatch.Ingestion.State;

namespace Tidewatch.Ingestion.Api;

/// <summary>Dashboard view of one gauge. Shaped for the read-only client.</summary>
public sealed record GaugeDto(
    string GaugeId,
    decimal? Level,
    string Stage,
    DateTimeOffset? ChangedAt,
    IReadOnlyList<TrendPointDto> Trend);

/// <summary>A single point on a gauge's recent-history trend.</summary>
public sealed record TrendPointDto(DateTimeOffset T, decimal V);

/// <summary>
/// Maps the raw <see cref="GaugeSnapshot"/> to the dashboard DTO. The trend is
/// downsampled to a fixed number of buckets here — a view concern kept out of the
/// state holder. The window is assumed chronological (insert order under the
/// consumer's single-dispatch processing), so no re-sorting is done.
/// </summary>
public static class GaugeMapper
{
    private const int TrendBuckets = 24;
    private const string NormalStage = "normal";

    public static GaugeDto ToDto(GaugeSnapshot snapshot)
    {
        var window = snapshot.Window;
        var level = window.Count > 0 ? window[^1].Value : (decimal?)null;
        return new GaugeDto(
            snapshot.GaugeId,
            level,
            snapshot.Alert?.Stage ?? NormalStage,
            snapshot.Alert?.ChangedAt,
            Downsample(window));
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
