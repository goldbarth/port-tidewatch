namespace Tidewatch.Contracts;

/// <summary>
/// Current alert state for a gauge. Shared between the state holder and the dashboard API.
/// </summary>
/// <param name="GaugeId">Identifier of the gauge.</param>
/// <param name="Stage">Current surge stage name (e.g. "normal", "warning", "severe").</param>
/// <param name="ChangedAt">Timestamp of the last stage change.</param>
public sealed record AlertState(string GaugeId, string Stage, DateTimeOffset ChangedAt);
