using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Services;

/// <summary>
/// Haalt elke 6 uur de zonnestraling per dag op voor elk huishouden met zonnepanelen en zet die in de dagtotalen.
/// Locatie: de plaats van het huishouden (één keer opgezocht), anders het midden van Nederland.
/// </summary>
public sealed class WeatherWorker(
    IServiceScopeFactory scopeFactory,
    IWeatherProvider weather,
    TimeProvider clock,
    IConfiguration configuration,
    ILogger<WeatherWorker> logger) : BackgroundService
{
    /// <summary>Midden van Nederland (Utrecht/Amersfoort) als er geen plaats bekend is.</summary>
    public const double DefaultLat = 52.13;
    public const double DefaultLon = 5.29;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Weather:Enabled", true))
        {
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        do
        {
            try
            {
                await RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Weerdata bijwerken mislukt");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task RunAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var today = DutchTime.LocalDate(clock.GetUtcNow());
        var from = today.AddDays(-31);

        var households = await db.Households
            .Where(h => h.Devices.Any(d => d.Type == DeviceType.Inverter))
            .ToListAsync(ct);

        foreach (var household in households)
        {
            double lat = DefaultLat, lon = DefaultLon;
            if (household.Latitude is { } savedLat && household.Longitude is { } savedLon)
            {
                (lat, lon) = (savedLat, savedLon);
            }
            else
            {
                var place = !string.IsNullOrWhiteSpace(household.City) ? household.City : household.PostalCode;
                var found = string.IsNullOrWhiteSpace(place) ? null : await weather.GeocodeAsync(place, ct);
                if (found is { } f)
                {
                    // Alleen een gevonden plaats bewaren; anders volgende ronde opnieuw proberen.
                    (lat, lon) = (f.Lat, f.Lon);
                    household.Latitude = lat;
                    household.Longitude = lon;
                }
            }

            var irradiance = await weather.DailyIrradianceAsync(lat, lon, ct);
            if (irradiance.Count == 0)
            {
                continue;
            }

            var summaries = await db.DailySummaries
                .Where(s => s.HouseholdId == household.Id && s.Date >= from && s.Date <= today)
                .ToListAsync(ct);

            foreach (var summary in summaries)
            {
                if (irradiance.TryGetValue(summary.Date, out var value))
                {
                    summary.IrradianceKwhM2 = value;
                }
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Weerdata bijgewerkt voor {Count} huishoudens", households.Count);
    }
}
