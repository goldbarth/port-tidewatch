namespace Tidewatch.Source.Configuration;

/// <summary>
/// Bound to the <c>RabbitMq</c> section of appsettings. The publish target — exchange and
/// routing key match what <c>Tidewatch.Ingestion</c> binds its consumer against. The
/// documented <c>RABBITMQ_HOST</c> environment variable still wins (post-configure in
/// Program).
/// </summary>
public sealed class PublisherOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = "localhost";

    /// <summary>Exchange readings are published to. Must match the ingestion exchange.</summary>
    public string Exchange { get; set; } = "tidewatch.readings";

    /// <summary>Routing key for published readings. Must match the ingestion binding.</summary>
    public string RoutingKey { get; set; } = "reading";
}
