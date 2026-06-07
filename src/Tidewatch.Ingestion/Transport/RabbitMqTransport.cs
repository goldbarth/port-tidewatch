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

    public RabbitMqTransport(IOptions<RabbitMqOptions> options) => _options = options.Value;

    /// <summary>The open channel. Valid only after <see cref="InitializeAsync"/>.</summary>
    public IChannel Channel =>
        _channel ?? throw new InvalidOperationException("Transport not initialized.");

    public string Queue => _options.Queue;

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
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
            await _channel.DisposeAsync();
        if (_connection is not null)
            await _connection.DisposeAsync();
    }
}
