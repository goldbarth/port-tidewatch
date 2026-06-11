using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;
using Tidewatch.Contracts;
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

    private readonly RabbitMqContainer _broker = new RabbitMqBuilder("rabbitmq:3-management")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    private IHost _host = null!;
    private IConnection _publisherConnection = null!;
    private IChannel _publisherChannel = null!;

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
    }

    /// <summary>Publishes a reading to the ingestion exchange.</summary>
    public async Task PublishAsync(Reading reading)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(reading);
        await _publisherChannel.BasicPublishAsync(
            Exchange, RoutingKey, mandatory: false,
            new BasicProperties { Persistent = true }, body);
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
