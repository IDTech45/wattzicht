using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Wattzicht.Shared;

namespace Wattzicht.Api.Services;

/// <summary>Weerdata voor de prestatiecheck.</summary>
public interface IWeatherProvider
{
    /// <summary>Breedte- en lengtegraad van een Nederlandse plaats, of <c>null</c> als onbekend.</summary>
    Task<(double Lat, double Lon)?> GeocodeAsync(string place, CancellationToken ct);

    /// <summary>Zonnestraling per dag (kWh/m²) voor de laatste ~31 dagen tot en met vandaag.</summary>
    Task<IReadOnlyDictionary<DateOnly, double>> DailyIrradianceAsync(double lat, double lon, CancellationToken ct);

    /// <summary>
    /// Verwachte zonnestraling per uur (W/m²) voor de komende twee dagen.
    ///
    /// De dagwaarden hierboven kijken terug en zijn bedoeld om te controleren of de panelen nog doen
    /// wat ze moeten doen. Hiermee kijken we vooruit: alleen als je weet hoeveel zon er morgenmiddag
    /// staat, kun je zeggen of iemand zijn auto beter dan kan laden dan vannacht van het net.
    /// </summary>
    Task<IReadOnlyList<HourlyIrradiance>> HourlyForecastAsync(double lat, double lon, CancellationToken ct);

    /// <summary>
    /// Het weer per dag voor vandaag en morgen: temperatuur, zonuren en wat voor dag het is.
    ///
    /// De twee methodes hierboven leveren straling, en daar kun je een paneel mee narekenen maar
    /// geen beginscherm mee vullen. Een bewoner die zijn app opent wil eerst weten of het vandaag
    /// mooi weer is; dat is de context waarin de rest van de cijfers pas iets betekenen.
    /// </summary>
    Task<IReadOnlyList<DagWeer>> DailyForecastAsync(double lat, double lon, CancellationToken ct);
}

/// <summary>Het weer van één dag zoals Open-Meteo het verwacht.</summary>
/// <param name="Datum">De dag in Nederlandse tijd.</param>
/// <param name="Soort">De WMO-code teruggebracht tot wat de app kan tekenen.</param>
/// <param name="TempMaxC">Hoogste temperatuur in graden Celsius.</param>
/// <param name="TempMinC">Laagste temperatuur.</param>
/// <param name="ZonUren">Uren zon; Open-Meteo geeft dit in seconden.</param>
public sealed record DagWeer(
    DateOnly Datum,
    WeerSoort Soort,
    double TempMaxC,
    double TempMinC,
    double ZonUren);

