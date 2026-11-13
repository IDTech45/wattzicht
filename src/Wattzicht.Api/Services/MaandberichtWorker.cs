using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Shared;
using Wattzicht.Shared.Taal;

namespace Wattzicht.Api.Services;

/// <summary>
/// Eén bericht per maand met wat die maand heeft gedaan.
///
/// Waarom dit bestaat: een app die alleen iets zegt als er iets mis is, wordt een app die je alleen
/// opent als er iets mis is. Dit is het tegenovergestelde: één keer per maand een regel met wat je
/// opwekte, wat je zelf gebruikte en wat het scheelde. Dat is het moment waarop iemand weer kijkt,
/// en het is ook het enige vaste moment waarop hij ziet dat het werkt.
///
/// Op de tweede van de maand, want de eerste is de dag dat de vorige maand nog aan het afronden is
/// (een meting van 23:55 komt soms pas daarna binnen). Eén keer per woning per maand, en nooit
/// 's nachts.
/// </summary>
public sealed class MaandberichtWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    IConfiguration configuration,
    ILogger<MaandberichtWorker> logger) : BackgroundService
{
    /// <summary>Elk uur kijken of het moment daar is; het bericht gaat maar één keer uit.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>De dag van de maand waarop het bericht uitgaat.</summary>
    public const int OpDag = 2;

    public const int VanafUur = 10;
    public const int TotUur = 20;

    /// <summary>Onder deze opwek valt er niets te vieren; dan slaan we de maand over.</summary>
    public const double MinimumKwh = 1.0;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Maandbericht:Enabled", true))
        {
            logger.LogInformation("Maandberichten staan uit (Maandbericht:Enabled=false).");
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
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
                logger.LogError(ex, "Maandbericht: ronde mislukt");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task RunAsync(CancellationToken ct)
    {
        var nu = clock.GetUtcNow();
        var vandaag = DutchTime.LocalDate(nu);
        var uur = DutchTime.LocalHour(nu);

        if (vandaag.Day != OpDag || uur < VanafUur || uur >= TotUur) return;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var push = scope.ServiceProvider.GetRequiredService<IPushSender>();
        if (!push.Enabled) return;

        var maand = new DateOnly(vandaag.Year, vandaag.Month, 1).AddMonths(-1);
        var eindeMaand = maand.AddMonths(1);

        // Woningen die deze maand nog geen bericht kregen. De vergelijking op maand en niet op een
        // tussenpoos: anders krijgt iemand die op de eerste is gekoppeld er twee kort achter elkaar.
        var woningen = await db.Households
            .Where(h => h.NotifyMonthly
                        && (h.LastMonthlyPushAt == null || h.LastMonthlyPushAt < maand.ToDateTime(TimeOnly.MinValue)))
            .Select(h => new { h.Id, h.Taal })
            .ToListAsync(ct);

        foreach (var woning in woningen)
        {
            ct.ThrowIfCancellationRequested();

            var rijen = await db.DailySummaries
                .Where(s => s.HouseholdId == woning.Id && s.Date >= maand && s.Date < eindeMaand)
                .Select(s => new { s.PvKwh, s.SelfUseKwh, s.ExportKwh, s.ImportKwh, s.SavedEur })
                .ToListAsync(ct);

            if (rijen.Count == 0) continue;

            var opwek = rijen.Sum(r => r.PvKwh);
            if (opwek < MinimumKwh) continue;

            var leverancier = await db.SupplierUsages
                .Where(x => x.HouseholdId == woning.Id && x.Month == maand)
                .Select(x => (double?)x.ImportKwh)
                .FirstOrDefaultAsync(ct);

            var (titel, tekst) = Bericht(
                maand,
                opwek,
                rijen.Sum(r => r.SelfUseKwh),
                rijen.Sum(r => r.SavedEur),
                leverancier,
                woning.Taal);

            var verstuurd = await push.SendToHouseholdAsync(woning.Id, titel, tekst, "savings", ct);
            if (verstuurd <= 0) continue;

            await db.Households
                .Where(h => h.Id == woning.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(h => h.LastMonthlyPushAt, nu), ct);

            logger.LogInformation("Maandbericht verstuurd voor woning {Woning} over {Maand}", woning.Id, maand);
        }
    }

    /// <summary>
    /// Het bericht zelf.
    ///
    /// Eerst wat je maakte, dan wat je ervan zelf gebruikte, dan het bedrag. En als we weten wat je
    /// leverancier over diezelfde maand zegt, komt dat erachteraan: dat is het enige getal in de app
    /// dat van buiten komt en dus het enige dat je kunt narekenen.
    /// </summary>
    internal static (string Titel, string Tekst) Bericht(
        DateOnly maand, double opwekKwh, double zelfKwh, decimal bespaardEur, double? leverancierAfnameKwh, string? taal)
    {
        var cultuur = Teksten.Cultuur(taal);
        var naam = new DateTime(maand.Year, maand.Month, 1).ToString("MMMM", cultuur);

        var titel = string.Format(Teksten.T(taal, "maand.titel"), naam);

        var tekst = string.Format(
            Teksten.T(taal, "maand.tekst"),
            opwekKwh.ToString("0", cultuur),
            zelfKwh.ToString("0", cultuur),
            bespaardEur.ToString("C0", cultuur));

        if (leverancierAfnameKwh is { } afname)
        {
            tekst += " " + string.Format(
                Teksten.T(taal, "maand.leverancier"),
                afname.ToString("0", cultuur));
        }

        return (titel, tekst);
    }
}
