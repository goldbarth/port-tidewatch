using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Tidewatch.Source.Configuration;

namespace Tidewatch.Source.Pegelonline;

/// <summary>
/// Reads the PEGELONLINE REST-API. Latest value via <c>currentmeasurement.json</c>, window
/// backfill via <c>measurements.json?start=...</c>, and the station's PNP elevation via the
/// station object's <c>gaugeZero.value</c>. Honours the API's <c>ETag</c>: the latest-value
/// poll sends <c>If-None-Match</c> and a <c>304</c> returns null, so no duplicate reading is
/// emitted. Singleton — it caches per-URL ETags across polls.
/// </summary>
public sealed class PegelonlineClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PegelonlineOptions _options;
    private readonly Dictionary<string, EntityTagHeaderValue> _etags = new();

    public PegelonlineClient(IHttpClientFactory httpClientFactory, IOptions<PegelonlineOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    /// <summary>The station's PNP elevation (m above NHN), or null if unavailable.</summary>
    public async Task<decimal?> GetGaugeZeroAsync(string uuid, CancellationToken cancellationToken)
    {
        var url = $"{_options.BaseUrl}/stations/{uuid}.json";
        using var client = _httpClientFactory.CreateClient();
        var station = await client.GetFromJsonAsync<PegelStation>(url, Json, cancellationToken);
        return station?.GaugeZero?.Value;
    }

    /// <summary>
    /// The latest W measurement (cm above PNP), or null when the server answers <c>304 Not
    /// Modified</c> — i.e. the value is unchanged since the last poll.
    /// </summary>
    public async Task<PegelMeasurement?> GetCurrentAsync(string uuid, CancellationToken cancellationToken)
    {
        var url = $"{_options.BaseUrl}/stations/{uuid}/W/currentmeasurement.json";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (_etags.TryGetValue(url, out var etag))
            request.Headers.IfNoneMatch.Add(etag);

        using var client = _httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotModified)
            return null;

        response.EnsureSuccessStatusCode();

        if (response.Headers.ETag is { } newEtag)
            _etags[url] = newEtag;

        return await response.Content.ReadFromJsonAsync<PegelMeasurement>(Json, cancellationToken);
    }

    /// <summary>W measurements from <paramref name="start"/> to now, for window backfill.</summary>
    public async Task<IReadOnlyList<PegelMeasurement>> GetMeasurementsAsync(
        string uuid, DateTimeOffset start, CancellationToken cancellationToken)
    {
        var url = $"{_options.BaseUrl}/stations/{uuid}/W/measurements.json?start={start:O}";
        using var client = _httpClientFactory.CreateClient();
        var measurements = await client.GetFromJsonAsync<List<PegelMeasurement>>(url, Json, cancellationToken);
        return measurements ?? [];
    }
}
