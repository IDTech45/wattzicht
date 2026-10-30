using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace Wattzicht.Api.Services;

/// <summary>Kale marktprijs (day-ahead) voor één uur.</summary>
/// <param name="StartUtc">Begin van het uur in UTC.</param>
/// <param name="MarketEurPerKwh">Prijs in euro per kWh, inclusief btw, exclusief energiebelasting en opslag.</param>
public sealed record HourPrice(DateTimeOffset StartUtc, decimal MarketEurPerKwh);

/// <summary>Levert uurprijzen voor de Nederlandse markt.</summary>
public interface IPriceProvider
{
    /// <summary>Naam van de bron, voor in de app.</summary>
    string Source { get; }

    /// <summary>Uurprijzen voor vandaag en (zodra gepubliceerd, rond 13:00) morgen. Lege lijst als de bron niet bereikbaar is.</summary>
    Task<IReadOnlyList<HourPrice>> GetPricesAsync(DateOnly today, CancellationToken ct);
}

/// <summary>
/// Day-ahead prijzen via de openbare EnergyZero-API (geen sleutel nodig).
/// Kwartierprijzen worden gemiddeld tot uurprijzen. Resultaat wordt 30 minuten per dag bewaard.
/// </summary>
public sealed class EnergyZeroPriceProvider(
    IHttpClientFactory httpClientFactory,
    TimeProvider clock,
    ILogger<EnergyZeroPriceProvider> logger) : IPriceProvider
{
    public const string HttpClientName = "energyzero";
    public const string DefaultBaseUrl = "https://api.energyzero.nl/v1/";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<DateOnly, (DateTimeOffset FetchedAt, IReadOnlyList<HourPrice> Prices)> _cache = new();

    public string Source => "EnergyZero (day-ahead)";

    public async Task<IReadOnlyList<HourPrice>> GetPricesAsync(DateOnly today, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (_cache.TryGetValue(today, out var cached) && now - cached.FetchedAt < CacheDuration)
        {
            return cached.Prices;
        }

        var from = DutchTime.StartOfLocalDayUtc(today);
        var till = DutchTime.StartOfLocalDayUtc(today.AddDays(2)).AddMilliseconds(-1);
        var url = "energyprices"
            + "?fromDate=" + Uri.EscapeDataString(from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture))
            + "&tillDate=" + Uri.EscapeDataString(till.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture))
            + "&interval=4&usageType=1&inclBtw=true";

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("EnergyZero gaf {Status} voor {Date}", (int)response.StatusCode, today);
                return cached.Prices ?? [];
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var prices = Parse(doc.RootElement);
            _cache[today] = (now, prices);
            return prices;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Uurprijzen ophalen bij EnergyZero mislukt");
            return cached.Prices ?? [];
        }
    }

    /// <summary>Leest <c>{"Prices":[{"price":0.21,"readingDate":"…Z"}]}</c> en middelt per uur.</summary>
    public static IReadOnlyList<HourPrice> Parse(JsonElement root)
    {
        if (!TryGetPropertyIgnoreCase(root, "Prices", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var perHour = new SortedDictionary<DateTimeOffset, List<decimal>>();
        foreach (var item in items.EnumerateArray())
        {
            if (!TryGetPropertyIgnoreCase(item, "price", out var priceEl)
                || !TryGetPropertyIgnoreCase(item, "readingDate", out var dateEl))
            {
                continue;
            }

            decimal price;
            if (priceEl.ValueKind == JsonValueKind.Number)
            {
                price = priceEl.GetDecimal();
            }
            else if (priceEl.ValueKind != JsonValueKind.String
                || !decimal.TryParse(priceEl.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out price))
            {
                continue;
            }

            if (dateEl.ValueKind != JsonValueKind.String
                || !DateTimeOffset.TryParse(dateEl.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var ts))
            {
                continue;
            }

            var hour = new DateTimeOffset(ts.Year, ts.Month, ts.Day, ts.Hour, 0, 0, TimeSpan.Zero);
            if (!perHour.TryGetValue(hour, out var list))
            {
                perHour[hour] = list = [];
            }

            list.Add(price);
        }

        return perHour
            .Select(kv => new HourPrice(kv.Key, Math.Round(kv.Value.Average(), 5)))
            .ToList();
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
