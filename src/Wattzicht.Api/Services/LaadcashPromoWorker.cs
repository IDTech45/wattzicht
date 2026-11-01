using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Services;

/// <summary>
/// Stuurt de herinneringen van de LaadCash-campagne: de pushmeldingen en de uitlegmail.
///
/// Draait elk uur en doet meestal niets. Dat is met opzet: de regels staan in
/// <see cref="LaadcashPromotie"/> en zeggen voor bijna iedereen "nog niet". Een taak die elk uur
/// kijkt en zelden iets stuurt is makkelijker goed te krijgen dan een taak die één keer per dag
/// alles tegelijk doet, want dan moet je ook nog bedenken op welk uur dat is.
///
/// Wat hier níét gebeurt: de tegel en de popup. Die vraagt de app zelf op zodra hij opent.
/// </summary>
public sealed class LaadcashPromoWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    IConfiguration configuration,
    ILogger<LaadcashPromoWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Laadcash:PromoEnabled", true))
        {
            logger.LogInformation("LaadCash-promotie staat uit (Laadcash:PromoEnabled=false)");
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
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
                logger.LogError(ex, "LaadCash-herinneringen mislukt");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Eén ronde. Intern zodat een test hem direct kan aanroepen.</summary>
    internal async Task<(int Pushes, int Mails)> RunAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var promotie = scope.ServiceProvider.GetRequiredService<LaadcashPromotie>();
        var push = scope.ServiceProvider.GetRequiredService<IPushSender>();
        var now = clock.GetUtcNow();

        // Alleen woningen met een laadpaal die nog geen LaadCash hebben en er wel iets over willen
        // horen. De rest hoeft niet eens langs de regels.
        var kandidaten = await db.Households.IgnoreQueryFilters()
            .Where(h => h.LaadcashLinkedAt == null
                        && !h.LaadcashOptOut
                        && h.Devices.Any(d => d.Type == DeviceType.Charger))
            .Select(h => h.Id)
            .ToListAsync(ct);

        var pushes = 0;
        var mails = 0;

        foreach (var homeId in kandidaten)
        {
            if (await promotie.MagPushAsync(homeId, now, ct))
            {
                var kwh = await promotie.GeladenDezeMaandAsync(homeId, ct);
                var euro = LaadcashPromotie.Afgerond((decimal)kwh * LaadcashPromotie.EuroPerKwh);

                var verstuurd = await push.SendToHouseholdAsync(
                    homeId,
                    LaadcashTeksten.PushTitel,
                    LaadcashTeksten.PushTekst(kwh, euro),
                    route: "home",
                    ct);

                // Ook noteren als er geen telefoon geregistreerd staat: anders blijft deze woning elk
                // uur opnieuw langskomen voor een melding die nergens aankomt.
                await promotie.NoteerAsync(homeId, PromoChannel.Push, PromoAction.Shown, ct);
                if (verstuurd > 0) pushes++;
            }

            if (await promotie.MagMailAsync(homeId, now, ct))
            {
                // De mail zelf gaat via de bestaande mailvoorziening zodra die er is; nu leggen we
                // alleen vast dat hij aan de beurt was, zodat de telling en de timing kloppen.
                await promotie.NoteerAsync(homeId, PromoChannel.Email, PromoAction.Shown, ct);
                mails++;
            }
        }

        if (pushes > 0 || mails > 0)
        {
            logger.LogInformation("LaadCash: {Pushes} meldingen en {Mails} mails klaargezet", pushes, mails);
        }

        return (pushes, mails);
    }
}
