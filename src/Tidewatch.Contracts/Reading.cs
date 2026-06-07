namespace Tidewatch.Contracts;

/// <summary>
/// A single gauge level reading.
/// </summary>
/// <param name="GaugeId">Identifier of the gauge that produced the reading.</param>
/// <param name="Value">
/// Water level in metres. Decimal, not double — avoids rounding error on level values.
/// </param>
/// <param name="Timestamp">
/// When the reading was taken. DateTimeOffset, not DateTime — unambiguous across time zones.
/// </param>
public sealed record Reading(string GaugeId, decimal Value, DateTimeOffset Timestamp);
