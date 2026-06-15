namespace Tidewatch.Source.Pegelonline;

/// <summary>A single W measurement: timestamp and value in cm above PNP.</summary>
public sealed record PegelMeasurement(DateTimeOffset Timestamp, decimal Value);

/// <summary>Station metadata; only <c>gaugeZero</c> is consumed (PNP elevation).</summary>
public sealed record PegelStation(PegelGaugeZero? GaugeZero);

/// <summary>The station's gauge zero: <c>value</c> is its elevation in metres above NHN.</summary>
public sealed record PegelGaugeZero(decimal Value, string? Unit);
