using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Services;

/// <summary>
/// De avond ervoor melden dat morgen een zonnig blok heeft.
///
/// Waarom dit naast <see cref="SmartMomentWorker"/> staat en er niet in: die kijkt drie kwartier
/// vooruit en zegt "nu de stekker erin". Dat werkt voor wie thuis is en de telefoon in zijn hand
/// heeft. Wie om acht uur 's avonds zijn dag doorneemt heeft iets anders nodig: morgenmiddag is het
/// zonnig, dus laat de auto morgen aan de lader staan in plaats van vanavond. Dat is een andere
/// beslissing op een ander moment, en die kun je niet in dezelfde ronde nemen.
///
/// Wat ze wél delen is de rem. Twee berichten over laden op één avond is drammen, en ze zouden
/// elkaar ook tegenspreken; <see cref="PushPolicy.MaySendZonMorgen"/> houdt daar rekening mee.
///
/// Alleen naar woningen met een laadpaal. Zonder laadpaal weten we niet of er een auto is, en een
/// melding over de lader bij iemand zonder lader is precies de melding waarna mensen alles uitzetten.
/// </summary>
public sealed class ZonMorgenWorker(
    IServiceScopeFactory scopeFactory,
    IWeatherProvider weather,
    TimeProvider clock,
    IConfiguration configuration,
    ILogger<ZonMorgenWorker> logger) : BackgroundService
{
    /// <summary>Elk half uur kijken; de ronde zelf gaat alleen door in het uur van acht.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("ZonMorgen:Enabled", true))
        {
            logger.LogInformation("Meldingen over de zon van morgen staan uit (ZonMorgen:Enabled=false).");
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
                logger.LogError(ex, "Melding over de zon van morgen: ronde mislukt");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task RunAsync(CancellationToken ct)
    {
        var nu = clock.GetUtcNow();
        if (DutchTime.LocalHour(nu) != PushPolicy.ZonMorgenUur)
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var insights = scope.ServiceProvider.GetRequiredService<InsightsService>();
        var push = scope.ServiceProvider.GetRequiredService<IPushSender>();
        if (!push.Enabled)
        {
            return;
        }

        var vandaag = DutchTime.LocalDate(nu);
        var morgen = vandaag.AddDays(1);
        var standaardPiek = configuration.GetValue("Advice:PvPeakKw", 4.0);

        // Een laadpaal én panelen: zonder panelen is het zonnige blok van morgen niet jouw zon maar
        // dat van de buurman, en dan valt er niets te verschuiven.
        var woningen = await db.Households
            .Where(h => h.NotifyZonMorgen
                        && h.Latitude != null
                        && h.Longitude != null
                        && h.Devices.Any(d => d.Type == DeviceType.Charger)
                        && h.Devices.Any(d => d.Type == DeviceType.Inverter))
            .ToListAsync(ct);

        foreach (var woning in woningen)
        {
            ct.ThrowIfCancellationRequested();
            if (!PushPolicy.MaySendZonMorgen(woning, nu))
            {
                continue;
            }

            var verwachting = await weather.HourlyForecastAsync(woning.Latitude!.Value, woning.Longitude!.Value, ct);
            var piek = woning.PvPeakKw
                       ?? await insights.GeschatPiekvermogenAsync(woning.Id, vandaag, ct)
                       ?? standaardPiek;

            var blok = ZonnigBlok.VoorDag(verwachting, morgen, piek);
            if (blok is null)
            {
                continue;
            }

            var (titel, tekst) = PushPolicy.ZonMorgenMessage(blok, woning.Taal);
            var verstuurd = await push.SendToHouseholdAsync(woning.Id, titel, tekst, "/advies", ct);
            if (verstuurd <= 0)
            {
                continue;
            }

            woning.LastZonMorgenPushAt = nu;
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Melding over de zon van morgen naar woning {Woning}: {Van}–{Tot} uur, {Kwh} kWh",
                woning.Id, blok.VanUur, blok.TotUur, blok.VerwachtKwh);
        }
    }
}
