namespace Tidewatch.Ingestion.Transport;

/// <summary>
/// Bound to the <c>RabbitMq</c> section of appsettings. Connection details and the
/// exchange / queue / dead-letter names declared by <see cref="RabbitMqTransport"/>.
/// </summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";

    /// <summary>Exchange readings are published to and the consumer binds against.</summary>
    public string Exchange { get; set; } = "tidewatch.readings";

    /// <summary>Ingestion queue the consumer listens on.</summary>
    public string Queue { get; set; } = "tidewatch.ingestion";

    /// <summary>Routing key bound between exchange and ingestion queue.</summary>
    public string RoutingKey { get; set; } = "reading";

    /// <summary>Dead-letter exchange for rejected messages.</summary>
    public string DeadLetterExchange { get; set; } = "tidewatch.readings.dlx";

    /// <summary>Dead-letter queue holding rejected messages.</summary>
    public string DeadLetterQueue { get; set; } = "tidewatch.ingestion.dead";
}
