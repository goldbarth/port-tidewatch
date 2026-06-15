using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Tidewatch.Contracts;
using Tidewatch.Source.Configuration;

namespace Tidewatch.Source.Publishing;

/// <summary>
/// Publishes <see cref="Reading"/> messages to the readings exchange. Owns the connection
/// and channel, opened lazily on first publish, and declares the exchange. One producer
/// span per reading is the trace root; RabbitMQ.Client adds its own publisher activity and
/// injects the W3C context into the message headers so the ingestion service continues the
/// same trace.
/// </summary>
public sealed class RabbitMqReadingPublisher : IReadingPublisher, IAsyncDisposable
{
    /// <summary>Source name; registered with OpenTelemetry in Program.</summary>
    public const string ActivitySourceName = "Tidewatch.Source";

    private static readonly ActivitySource Source = new(ActivitySourceName);

    private readonly PublisherOptions _options;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqReadingPublisher(IOptions<PublisherOptions> options) => _options = options.Value;

    public async Task PublishAsync(Reading reading, CancellationToken cancellationToken)
    {
        var channel = await EnsureChannelAsync(cancellationToken);
        var body = JsonSerializer.SerializeToUtf8Bytes(reading);

        using var activity = Source.StartActivity("publish reading", ActivityKind.Producer);
        activity?.SetTag("gauge.id", reading.GaugeId);
        activity?.SetTag("reading.value_m", (double)reading.Value);

        await channel.BasicPublishAsync(
            _options.Exchange, _options.RoutingKey, mandatory: false,
            basicProperties: new BasicProperties { Persistent = true },
            body: body, cancellationToken: cancellationToken);
    }

    private async Task<IChannel> EnsureChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null) return _channel;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_channel is not null) return _channel;

            var factory = new ConnectionFactory { HostName = _options.HostName };
            _connection = await factory.CreateConnectionAsync(cancellationToken);
            var channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
            await channel.ExchangeDeclareAsync(
                _options.Exchange, ExchangeType.Direct, durable: true,
                cancellationToken: cancellationToken);
            _channel = channel;
            return _channel;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null) await _channel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
        _initLock.Dispose();
    }
}
