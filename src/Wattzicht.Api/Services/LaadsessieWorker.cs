using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Services;

/// <summary>
/// Stuurt na afloop van een laadbeurt één bericht: wat erin ging en hoeveel daarvan van eigen dak kwam.
///
/// Dit is de andere helft van het advies dat er al stond. SmartMomentWorker zegt vooraf wanneer je
/// het voordeligst laadt, en daarna hoorden we er nooit meer iets van. Het laadvermogen verdween in
/// een grafiek die niemand opent, want niemand kijkt elke vijf minuten naar zijn laadpaal. Zo wordt
/// die vijf minuten geen product maar grondstof: de cijfers zijn alleen nodig om achteraf één zin te
/// kunnen zeggen die ergens over gaat.
///
/// Wij herkennen de beurt uit de metingen en niet uit een gebeurtenis van de paal, want de meeste
/// merken sturen ons niets op het moment dat de stekker eruit gaat. Daarom onthouden we per
/// huishouden het einde van de laatst gemelde beurt; anders komt dezelfde beurt elke ronde opnieuw
/// langs.
/// </summary>
public sealed class LaadsessieWorker(
    IServiceScopeFactory scopeFactory,
    IWeatherProvider weather,
    TimeProvider clock,
    IConfiguration configuration,
    ILogger<LaadsessieWorker> logger) : BackgroundService
{
    /// <summary>Elk kwartier is genoeg: een beurt is pas af als de paal een kwartier stilstaat.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    /// <summary>Hoe ver we terugkijken. Ruim genoeg voor een nacht laden, kort genoeg om niet te zwoegen.</summary>
    public static readonly TimeSpan Venster = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("ChargeSession:Enabled", true))
        {
            logger.LogInformation("Berichten na een laadbeurt staan uit (ChargeSession:Enabled=false).");
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ronde berichten na een laadbeurt mislukt");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var vanaf = now - Venster;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var push = scope.ServiceProvider.GetRequiredService<IPushSender>();

        var kandidaten = await db.Households
            .Where(h => h.NotifyChargeSession && h.Devices.Any(d => d.Type == DeviceType.Charger))
            .Select(h => new { Household = h, h.Latitude, h.Longitude })
            .ToListAsync(ct);

        foreach (var kandidaat in kandidaten)
        {
            ct.ThrowIfCancellationRequested();
            var huishouden = kandidaat.Household;
            if (!PushPolicy.MaySendChargeSession(huishouden, now)) continue;

            var metingen = await MeetpuntenAsync(db, huishouden.Id, vanaf, ct);
            if (metingen.Count < 2) continue;

            var sessies = LaadsessieDetector.Vind(metingen);
            // Alleen de laatste. Komt iemand na een week terug, dan krijgt hij geen vijf berichten
            // achter elkaar over beurten die hij allang vergeten is.
            var laatste = sessies.LastOrDefault(s => huishouden.LastChargeSessionEndUtc is not { } gemeld
                                                     || s.Eind > gemeld);
            if (laatste is null) continue;

            var zonnigUur = await ZonnigsteUurMorgenAsync(kandidaat.Latitude, kandidaat.Longitude, now, ct);
            var (titel, tekst) = PushPolicy.ChargeSessionMessage(laatste, zonnigUur, huishouden.Taal);
            var verstuurd = await push.SendToHouseholdAsync(huishouden.Id, titel, tekst, "/", ct);

            // Ook bij een mislukte verzending vastleggen dat deze beurt geweest is. Anders blijft hij
            // het elk kwartier opnieuw proberen met een bericht dat steeds ouder wordt.
            huishouden.LastChargeSessionEndUtc = laatste.Eind;
            await db.SaveChangesAsync(ct);

            if (verstuurd > 0)
            {
                logger.LogInformation("Laadbeurt gemeld aan huishouden {Id}: {Kwh} kWh, {Zon} kWh eigen opwek",
                    huishouden.Id, laatste.Kwh, laatste.ZonKwh);
            }
        }
    }

    /// <summary>
    /// De metingen van dit huishouden, per moment samengevoegd.
    ///
    /// Het laadvermogen komt van de laadpaal en de netkant van de P1-meter of de omvormer, dus dat
    /// zijn twee apparaten met hun eigen meetmomenten. We leggen ze op de minuut naast elkaar; dat is
    /// nauwkeurig genoeg, want beide leveren om de paar minuten en het gaat om een optelsom over een
    /// hele laadbeurt.
    /// </summary>
    private static async Task<List<Meetpunt>> MeetpuntenAsync(AppDbContext db, Guid huishoudenId,
        DateTimeOffset vanaf, CancellationToken ct)
    {
        var rijen = await db.Readings
            .Where(r => r.Device.HouseholdId == huishoudenId && r.Timestamp >= vanaf)
            .Select(r => new { r.Timestamp, r.ChargerPowerW, r.GridPowerW, r.Device.Type })
            .ToListAsync(ct);

        return [.. rijen
            .GroupBy(r => new DateTimeOffset(r.Timestamp.UtcDateTime.AddSeconds(-r.Timestamp.Second), TimeSpan.Zero))
            .OrderBy(g => g.Key)
            .Select(g => new Meetpunt(
                g.Key,
                g.Sum(r => r.ChargerPowerW),
                g.Sum(r => r.GridPowerW),
                // Zonder een apparaat dat de netkant meet weten we de herkomst niet. Een laadpaal
                // meldt netjes nul op dat veld, en dat is geen meting maar het ontbreken ervan.
                g.Any(r => r.Type is DeviceType.P1Meter or DeviceType.Inverter)))];
    }

    /// <summary>Het uur waarop morgen de meeste zon staat, lokaal. Null als we de plaats niet weten.</summary>
    private async Task<int?> ZonnigsteUurMorgenAsync(double? lat, double? lon, DateTimeOffset now, CancellationToken ct)
    {
        if (lat is not { } breedte || lon is not { } lengte) return null;

        var verwachting = await weather.HourlyForecastAsync(breedte, lengte, ct);
        var morgen = DutchTime.LocalDate(now).AddDays(1);
        var uren = verwachting.Where(u => DutchTime.LocalDate(u.StartUtc) == morgen).ToList();
        if (uren.Count == 0) return null;

        var beste = uren.MaxBy(u => u.WattPerM2);
        // Een dag zonder noemenswaardige zon is geen tip waard: dan sturen we iemand met zijn auto
        // naar een uur waarin zijn panelen alsnog niets doen.
        return beste is { WattPerM2: >= 150 } ? DutchTime.LocalHour(beste.StartUtc) : null;
    }
}
