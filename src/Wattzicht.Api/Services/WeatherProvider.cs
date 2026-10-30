using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace Wattzicht.Api.Services;

/// <summary>Weerdata voor de prestatiecheck.</summary>
public interface IWeatherProvider
{
    /// <summary>Breedte- en lengtegraad van een Nederlandse plaats, of <c>null</c> als onbekend.</summary>
    Task<(double Lat, double Lon)?> GeocodeAsync(string place, CancellationToken ct);

    /// <summary>Zonnestraling per dag (kWh/m²) voor de laatste ~31 dagen tot en met vandaag.</summary>
    Task<IReadOnlyDictionary<DateOnly, double>> DailyIrradianceAsync(double lat, double lon, CancellationToken ct);
}

/// <summary>
/// Open-Meteo (gratis, geen sleutel). Straling = <c>shortwave_radiation_sum</c> in MJ/m², omgerekend naar kWh/m² (÷ 3,6).
/// Resultaten worden 3 uur per locatie bewaard.
/// </summary>
public sealed class OpenMeteoWeatherProvider(
    IHttpClientFactory httpClientFactory,
    TimeProvider clock,
    ILogger<OpenMeteoWeatherProvider> logger) : IWeatherProvider
{
    public const string HttpClientName = "open-meteo";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(3);

    private readonly ConcurrentDictionary<string, (DateTimeOffset At, IReadOnlyDictionary<DateOnly, double> Data)> _cache = new();

    public async Task<(double Lat, double Lon)?> GeocodeAsync(string place, CancellationToken ct)
    {
        var url = "https://geocoding-api.open-meteo.com/v1/search?count=1&language=nl&countryCode=NL&name="
                  + Uri.EscapeDataString(place.Trim());
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var doc = JsonDocument.Parse(await client.GetStringAsync(url, ct));
            if (doc.RootElement.TryGetProperty("results", out var results)
                && results.ValueKind == JsonValueKind.Array
                && results.GetArrayLength() > 0)
            {
                var first = results[0];
                return (first.GetProperty("latitude").GetDouble(), first.GetProperty("longitude").GetDouble());
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            logger.LogWarning(ex, "Plaats {Place} kon niet opgezocht worden", place);
        }

        return null;
    }

    public async Task<IReadOnlyDictionary<DateOnly, double>> DailyIrradianceAsync(double lat, double lon, CancellationToken ct)
    {
        var key = string.Create(CultureInfo.InvariantCulture, $"{lat:0.00},{lon:0.00}");
        var now = clock.GetUtcNow();
        if (_cache.TryGetValue(key, out var cached) && now - cached.At < CacheDuration)
        {
            return cached.Data;
        }

        var url = string.Create(CultureInfo.InvariantCulture,
            $"https://api.open-meteo.com/v1/forecast?latitude={lat:0.00}&longitude={lon:0.00}&daily=shortwave_radiation_sum&past_days=31&forecast_days=1&timezone=Europe%2FAmsterdam");
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var doc = JsonDocument.Parse(await client.GetStringAsync(url, ct));
            var data = Parse(doc.RootElement);
            _cache[key] = (now, data);
            return data;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Weerdata van Open-Meteo ophalen mislukt");
            return cached.Data ?? new Dictionary<DateOnly, double>();
        }
    }

    /// <summary>Leest <c>{"daily":{"time":["2026-10-01"],"shortwave_radiation_sum":[7.2]}}</c>.</summary>
    public static IReadOnlyDictionary<DateOnly, double> Parse(JsonElement root)
    {
        var result = new Dictionary<DateOnly, double>();
        if (!root.TryGetProperty("daily", out var daily)
            || !daily.TryGetProperty("time", out var times)
            || !daily.TryGetProperty("shortwave_radiation_sum", out var values))
        {
            return result;
        }

        var count = Math.Min(times.GetArrayLength(), values.GetArrayLength());
        for (var i = 0; i < count; i++)
        {
            if (values[i].ValueKind != JsonValueKind.Number
                || times[i].ValueKind != JsonValueKind.String
                || !DateOnly.TryParse(times[i].GetString(), CultureInfo.InvariantCulture, out var date))
            {
                continue;
            }

            result[date] = Math.Round(values[i].GetDouble() / 3.6, 3); // MJ/m² → kWh/m²
        }

        return result;
    }
}
