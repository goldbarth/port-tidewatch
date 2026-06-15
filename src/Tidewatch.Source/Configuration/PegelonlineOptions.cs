namespace Tidewatch.Source.Configuration;

/// <summary>
/// Bound to the <c>Pegelonline</c> section of appsettings. The live WSV/PEGELONLINE feed:
/// base URL, poll cadence, the window to backfill on start, and the configured stations.
/// </summary>
public sealed class PegelonlineOptions
{
    public const string SectionName = "Pegelonline";

    /// <summary>REST-API v2 base. Datenlizenz Deutschland Zero 2.0, no auth.</summary>
    public string BaseUrl { get; set; } = "https://www.pegelonline.wsv.de/webservices/rest-api/v2";

    /// <summary>How often the latest value is polled per station.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Window backfilled once at start, so the evaluator has a trend immediately.</summary>
    public TimeSpan Backfill { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Hamburg Elbe gauges to poll. UUIDs are configuration, never hard-coded.</summary>
    public List<PegelStationOptions> Stations { get; set; } = [];
}

/// <summary>One configured PEGELONLINE station.</summary>
public sealed class PegelStationOptions
{
    /// <summary>The GaugeId emitted on <c>Reading</c> records (the downstream identifier).</summary>
    public string GaugeId { get; set; } = string.Empty;

    /// <summary>The PEGELONLINE station UUID.</summary>
    public string Uuid { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit PNP elevation (m above NHN). When null, it is fetched live from
    /// the station's <c>gaugeZero.value</c> on start; this is the override / fallback.
    /// </summary>
    public decimal? PnpOffsetMeters { get; set; }
}
