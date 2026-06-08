using Tidewatch.Contracts;

namespace Tidewatch.Ingestion.Evaluation;

/// <summary>
/// Determines the surge stage for a gauge from its current reading window.
/// </summary>
public interface ISurgeEvaluator
{
    /// <summary>
    /// Evaluates the stage for <paramref name="gaugeId"/> given its current
    /// <paramref name="window"/> of readings (already trimmed to the trend window) and
    /// the gauge's <paramref name="currentStage"/>.
    /// </summary>
    /// <remarks>
    /// <paramref name="currentStage"/> is required for de-escalation hysteresis: the
    /// stage is only lowered once the level has cleared the current boundary by a
    /// margin, so the result depends on where the gauge already is. Pass the gauge's
    /// last known stage, or <c>null</c> / "normal" if none has been determined yet.
    /// </remarks>
    /// <returns>The determined stage name.</returns>
    string Evaluate(string gaugeId, IReadOnlyList<Reading> window, string? currentStage);
}
