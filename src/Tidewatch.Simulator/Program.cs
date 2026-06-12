using System.Diagnostics;
using System.Text.Json;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RabbitMQ.Client;
using Tidewatch.Contracts;

// Emits Reading messages for a set of gauges to the exchange. Knows nothing about
// thresholds — it only produces input.

const string exchange = "tidewatch.readings";
const string routingKey = "reading";
var interval = TimeSpan.FromSeconds(2);

// Each gauge has a role driving its signal (see GaugeProfile below): one gauge runs a
// scripted surge that crosses warning (4.50 m) and severe (5.50 m) then recedes; the
// rest stay normal for contrast. Level = baseline + tide + surge + small noise. The
// surge is a smooth raised-cosine bump so the rise is monotone and the evaluator does
// not flap. Surge peak/period are parameterisable via env (SURGE_PEAK_M / SURGE_PERIOD_S).
const decimal tideAmplitude = 0.30m;            // gentle rolling tide, never trips warning alone
const double tidePeriodS = 60.0;                // seconds per tide cycle
const decimal noiseAmplitude = 0.02m;           // tiny — keeps trend clean
const decimal defaultSurgePeak = 5.80m;         // absolute level the surge gauge peaks at
const double defaultSurgePeriodS = 180.0;       // one full surge cycle every 3 min

var surgePeak = decimal.TryParse(Environment.GetEnvironmentVariable("SURGE_PEAK_M"), out var p)
    ? p : defaultSurgePeak;
var surgePeriodS = double.TryParse(Environment.GetEnvironmentVariable("SURGE_PERIOD_S"), out var s)
    ? s : defaultSurgePeriodS;
var surgeDurationS = surgePeriodS * 0.66;        // bump occupies most of the cycle, then rests

GaugeProfile[] profiles =
[
    new("CUX", Baseline: 0.80m, Surges: true),   // storm-surge gauge: normal → warning → severe → recede
    new("HEL", Baseline: 0.50m, Surges: false),  // stays normal throughout, for contrast
    new("STP", Baseline: 1.00m, Surges: false),  // mild tide, normal
    new("BHV", Baseline: 0.70m, Surges: false),  // mild tide, normal
];

// One span per published reading is the trace root. RabbitMQ.Client creates its own
// publisher activity inside BasicPublishAsync — but only while a listener on
// PublisherSourceName is registered — and then injects the W3C trace context into the
// message headers, so the ingestion service can continue the same trace.
using var simSource = new ActivitySource("Tidewatch.Simulator");
using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .ConfigureResource(r => r.AddService("tidewatch-simulator"))
    .AddSource("Tidewatch.Simulator")
    .AddSource(RabbitMQActivitySource.PublisherSourceName)
    .AddOtlpExporter()
    .Build();

var factory = new ConnectionFactory
{
    HostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost",
};

await using var connection = await factory.CreateConnectionAsync();
await using var channel = await connection.CreateChannelAsync();

await channel.ExchangeDeclareAsync(exchange, ExchangeType.Direct, durable: true);

var rng = new Random();
var startedAt = DateTimeOffset.UtcNow;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

Console.WriteLine($"Simulating {profiles.Length} gauges (surge peak {surgePeak:0.00} m, "
    + $"period {surgePeriodS:0}s). Ctrl+C to stop.");

while (!cts.IsCancellationRequested)
{
    var now = DateTimeOffset.UtcNow;
    var t = (now - startedAt).TotalSeconds;

    foreach (var profile in profiles)
    {
        var level = LevelAt(profile, t);

        var reading = new Reading(profile.Id, decimal.Round(level, 3), now);
        var body = JsonSerializer.SerializeToUtf8Bytes(reading);

        using var activity = simSource.StartActivity("publish reading", ActivityKind.Producer);
        activity?.SetTag("gauge.id", reading.GaugeId);
        activity?.SetTag("reading.value_m", (double)reading.Value);

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

// Composite level for a gauge at elapsed time t (seconds): tide baseline plus, for the
// surge gauge, a smooth raised-cosine bump, plus tiny noise.
decimal LevelAt(GaugeProfile profile, double t)
{
    var tide = tideAmplitude * (decimal)Math.Sin(2 * Math.PI * t / tidePeriodS);
    var noise = (decimal)(rng.NextDouble() - 0.5) * 2 * noiseAmplitude;
    var surge = profile.Surges ? SurgeBump(t, profile.Baseline) : 0m;
    return profile.Baseline + tide + surge + noise;
}

// Raised-cosine bump: 0 at the start/end of its window, peaking mid-window. Active for
// surgeDurationS out of every surgePeriodS, so the gauge rests at baseline between events.
// Height is chosen so the peak reaches surgePeak above the surge gauge's baseline.
decimal SurgeBump(double t, decimal baseline)
{
    var phase = t % surgePeriodS;
    if (phase >= surgeDurationS) return 0m;       // resting between surges
    var height = surgePeak - baseline;
    var shape = 0.5 * (1 - Math.Cos(2 * Math.PI * phase / surgeDurationS));
    return height * (decimal)shape;
}

// A gauge's signal role. Baseline is its calm level (m NHN); Surges marks the one gauge
// that runs the scripted storm-surge event.
record GaugeProfile(string Id, decimal Baseline, bool Surges);
