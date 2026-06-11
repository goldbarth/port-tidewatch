using System.Diagnostics;

namespace Tidewatch.Ingestion.Telemetry;

/// <summary>
/// Single instrumentation handle for the ingestion path. The source name is
/// registered with the tracer provider in Program.cs and used to start spans
/// in the consumer.
/// </summary>
public static class IngestionTelemetry
{
    public const string SourceName = "Tidewatch.Ingestion";

    public static readonly ActivitySource Source = new(SourceName);
}
