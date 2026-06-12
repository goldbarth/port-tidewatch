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

    /// <summary>
    /// Fanout exchange alert events are published to on a genuine stage change. Fanout so
    /// independent consumers (notification, audit) can subscribe without coordinating
    /// routing keys (ADR-001, v1.1.0).
    /// </summary>
    public string AlertExchange { get; set; } = "tidewatch.alerts";

    /// <summary>Durable queue bound to the alert exchange — a persistent audit record.</summary>
    public string AlertAuditQueue { get; set; } = "tidewatch.alerts.audit";
}
