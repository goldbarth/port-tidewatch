using Microsoft.Extensions.Options;
using Tidewatch.Contracts;
using Tidewatch.Ingestion.Configuration;
using Tidewatch.Ingestion.Evaluation;

namespace Tidewatch.Ingestion.UnitTests.Evaluation;

/// <summary>
/// Behaviour of <see cref="SurgeEvaluator"/> against the configured stages
/// (normal 0, warning 4.50, severe 5.50 m NHN). Cases mirror the worked examples and
/// decisions in ADR-004: median base stage, trend pre-escalation, de-escalation
/// hysteresis, and outlier damping.
/// </summary>
public sealed class SurgeEvaluatorTests
{
    private const string Normal = "normal";
    private const string Warning = "warning";
    private const string Severe = "severe";

    private static SurgeEvaluator CreateEvaluator()
    {
        var options = new SurgeThresholdOptions
        {
            Reference = "NHN",
            TrendWindow = TimeSpan.FromMinutes(30),
            Stages =
            [
                new SurgeStage { Name = Normal, MinMeters = 0m },
                new SurgeStage { Name = Warning, MinMeters = 4.50m },
                new SurgeStage { Name = Severe, MinMeters = 5.50m },
            ],
        };
        return new SurgeEvaluator(Options.Create(options));
    }

    /// <summary>Builds a window of readings, one minute apart, in the given order.</summary>
    private static IReadOnlyList<Reading> Window(params decimal[] values)
    {
        var t0 = DateTimeOffset.UnixEpoch;
        return values
            .Select((v, i) => new Reading("g1", v, t0.AddMinutes(i)))
            .ToArray();
    }

    [Fact]
    public void Empty_window_is_normal()
    {
        var sut = CreateEvaluator();

        Assert.Equal(Normal, sut.Evaluate("g1", Window(), currentStage: null));
    }

    [Fact]
    public void Level_over_boundary_escalates_immediately()
    {
        var sut = CreateEvaluator();

        // Steady 4.55 (median 4.55, not rising): base stage alone reaches warning.
        var result = sut.Evaluate("g1", Window(4.55m, 4.55m, 4.55m, 4.55m, 4.55m), Normal);

        Assert.Equal(Warning, result);
    }

    [Fact]
    public void Level_over_two_boundaries_jumps_straight_to_severe()
    {
        var sut = CreateEvaluator();

        // Escalation is immediate and multi-stage: normal -> severe in one step.
        var result = sut.Evaluate("g1", Window(5.55m, 5.55m, 5.55m, 5.55m, 5.55m), Normal);

        Assert.Equal(Severe, result);
    }

    [Fact]
    public void Rising_within_trend_margin_pre_escalates_one_stage()
    {
        var sut = CreateEvaluator();

        // Recent median 4.45 (below the 4.50 floor) but rising and within 0.15 of it.
        var window = Window(
            4.20m, 4.21m, 4.22m, 4.23m, 4.24m,
            4.43m, 4.44m, 4.45m, 4.46m, 4.47m);

        Assert.Equal(Warning, sut.Evaluate("g1", window, Normal));
    }

    [Fact]
    public void Same_level_but_falling_does_not_pre_escalate()
    {
        var sut = CreateEvaluator();

        // Mirror of the trend case, reversed: median 4.45, but the window is falling.
        var window = Window(
            4.47m, 4.46m, 4.45m, 4.44m, 4.43m,
            4.24m, 4.23m, 4.22m, 4.21m, 4.20m);

        Assert.Equal(Normal, sut.Evaluate("g1", window, Normal));
    }

    [Theory]
    [InlineData(4.45)] // 4.45 > 4.50 - 0.15 -> still inside the hysteresis band
    [InlineData(4.40)] // 4.40 > 4.35 -> still held, no flap
    public void Within_hysteresis_band_holds_current_stage(decimal level)
    {
        var sut = CreateEvaluator();

        var result = sut.Evaluate("g1", Window(level, level, level, level, level), Warning);

        Assert.Equal(Warning, result);
    }

    [Fact]
    public void Clearing_hysteresis_floor_de_escalates()
    {
        var sut = CreateEvaluator();

        // 4.35 is exactly the floor (4.50 - 0.15); the strict comparison releases here.
        var result = sut.Evaluate("g1", Window(4.35m, 4.35m, 4.35m, 4.35m, 4.35m), Warning);

        Assert.Equal(Normal, result);
    }

    [Fact]
    public void Hysteresis_holds_at_the_severe_boundary_too()
    {
        var sut = CreateEvaluator();

        // Median 5.40: base is warning, but within 0.15 of the 5.50 floor while at severe.
        var result = sut.Evaluate("g1", Window(5.40m, 5.40m, 5.40m, 5.40m, 5.40m), Severe);

        Assert.Equal(Severe, result);
    }

    [Fact]
    public void Dropping_well_below_de_escalates_multiple_stages_at_once()
    {
        var sut = CreateEvaluator();

        // From severe to a level cleared well below the warning floor: straight to normal.
        var result = sut.Evaluate("g1", Window(4.30m, 4.30m, 4.30m, 4.30m, 4.30m), Severe);

        Assert.Equal(Normal, result);
    }

    [Fact]
    public void Single_outlier_does_not_move_the_stage()
    {
        var sut = CreateEvaluator();

        // One 6.0 spike among 3.0 readings: the median stays at 3.0, no escalation.
        var result = sut.Evaluate("g1", Window(3.0m, 3.0m, 6.0m, 3.0m, 3.0m), Normal);

        Assert.Equal(Normal, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bogus")]
    public void Null_or_unknown_current_stage_is_treated_as_normal(string? currentStage)
    {
        var sut = CreateEvaluator();

        // With no real prior stage, hysteresis cannot hold; the base stage stands.
        var result = sut.Evaluate("g1", Window(4.55m, 4.55m, 4.55m, 4.55m, 4.55m), currentStage);

        Assert.Equal(Warning, result);
    }
}
