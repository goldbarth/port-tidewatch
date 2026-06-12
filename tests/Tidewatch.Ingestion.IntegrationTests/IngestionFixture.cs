using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Testcontainers.RabbitMq;
using Tidewatch.Contracts;
using Tidewatch.Ingestion.Alerting;
using Tidewatch.Ingestion.Configuration;
using Tidewatch.Ingestion.Consumer;
using Tidewatch.Ingestion.Evaluation;
using Tidewatch.Ingestion.State;
using Tidewatch.Ingestion.Transport;

namespace Tidewatch.Ingestion.IntegrationTests;

/// <summary>
/// Spins up a real RabbitMQ broker via Testcontainers and hosts the ingestion pipeline
/// against it (transport, consumer, state holder, evaluator). Mirrors the wiring in
/// Program.cs but deliberately leaves out OpenTelemetry — the tests assert processing
/// behaviour, not telemetry, and an OTLP exporter has no collector to reach here.
/// </summary>
public sealed class IngestionFixture : IAsyncLifetime
{
    private const string Exchange = "tidewatch.readings";
    private const string RoutingKey = "reading";
    private const string Queue = "tidewatch.ingestion";
    private const string AlertExchange = "tidewatch.alerts";

    private readonly RabbitMqContainer _broker = new RabbitMqBuilder("rabbitmq:3-management")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    private IHost _host = null!;
    private IConnection _publisherConnection = null!;
    private IChannel _publisherChannel = null!;
    private IChannel _alertChannel = null!;
    private readonly ConcurrentQueue<AlertEvent> _alerts = new();

    /// <summary>The live state holder resolved from the hosted pipeline.</summary>
    public GaugeStateHolder State => _host.Services.GetRequiredService<GaugeStateHolder>();

