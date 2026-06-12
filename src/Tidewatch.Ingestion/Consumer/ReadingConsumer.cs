using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Tidewatch.Contracts;
using Tidewatch.Ingestion.Evaluation;
using Tidewatch.Ingestion.State;
using Tidewatch.Ingestion.Telemetry;
using Tidewatch.Ingestion.Transport;

namespace Tidewatch.Ingestion.Consumer;

/// <summary>
/// Background service listening on the ingestion queue. Deserialises each message to a
/// <see cref="Reading"/>; on deserialisation or basic validity failure it nacks the
/// message to the dead-letter queue. Valid readings are added to the state holder and
/// the gauge's stage is re-evaluated; a stage change is applied through the state
/// holder's single isolated point. No threshold logic lives here — that is the
/// evaluator's job.
/// </summary>
public sealed class ReadingConsumer : BackgroundService
{
    private readonly RabbitMqTransport _transport;
    private readonly GaugeStateHolder _state;
    private readonly ISurgeEvaluator _evaluator;
    private readonly ILogger<ReadingConsumer> _logger;

    public ReadingConsumer(
        RabbitMqTransport transport,
        GaugeStateHolder state,
        ISurgeEvaluator evaluator,
        ILogger<ReadingConsumer> logger)
    {
        _transport = transport;
        _state = state;
        _evaluator = evaluator;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _transport.InitializeAsync(stoppingToken);
        var channel = _transport.Channel;

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, ea) => HandleAsync(channel, ea, stoppingToken);

        await channel.BasicConsumeAsync(
            _transport.Queue, autoAck: false, consumer, stoppingToken);

        // Keep the service alive until shutdown.
        await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { });
    }

    private async Task HandleAsync(
        IChannel channel, BasicDeliverEventArgs ea, CancellationToken cancellationToken)
    {
        // Continue the trace started by the publisher: the W3C context rides in the
        // message headers and is recovered with RabbitMQ.Client's own extractor.
        var parentContext = RabbitMQActivitySource.ContextExtractor(ea.BasicProperties);
        using var activity = IngestionTelemetry.Source.StartActivity(
            "ingest reading", ActivityKind.Consumer, parentContext);

        Reading? reading;
        try
        {
            reading = JsonSerializer.Deserialize<Reading>(ea.Body.Span);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Malformed reading; dead-lettering.");
            activity?.AddEvent(new ActivityEvent("dead-lettered: malformed"));
            activity?.SetStatus(ActivityStatusCode.Error, "malformed");
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false,
                cancellationToken);
            return;
        }

        if (!IsValid(reading))
        {
            _logger.LogWarning("Invalid reading; dead-lettering.");
            activity?.AddEvent(new ActivityEvent("dead-lettered: invalid"));
            activity?.SetStatus(ActivityStatusCode.Error, "invalid");
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false,
                cancellationToken);
            return;
        }

        // Store the reading, then re-evaluate the gauge's stage from its window. Only a
        // genuine stage change is applied — ApplyStageChange also stamps ChangedAt, so
        // calling it on every reading would reset the "last change" timestamp.
        _state.Add(reading!);

        var currentStage = _state.GetAlertState(reading!.GaugeId)?.Stage;
        var newStage = _evaluator.Evaluate(
            reading.GaugeId, _state.GetWindow(reading.GaugeId), currentStage);

        activity?.SetTag("gauge.id", reading.GaugeId);
        activity?.SetTag("surge.stage.previous", currentStage);
        activity?.SetTag("surge.stage.current", newStage);

        if (!string.Equals(newStage, currentStage, StringComparison.Ordinal))
        {
            activity?.AddEvent(new ActivityEvent("stage changed"));
            await _state.ApplyStageChange(
                reading.GaugeId, newStage, reading.Value, reading.Timestamp);
        }

        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken);
    }

    /// <summary>Basic plausibility check before a reading is processed.</summary>
    private static bool IsValid([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] Reading? reading) =>
        reading is not null
        && !string.IsNullOrWhiteSpace(reading.GaugeId)
        && reading.Value is > -20m and < 20m
        && reading.Timestamp != default;
}
