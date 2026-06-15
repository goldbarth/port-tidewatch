namespace Tidewatch.Ingestion.Configuration;

/// <summary>
/// Bound to the <c>SurgeThresholds</c> section of appsettings.
/// </summary>
public sealed class SurgeThresholdOptions
{
    public const string SectionName = "SurgeThresholds";

    /// <summary>Vertical reference datum, e.g. "NHN".</summary>
    public string Reference { get; set; } = string.Empty;

    public TimeSpan TrendWindow { get; set; }

    /// <summary>Ordered list of stages, ascending by <see cref="SurgeStage.MinMeters"/>.</summary>
    public IReadOnlyList<SurgeStage> Stages { get; set; } = [];
}

public sealed class SurgeStage
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Lower bound of the stage in metres.</summary>
    public decimal MinMeters { get; set; }
}
