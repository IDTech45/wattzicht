using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Shared;
using Wattzicht.Shared.Taal;

namespace Wattzicht.Api.Services;

/// <summary>
/// Waarschuwt de bewoner als zijn panelen minder leveren dan het weer toeliet.
///
/// De prestatiecheck bestond al, maar stond als regeltje op het besparingsscherm. Daar kijkt niemand
/// naar zolang alles goed gaat, en juist dan moet je het horen: vuile panelen, een tak die is
/// gegroeid, een micro-omvormer die eruit ligt. Dat kost elke dag een paar kilowattuur en het valt
/// pas op bij de jaarafrekening.
///
/// Eén melding per twee weken, nooit vaker. De oorzaak los je niet op een middag op, en een app die
/// elke dag hetzelfde zegt zet je uit.
/// </summary>
public sealed class PrestatieWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    IConfiguration configuration,
    ILogger<PrestatieWorker> logger) : BackgroundService
{
    /// <summary>Eens per zes uur kijken is ruim genoeg; dit verandert over dagen, niet over uren.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    /// <summary>Niet vaker dan dit dezelfde woning lastigvallen.</summary>
    public static readonly TimeSpan Tussenpoos = TimeSpan.FromDays(14);

    /// <summary>Niet 's nachts; dit heeft nooit haast.</summary>
    public const int VanafUur = 10;
    public const int TotUur = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Prestatie:Enabled", true))
        {
            logger.LogInformation("Prestatiemeldingen staan uit (Prestatie:Enabled=false).");
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);
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
                logger.LogError(ex, "Prestatiemeldingen: ronde mislukt");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task RunAsync(CancellationToken ct)
    {
        var nu = clock.GetUtcNow();
        var uur = DutchTime.LocalHour(nu);
        if (uur < VanafUur || uur >= TotUur) return;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var insights = scope.ServiceProvider.GetRequiredService<InsightsService>();
        var push = scope.ServiceProvider.GetRequiredService<IPushSender>();

        if (!push.Enabled) return;

        var vandaag = DutchTime.LocalDate(nu);
        var grens = nu - Tussenpoos;

        // Alleen woningen met panelen, en alleen als de bewoner storingsmeldingen aan heeft staan.
        // Dit is een storing in het klein: je installatie doet het, maar niet goed.
        var woningen = await db.Households
            .Where(h => h.NotifyAlerts
                        && h.Devices.Any(d => d.Type == DeviceType.Inverter)
                        && (h.LastPerformancePushAt == null || h.LastPerformancePushAt < grens))
            .Select(h => new { h.Id, h.Taal })
            .ToListAsync(ct);

        foreach (var woning in woningen)
        {
            ct.ThrowIfCancellationRequested();

            var prestatie = await insights.PerformanceAsync(woning.Id, vandaag, ct);
            if (prestatie.Status != PerformanceStatus.Low) continue;

            var (titel, tekst) = Bericht(prestatie, woning.Taal);
            var verstuurd = await push.SendToHouseholdAsync(woning.Id, titel, tekst, "savings", ct);
            if (verstuurd <= 0) continue;

            await db.Households
                .Where(h => h.Id == woning.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(h => h.LastPerformancePushAt, nu), ct);

            logger.LogInformation("Prestatiemelding verstuurd voor woning {Woning}", woning.Id);
        }
    }

    /// <summary>
    /// De melding zelf. Eerst wat er aan de hand is, dan wat het kost, dan pas wat je eraan doet;
    /// die volgorde is bewust, want zonder bedrag is het een weetje.
    /// </summary>
    internal static (string Titel, string Tekst) Bericht(PerformanceDto prestatie, string? taal)
    {
        var titel = Teksten.T(taal, "prestatie.titel");

        var tekst = string.Format(
            Teksten.T(taal, "prestatie.tekst"),
            prestatie.PercentOfExpected ?? 0,
            prestatie.LostKwh7Days.ToString("0", Teksten.Cultuur(taal)));

        return (titel, tekst);
    }
}