    public async Task InitializeAsync()
    {
        await _broker.StartAsync();

        var port = _broker.GetMappedPublicPort(5672);

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMq:HostName"] = _broker.Hostname,
            ["RabbitMq:Port"] = port.ToString(),
            ["RabbitMq:UserName"] = "guest",
            ["RabbitMq:Password"] = "guest",
            ["SurgeThresholds:Reference"] = "NHN",
            ["SurgeThresholds:TrendWindow"] = "00:30:00",
            ["SurgeThresholds:Stages:0:Name"] = "normal",
            ["SurgeThresholds:Stages:0:MinMeters"] = "0",
            ["SurgeThresholds:Stages:1:Name"] = "warning",
            ["SurgeThresholds:Stages:1:MinMeters"] = "4.5",
            ["SurgeThresholds:Stages:2:Name"] = "severe",
            ["SurgeThresholds:Stages:2:MinMeters"] = "5.5",
        });

        // Mirror Program.cs registrations (without OpenTelemetry).
        builder.Services
            .AddOptions<SurgeThresholdOptions>()
            .Bind(builder.Configuration.GetSection(SurgeThresholdOptions.SectionName));
        builder.Services
            .AddOptions<RabbitMqOptions>()
            .Bind(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
        builder.Services.AddSingleton<RabbitMqTransport>();
        builder.Services.AddSingleton<IAlertPublisher, RabbitMqAlertPublisher>();
        builder.Services.AddSingleton<GaugeStateHolder>();
        builder.Services.AddSingleton<ISurgeEvaluator, SurgeEvaluator>();
        builder.Services.AddHostedService<ReadingConsumer>();

        _host = builder.Build();
        await _host.StartAsync();

        var factory = new ConnectionFactory
        {
            HostName = _broker.Hostname,
            Port = port,
            UserName = "guest",
            Password = "guest",
        };
        _publisherConnection = await factory.CreateConnectionAsync();
        _publisherChannel = await _publisherConnection.CreateChannelAsync();

        await WaitForConsumerTopologyAsync();
        await SubscribeToAlertsAsync();
    }

    /// <summary>
    /// Binds a private exclusive queue to the fanout alert exchange and collects every
    /// published <see cref="AlertEvent"/>. The exchange is declared defensively (idempotent,
    /// matching the service) so the subscription does not race the consumer's declaration.
    /// </summary>
    private async Task SubscribeToAlertsAsync()
    {
        _alertChannel = await _publisherConnection.CreateChannelAsync();
        await _alertChannel.ExchangeDeclareAsync(AlertExchange, ExchangeType.Fanout, durable: true);
        var queue = await _alertChannel.QueueDeclareAsync(
            queue: string.Empty, durable: false, exclusive: true, autoDelete: true);
        await _alertChannel.QueueBindAsync(queue.QueueName, AlertExchange, routingKey: string.Empty);

        var consumer = new AsyncEventingBasicConsumer(_alertChannel);
        consumer.ReceivedAsync += (_, ea) =>
        {
            var evt = JsonSerializer.Deserialize<AlertEvent>(ea.Body.Span);
            if (evt is not null)
                _alerts.Enqueue(evt);
            return Task.CompletedTask;
        };
        await _alertChannel.BasicConsumeAsync(queue.QueueName, autoAck: true, consumer);
    }

    /// <summary>Publishes a reading to the ingestion exchange.</summary>
    public async Task PublishAsync(Reading reading)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(reading);
        await _publisherChannel.BasicPublishAsync(
            Exchange, RoutingKey, mandatory: false,
            new BasicProperties { Persistent = true }, body);
    }

    /// <summary>Alert events received so far for a gauge, in arrival order.</summary>
    public IReadOnlyList<AlertEvent> AlertsFor(string gaugeId) =>
        _alerts.Where(a => a.GaugeId == gaugeId).ToArray();

    /// <summary>
    /// Polls until at least <paramref name="count"/> alert events have arrived for the
    /// gauge or the timeout elapses. Returns whatever has arrived (so an assertion can
    /// show the actual count).
    /// </summary>
    public async Task<IReadOnlyList<AlertEvent>> WaitForAlertsAsync(
        string gaugeId, int count, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var matching = AlertsFor(gaugeId);
            if (matching.Count >= count)
                return matching;
            await Task.Delay(100);
        }
        return AlertsFor(gaugeId);
    }

    /// <summary>
    /// Polls the state holder until the gauge reaches <paramref name="expected"/> or the
    /// timeout elapses. Returns the last observed stage (so a failed assertion can show
    /// what was actually reached).
    /// </summary>
    public async Task<string?> WaitForStageAsync(string gaugeId, string expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        string? stage = null;
        while (DateTime.UtcNow < deadline)
        {
            stage = State.GetAlertState(gaugeId)?.Stage;
            if (string.Equals(stage, expected, StringComparison.Ordinal))
                return stage;
            await Task.Delay(100);
        }
        return stage;
    }

    /// <summary>
    /// Waits until the consumer has declared its queue. Until the queue exists and is
    /// bound, a direct-exchange publish has nowhere to route and is silently dropped, so
    /// tests must not publish before this completes. A passive declare faults the channel
    /// when the queue is absent, so each attempt uses a throwaway channel.
    /// </summary>
    private async Task WaitForConsumerTopologyAsync()
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            await using var probe = await _publisherConnection.CreateChannelAsync();
            try
            {
                await probe.QueueDeclarePassiveAsync(Queue);
                return;
            }
            catch
            {
                await Task.Delay(200);
            }
        }
        throw new TimeoutException($"Consumer did not declare queue '{Queue}' in time.");
    }

    public async Task DisposeAsync()
    {
        if (_alertChannel is not null)
            await _alertChannel.DisposeAsync();
        if (_publisherChannel is not null)
            await _publisherChannel.DisposeAsync();
        if (_publisherConnection is not null)
            await _publisherConnection.DisposeAsync();
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        await _broker.DisposeAsync();
    }
}
