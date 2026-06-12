using Tidewatch.Contracts;
using Tidewatch.Ingestion.Api;
using Tidewatch.Ingestion.State;

namespace Tidewatch.Ingestion.UnitTests.Api;

/// <summary>
/// Behaviour of the derived monitoring signals added in M6 (#28): rate-of-change,
/// time-in-stage, and window extent. These are computed in <see cref="GaugeMapper"/>;
/// the state holder stays raw (ADR-002).
/// </summary>
public sealed class GaugeMapperTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    /// <summary>Builds a window of readings, one minute apart, in the given order.</summary>
    private static IReadOnlyList<Reading> Window(params decimal[] values) =>
        values.Select((v, i) => new Reading("CUX", v, T0.AddMinutes(i))).ToArray();

    private static GaugeSnapshot Snapshot(IReadOnlyList<Reading> window, AlertState? alert = null) =>
        new("CUX", window, alert);

    [Fact]
    public void Rate_is_positive_for_a_rising_window()
    {
        var dto = GaugeMapper.ToDto(Snapshot(Window(0.5m, 1.0m, 1.5m, 2.0m)), T0);

        // +0.5 m per 1-min step → slope 0.5 m/min.
        Assert.Equal(0.5m, dto.RateMetersPerMin);
    }

    [Fact]
    public void Rate_is_negative_for_a_receding_window()
    {
        var dto = GaugeMapper.ToDto(Snapshot(Window(2.0m, 1.5m, 1.0m, 0.5m)), T0);

        Assert.Equal(-0.5m, dto.RateMetersPerMin);
    }

    [Fact]
    public void Rate_is_zero_for_a_flat_window()
    {
        var dto = GaugeMapper.ToDto(Snapshot(Window(3.0m, 3.0m, 3.0m, 3.0m)), T0);

        Assert.Equal(0m, dto.RateMetersPerMin);
    }

    [Fact]
    public void Rate_keeps_its_sign_against_a_single_outlier()
    {
        // Steady rise with one spike near the end; a least-squares slope stays positive
        // where an endpoint difference would be distorted.
        var dto = GaugeMapper.ToDto(Snapshot(Window(0.5m, 1.0m, 1.5m, 9.0m, 2.5m)), T0);

        Assert.True(dto.RateMetersPerMin > 0);
    }

    [Fact]
    public void Rate_is_null_with_fewer_than_two_readings()
    {
        var dto = GaugeMapper.ToDto(Snapshot(Window(1.0m)), T0);

        Assert.Null(dto.RateMetersPerMin);
    }

    [Fact]
    public void Rate_is_null_when_all_readings_share_a_timestamp()
    {
        var window = new[]
        {
            new Reading("CUX", 1.0m, T0),
            new Reading("CUX", 2.0m, T0),
        };

        var dto = GaugeMapper.ToDto(Snapshot(window), T0);

        Assert.Null(dto.RateMetersPerMin);
    }

    [Fact]
    public void TimeInStage_is_elapsed_seconds_since_the_stage_changed()
    {
        var changedAt = T0;
        var alert = new AlertState("CUX", "warning", changedAt);
        var now = changedAt.AddMinutes(3);

        var dto = GaugeMapper.ToDto(Snapshot(Window(4.6m, 4.7m), alert), now);

        Assert.Equal(180, dto.TimeInStageSeconds);
    }

    [Fact]
    public void TimeInStage_is_null_when_no_alert_has_been_set()
    {
        var dto = GaugeMapper.ToDto(Snapshot(Window(1.0m, 1.1m)), T0);

        Assert.Null(dto.TimeInStageSeconds);
    }

    [Fact]
    public void TimeInStage_clamps_to_zero_under_clock_skew()
    {
        var alert = new AlertState("CUX", "warning", T0.AddMinutes(1));
        var now = T0;   // "now" earlier than the change — skew

        var dto = GaugeMapper.ToDto(Snapshot(Window(4.6m, 4.7m), alert), now);

        Assert.Equal(0, dto.TimeInStageSeconds);
    }

    [Fact]
    public void Window_extent_reports_min_and_max_values()
    {
        var dto = GaugeMapper.ToDto(Snapshot(Window(1.2m, 4.8m, 0.9m, 3.3m)), T0);

        Assert.Equal(0.9m, dto.WindowMin);
        Assert.Equal(4.8m, dto.WindowMax);
    }

    [Fact]
    public void Empty_window_yields_null_signals()
    {
        var dto = GaugeMapper.ToDto(Snapshot([]), T0);

        Assert.Null(dto.Level);
        Assert.Null(dto.RateMetersPerMin);
        Assert.Null(dto.WindowMin);
        Assert.Null(dto.WindowMax);
    }
}
