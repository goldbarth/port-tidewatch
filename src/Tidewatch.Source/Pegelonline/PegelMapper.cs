namespace Tidewatch.Source.Pegelonline;

/// <summary>
/// The explicit mapping layer from PEGELONLINE's gauge-relative centimetres to true metres
/// above NHN. PEGELONLINE publishes <c>W</c> in centimetres above the gauge zero
/// (Pegelnullpunkt, PNP). NHN level = W/100 + the station's PNP elevation (m above NHN,
/// <c>gaugeZero.value</c>). Pure and unit-tested — a known value is pinned end to end.
/// </summary>
public static class PegelMapper
{
    /// <summary>
    /// Converts a raw W value in cm above PNP to metres above NHN, given the station's PNP
    /// elevation in metres above NHN. Rounded to 3 dp, matching the simulator's precision.
    /// </summary>
    public static decimal ToNhnMeters(decimal valueCm, decimal pnpOffsetMeters)
        => decimal.Round(valueCm / 100m + pnpOffsetMeters, 3);
}
