using Tidewatch.Contracts;

namespace Tidewatch.Ingestion.IntegrationTests;

/// <summary>
/// End-to-end coverage of the ingestion pipeline against a real broker: readings are
/// published to the exchange, consumed, deserialised, and run through the evaluator,
/// and the resulting stage is observed on the live state holder. Mirrors the boundary
/// and rising-trend unit cases, now across RabbitMQ rather than a direct evaluator call.
/// </summary>
public sealed class IngestionPipelineTests : IClassFixture<IngestionFixture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly IngestionFixture _fixture;

    public IngestionPipelineTests(IngestionFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Readings_over_the_warning_boundary_escalate_to_warning()
    {
        const string gauge = "BOUNDARY";
        var t0 = DateTimeOffset.UtcNow;

        // Five steady readings at 4.55 m: median over the 4.50 boundary, not rising.
        for (var i = 0; i < 5; i++)
            await _fixture.PublishAsync(new Reading(gauge, 4.55m, t0.AddSeconds(i)));

        var stage = await _fixture.WaitForStageAsync(gauge, "warning", Timeout);

        Assert.Equal("warning", stage);
    }

    [Fact]
    public async Task Rising_trend_below_the_boundary_pre_escalates_to_warning()
    {
        const string gauge = "TREND";
        var t0 = DateTimeOffset.UtcNow;

        // Ten ascending readings ending at 4.47 m — recent median below the 4.50 floor
        // but rising within the 0.15 trend margin. Only the trend rule reaches warning.
        decimal[] values =
        [
            4.20m, 4.21m, 4.22m, 4.23m, 4.24m,
            4.43m, 4.44m, 4.45m, 4.46m, 4.47m,
        ];
        for (var i = 0; i < values.Length; i++)
            await _fixture.PublishAsync(new Reading(gauge, values[i], t0.AddSeconds(i)));

        var stage = await _fixture.WaitForStageAsync(gauge, "warning", Timeout);

        Assert.Equal("warning", stage);
    }
}