/// <summary>Verwachte zonnestraling voor één uur.</summary>
/// <param name="StartUtc">Begin van het uur in UTC.</param>
/// <param name="WattPerM2">Gemiddelde straling in dat uur, in watt per vierkante meter.</param>
public sealed record HourlyIrradiance(DateTimeOffset StartUtc, double WattPerM2);

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
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, IReadOnlyList<HourlyIrradiance> Data)> _uurCache = new();
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, IReadOnlyList<DagWeer> Data)> _dagCache = new();

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

    public async Task<IReadOnlyList<HourlyIrradiance>> HourlyForecastAsync(double lat, double lon, CancellationToken ct)
    {
        var key = string.Create(CultureInfo.InvariantCulture, $"uur:{lat:0.00},{lon:0.00}");
        var now = clock.GetUtcNow();
        if (_uurCache.TryGetValue(key, out var cached) && now - cached.At < CacheDuration)
        {
            return cached.Data;
        }

        // Tijden in UTC opvragen: alles binnen in rekent in UTC, en zo hoeven we hier niet met
        // zomertijd te goochelen op precies het moment dat die overgaat.
        var url = string.Create(CultureInfo.InvariantCulture,
            $"https://api.open-meteo.com/v1/forecast?latitude={lat:0.00}&longitude={lon:0.00}&hourly=shortwave_radiation&forecast_days=2&timezone=UTC");
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var doc = JsonDocument.Parse(await client.GetStringAsync(url, ct));
            var data = ParseHourly(doc.RootElement);
            _uurCache[key] = (now, data);
            return data;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Zonverwachting van Open-Meteo ophalen mislukt");
            return cached.Data ?? [];
        }
    }

    public async Task<IReadOnlyList<DagWeer>> DailyForecastAsync(double lat, double lon, CancellationToken ct)
    {
        var key = string.Create(CultureInfo.InvariantCulture, $"dag:{lat:0.00},{lon:0.00}");
        var now = clock.GetUtcNow();
        if (_dagCache.TryGetValue(key, out var cached) && now - cached.At < CacheDuration)
        {
            return cached.Data;
        }

        // Hier wél met de Nederlandse tijdzone, anders dan bij de uurwaarden: een dag is hier een
        // kalenderdag en niet een moment, en "de hoogste temperatuur van vandaag" moet over onze dag
        // gaan en niet over een UTC-dag die om één uur 's nachts begint.
        var url = string.Create(CultureInfo.InvariantCulture,
            $"https://api.open-meteo.com/v1/forecast?latitude={lat:0.00}&longitude={lon:0.00}&daily=weather_code,temperature_2m_max,temperature_2m_min,sunshine_duration&forecast_days=2&timezone=Europe%2FAmsterdam");
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var doc = JsonDocument.Parse(await client.GetStringAsync(url, ct));
            var data = ParseDaily(doc.RootElement);
            _dagCache[key] = (now, data);
            return data;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Dagverwachting van Open-Meteo ophalen mislukt");
            return cached.Data ?? [];
        }
    }

    /// <summary>
    /// Leest <c>{"daily":{"time":["2026-11-14"],"weather_code":[3],"temperature_2m_max":[9.1],
    /// "temperature_2m_min":[4.0],"sunshine_duration":[7200]}}</c>.
    ///
    /// Een dag zonder temperatuur of zonder code slaan we over. Dat gebeurt aan de rand van de
    /// verwachting, en een halve dag tonen is erger dan hem weglaten.
    /// </summary>
    public static IReadOnlyList<DagWeer> ParseDaily(JsonElement root)
    {
        var result = new List<DagWeer>();
        if (!root.TryGetProperty("daily", out var daily)
            || !daily.TryGetProperty("time", out var times))
        {
            return result;
        }

        for (var i = 0; i < times.GetArrayLength(); i++)
        {
            if (times[i].ValueKind != JsonValueKind.String
                || !DateOnly.TryParse(times[i].GetString(), CultureInfo.InvariantCulture, out var datum))
            {
                continue;
            }

            var code = Getal(daily, "weather_code", i);
            var max = Getal(daily, "temperature_2m_max", i);
            var min = Getal(daily, "temperature_2m_min", i);
            if (code is null || max is null || min is null)
            {
                continue;
            }

            var zonSeconden = Getal(daily, "sunshine_duration", i) ?? 0;
            result.Add(new DagWeer(
                datum,
                Soort((int)code.Value),
                Math.Round(max.Value, 1),
                Math.Round(min.Value, 1),
                Math.Round(zonSeconden / 3600.0, 1)));
        }

        return result;
    }

    /// <summary>Eén getal uit een van de daily-lijsten, of null als het er niet staat.</summary>
    private static double? Getal(JsonElement daily, string naam, int index)
    {
        if (!daily.TryGetProperty(naam, out var lijst)
            || lijst.ValueKind != JsonValueKind.Array
            || index >= lijst.GetArrayLength()
            || lijst[index].ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return lijst[index].GetDouble();
    }

    /// <summary>
    /// De WMO-weercode teruggebracht tot de zeven soorten die de app kan tekenen.
    ///
    /// Code 1 ("overwegend onbewolkt") valt bij zonnig en niet bij half bewolkt: dat is in Nederland
    /// een dag waarop de panelen volop draaien, en wie zo'n dag "half bewolkt" ziet staan vertrouwt
    /// het icoon de volgende keer niet meer.
    /// </summary>
    public static WeerSoort Soort(int code) => code switch
    {
        0 or 1 => WeerSoort.Zonnig,
        2 => WeerSoort.HalfBewolkt,
        3 => WeerSoort.Bewolkt,
        45 or 48 => WeerSoort.Mist,
        >= 71 and <= 77 or 85 or 86 => WeerSoort.Sneeuw,
        >= 95 => WeerSoort.Onweer,
        >= 51 and <= 67 or >= 80 and <= 82 => WeerSoort.Regen,
        _ => WeerSoort.Bewolkt,
    };

    /// <summary>Leest <c>{"hourly":{"time":["2026-11-01T12:00"],"shortwave_radiation":[420.0]}}</c>.</summary>
    public static IReadOnlyList<HourlyIrradiance> ParseHourly(JsonElement root)
    {
        var result = new List<HourlyIrradiance>();
        if (!root.TryGetProperty("hourly", out var hourly)
            || !hourly.TryGetProperty("time", out var times)
            || !hourly.TryGetProperty("shortwave_radiation", out var values))
        {
            return result;
        }

        var count = Math.Min(times.GetArrayLength(), values.GetArrayLength());
        for (var i = 0; i < count; i++)
        {
            if (values[i].ValueKind != JsonValueKind.Number
                || !DateTime.TryParse(times[i].GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var moment))
            {
                continue;
            }

            result.Add(new HourlyIrradiance(new DateTimeOffset(moment, TimeSpan.Zero), values[i].GetDouble()));
        }

        return result;
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
