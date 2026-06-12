using Tidewatch.Contracts;

namespace Tidewatch.Ingestion.IntegrationTests;

/// <summary>
/// End-to-end coverage of alert-event publishing (#30, ADR-001 v1.1.0): a genuine stage
/// change at the <c>ApplyStageChange</c> chokepoint publishes exactly one
/// <see cref="AlertEvent"/> to the fanout alert exchange; a held stage publishes none.
/// </summary>
public sealed class AlertEventTests : IClassFixture<IngestionFixture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SettleGrace = TimeSpan.FromSeconds(1);

    private readonly IngestionFixture _fixture;

    public AlertEventTests(IngestionFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_stage_transition_publishes_exactly_one_alert_event()
    {
        const string gauge = "ALERT-RISE";
        var t0 = DateTimeOffset.UtcNow;

        // Five steady readings over the warning boundary: the gauge crosses normal →
        // warning once, then holds. Exactly one transition, so exactly one event.
        for (var i = 0; i < 5; i++)
            await _fixture.PublishAsync(new Reading(gauge, 4.55m, t0.AddSeconds(i)));

        await _fixture.WaitForStageAsync(gauge, "warning", Timeout);
        var alerts = await _fixture.WaitForAlertsAsync(gauge, 1, Timeout);
        await Task.Delay(SettleGrace); // allow any spurious extra event to arrive

        var alert = Assert.Single(_fixture.AlertsFor(gauge));
        Assert.Equal("normal", alert.PreviousStage);
        Assert.Equal("warning", alert.NewStage);
        Assert.Equal(4.55m, alert.Level);
        Assert.Equal(gauge, alert.GaugeId);
        Assert.NotEmpty(alerts);
    }

    [Fact]
    public async Task A_held_stage_publishes_no_alert_event()
    {
        const string gauge = "ALERT-CALM";
        var t0 = DateTimeOffset.UtcNow;

        // Steady normal readings: the gauge only ever establishes normal, which is not a
        // transition, so no alert event is published.
        for (var i = 0; i < 5; i++)
            await _fixture.PublishAsync(new Reading(gauge, 1.00m, t0.AddSeconds(i)));

        await _fixture.WaitForStageAsync(gauge, "normal", Timeout);
        await Task.Delay(SettleGrace); // give any event time to arrive if one were published

        Assert.Empty(_fixture.AlertsFor(gauge));
    }
}
