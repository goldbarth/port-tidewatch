namespace Tidewatch.Contracts;

/// <summary>
/// Published when a gauge's surge stage genuinely changes. Lets additional consumers
/// (notification, audit) react without touching the ingestion path. Emitted from the
/// single <c>ApplyStageChange</c> chokepoint (ADR-001, v1.1.0).
/// </summary>
/// <param name="GaugeId">Identifier of the gauge whose stage changed.</param>
/// <param name="PreviousStage">Stage before the change (e.g. <c>normal</c>).</param>
/// <param name="NewStage">Stage after the change (e.g. <c>warning</c>).</param>
/// <param name="Level">
/// Water level (metres) of the reading that triggered the change. Decimal, not double —
/// consistent with <see cref="Reading.Value"/>.
/// </param>
/// <param name="Timestamp">When the change occurred (the triggering reading's time).</param>
public sealed record AlertEvent(
    string GaugeId,
    string PreviousStage,
    string NewStage,
    decimal Level,
    DateTimeOffset Timestamp);
