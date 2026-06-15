namespace Tidewatch.Source.Configuration;

/// <summary>
/// Bound to the <c>Simulator</c> section of appsettings. Tunables for the scripted surge
/// signal. The documented <c>SURGE_PEAK_M</c> / <c>SURGE_PERIOD_S</c> environment
/// variables still win — they are applied as a post-configure override in Program.
/// </summary>
public sealed class SimulatorOptions
{
    public const string SectionName = "Simulator";

    /// <summary>Absolute level (m NHN) the surge gauge peaks at.</summary>
    public decimal SurgePeakMeters { get; set; } = 5.80m;

    /// <summary>Seconds for one full surge cycle.</summary>
    public double SurgePeriodSeconds { get; set; } = 180.0;

    /// <summary>Seconds between published reading batches.</summary>
    public double IntervalSeconds { get; set; } = 2.0;
}
