using System.Text.Json;
using RabbitMQ.Client;
using Tidewatch.Contracts;

// Emits Reading messages for a set of gauges to the exchange. Knows nothing about
// thresholds — it only produces input.

const string exchange = "tidewatch.readings";
const string routingKey = "reading";
string[] gauges = ["CUX", "HEL", "WSV", "BHV"];
var interval = TimeSpan.FromSeconds(2);

var factory = new ConnectionFactory
{
    HostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost",
};

await using var connection = await factory.CreateConnectionAsync();
await using var channel = await connection.CreateChannelAsync();

await channel.ExchangeDeclareAsync(exchange, ExchangeType.Direct, durable: true);

var rng = new Random();
var levels = gauges.ToDictionary(g => g, _ => 0.5m);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

Console.WriteLine($"Simulating {gauges.Length} gauges. Ctrl+C to stop.");

while (!cts.IsCancellationRequested)
{
    foreach (var gauge in gauges)
    {
        // Random walk around the current level, clamped to a plausible range.
        var delta = (decimal)(rng.NextDouble() - 0.45) * 0.3m;
        levels[gauge] = Math.Clamp(levels[gauge] + delta, -2.0m, 5.0m);

        var reading = new Reading(gauge, decimal.Round(levels[gauge], 3), DateTimeOffset.UtcNow);
        var body = JsonSerializer.SerializeToUtf8Bytes(reading);

        await channel.BasicPublishAsync(
            exchange, routingKey, mandatory: false,
            basicProperties: new BasicProperties { Persistent = true },
            body: body);

        Console.WriteLine($"{reading.GaugeId} {reading.Value:0.000} m @ {reading.Timestamp:HH:mm:ss}");
    }

    try { await Task.Delay(interval, cts.Token); }
    catch (TaskCanceledException) { break; }
}

Console.WriteLine("Stopped.");
