using Microsoft.Extensions.Options;
using Tidewatch.Contracts;
using Tidewatch.Ingestion.Configuration;

namespace Tidewatch.Ingestion.Evaluation;

/// <summary>
/// Default surge evaluator. Derives the stage from a gauge's reading window using the
/// median of the most recent samples (robust against single outliers), pre-escalates one
/// stage when the level is rising toward the next boundary, and applies de-escalation
/// hysteresis so the stage does not flap while the level hovers around a boundary.
/// </summary>
/// <remarks>
/// Not a forecasting model. The trend rule is a sign-and-proximity nudge, not a
/// projection. The sample count and margins below are deliberate constants for v1; if
/// they ever need per-deployment tuning they can move into
/// <see cref="SurgeThresholdOptions"/> with validation.
/// </remarks>
public sealed class SurgeEvaluator : ISurgeEvaluator
{
    /// <summary>Number of most-recent readings the median is taken over.</summary>
    private const int SampleCount = 5;

    /// <summary>
    /// When the level is rising and within this margin (in metres) below the next stage's
    /// floor, the stage is pre-escalated. A modest early warning, not a forecast.
    /// </summary>
    private const decimal TrendMargin = 0.15m;

    /// <summary>
    /// The level must clear the current stage's floor by this margin (in metres) before
    /// the stage is lowered. Keeps the stage from flapping at a boundary while abating.
    /// </summary>
    /// <remarks>
    /// Invariant: <c>HysteresisMargin &gt;= TrendMargin</c> (asserted in the static
    /// constructor). If the hysteresis band were narrower than the trend band, a level
    /// that wobbles in the gap between the trend trigger
    /// (<c>nextFloor - TrendMargin</c>) and the hysteresis hold floor
    /// (<c>floor - HysteresisMargin</c>) would alternately pre-escalate and fall back on
    /// each reading — the exact flapping this evaluator exists to prevent. Equality is the
    /// tightest safe setting: the trend stops pre-escalating and the stage stops being
    /// held at the same level, leaving no flapping gap and not delaying legitimate
    /// de-escalation.
    /// </remarks>
    private const decimal HysteresisMargin = 0.15m;

    private const string NormalStage = "normal";

    /// <summary>Stages ordered ascending by <see cref="SurgeStage.MinMeters"/>.</summary>
    private readonly IReadOnlyList<SurgeStage> _stages;

    static SurgeEvaluator()
    {
        // A narrower hysteresis band than the trend band leaves a gap in which a wobbling
        // level flaps between the pre-escalated and base stage on each reading. Guard the
        // invariant here so a future margin change fails fast instead of flapping silently.
        if (HysteresisMargin < TrendMargin)
        {
            throw new InvalidOperationException(
                $"{nameof(HysteresisMargin)} ({HysteresisMargin}) must be >= " +
                $"{nameof(TrendMargin)} ({TrendMargin}) to avoid boundary flapping.");
        }
    }

    public SurgeEvaluator(IOptions<SurgeThresholdOptions> options) =>
        _stages = options.Value.Stages.OrderBy(s => s.MinMeters).ToArray();

    public string Evaluate(string gaugeId, IReadOnlyList<Reading> window, string? currentStage)
    {
        if (window.Count == 0)
            return NormalStage;

        var level = Median(window.TakeLast(SampleCount));
        var earlier = Median(window.Take(SampleCount));
        var rising = level > earlier;

        var baseIndex = BaseStageIndex(level);
        var candidateIndex = baseIndex;

        // Trend nudge: rising and knocking on the next boundary → pre-escalate one stage.
        if (baseIndex < _stages.Count - 1
            && rising
            && level >= _stages[baseIndex + 1].MinMeters - TrendMargin)
        {
            candidateIndex = baseIndex + 1;
        }

        var currentIndex = StageIndex(currentStage);

        // Escalating or holding is immediate; de-escalating requires clearing the current
        // floor by the hysteresis margin, otherwise hold the current stage. Because
        // HysteresisMargin >= TrendMargin, a trend-pre-escalated stage is held until the
        // level drops below its own trend trigger, so the stage cannot flap at a boundary.
        if (candidateIndex < currentIndex
            && level > _stages[currentIndex].MinMeters - HysteresisMargin)
        {
            return _stages[currentIndex].Name;
        }

        return _stages[candidateIndex].Name;
    }

    /// <summary>Highest stage whose floor the level has reached.</summary>
    private int BaseStageIndex(decimal level)
    {
        var index = 0;
        for (var i = 0; i < _stages.Count; i++)
        {
            if (level >= _stages[i].MinMeters)
                index = i;
            else
                break;
        }
        return index;
    }

    /// <summary>
    /// Index of <paramref name="stage"/> in the ordered stages, or the normal stage
    /// (index 0) when it is null or not a configured stage.
    /// </summary>
    private int StageIndex(string? stage)
    {
        if (string.IsNullOrWhiteSpace(stage))
            return 0;
        for (var i = 0; i < _stages.Count; i++)
        {
            if (string.Equals(_stages[i].Name, stage, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return 0;
    }

    private static decimal Median(IEnumerable<Reading> readings)
    {
        var values = readings.Select(r => r.Value).OrderBy(v => v).ToArray();
        var mid = values.Length / 2;
        return values.Length % 2 == 1
            ? values[mid]
            : (values[mid - 1] + values[mid]) / 2m;
    }
}
