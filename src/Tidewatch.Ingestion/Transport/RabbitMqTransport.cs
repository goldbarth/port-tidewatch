using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Tidewatch.Ingestion.Transport;

/// <summary>
/// Owns the RabbitMQ connection, channel, and the exchange / queue / dead-letter
/// declarations. Kept apart from message processing — infrastructure only.
/// </summary>
public sealed class RabbitMqTransport : IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private IConnection? _connection;
    private IChannel? _channel;
    private IChannel? _publishChannel;

    public RabbitMqTransport(IOptions<RabbitMqOptions> options) => _options = options.Value;

    /// <summary>The open channel. Valid only after <see cref="InitializeAsync"/>.</summary>
    public IChannel Channel =>
        _channel ?? throw new InvalidOperationException("Transport not initialized.");

    public string Queue => _options.Queue;

    /// <summary>Fanout exchange alert events are published to.</summary>
    public string AlertExchange => _options.AlertExchange;

    /// <summary>
    /// Opens the connection and channel and declares the topology: main exchange,
    /// ingestion queue (dead-lettered), and the dead-letter exchange/queue.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
        };

        _connection = await factory.CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

        // Dead-letter topology first, so the main queue can reference it.
        await _channel.ExchangeDeclareAsync(
            _options.DeadLetterExchange, ExchangeType.Fanout, durable: true,
            cancellationToken: cancellationToken);
        await _channel.QueueDeclareAsync(
            _options.DeadLetterQueue, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: cancellationToken);
        await _channel.QueueBindAsync(
            _options.DeadLetterQueue, _options.DeadLetterExchange, routingKey: string.Empty,
            cancellationToken: cancellationToken);

        // Main topology, routing rejects to the dead-letter exchange.
        await _channel.ExchangeDeclareAsync(
            _options.Exchange, ExchangeType.Direct, durable: true,
            cancellationToken: cancellationToken);
        await _channel.QueueDeclareAsync(
            _options.Queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = _options.DeadLetterExchange,
            },
            cancellationToken: cancellationToken);
        await _channel.QueueBindAsync(
            _options.Queue, _options.Exchange, _options.RoutingKey,
            cancellationToken: cancellationToken);

        // Alert topology: a fanout exchange with a durable audit queue bound to it.
        // Independent subscribers (notification, etc.) declare their own queues against
        // the same exchange. A dedicated channel publishes alerts so it never contends
        // with the consume/ack channel.
        await _channel.ExchangeDeclareAsync(
            _options.AlertExchange, ExchangeType.Fanout, durable: true,
            cancellationToken: cancellationToken);
        await _channel.QueueDeclareAsync(
            _options.AlertAuditQueue, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: cancellationToken);
        await _channel.QueueBindAsync(
            _options.AlertAuditQueue, _options.AlertExchange, routingKey: string.Empty,
            cancellationToken: cancellationToken);

        _publishChannel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Publishes a serialized alert event to the fanout alert exchange. Uses the dedicated
    /// publish channel, kept separate from the consume/ack channel.
    /// </summary>
    public async Task PublishAlertAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        var channel = _publishChannel
            ?? throw new InvalidOperationException("Transport not initialized.");
        await channel.BasicPublishAsync(
            _options.AlertExchange, routingKey: string.Empty, mandatory: false,
            basicProperties: new BasicProperties { Persistent = true },
            body: body, cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_publishChannel is not null)
            await _publishChannel.DisposeAsync();
        if (_channel is not null)
            await _channel.DisposeAsync();
        if (_connection is not null)
            await _connection.DisposeAsync();
    }
}
