using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;

namespace Wattzicht.Api.Services;

/// <summary>
/// Houdt de abonnementen bij: zet verlopen periodes op "expired" en waarschuwt zeven dagen voordat
/// een proefperiode afloopt.
///
/// Die waarschuwing is het enige wat de klant ervan merkt, en hij hoort maar één keer te komen.
/// Daarom staat op de rij wanneer hij verstuurd is; zonder dat zou deze controle hem zeven dagen
/// achter elkaar sturen, en dat is precies het soort melding waar mensen de app voor uitzetten.
/// </summary>
public sealed class SubscriptionWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    IConfiguration configuration,
    ILogger<SubscriptionWorker> logger) : BackgroundService
{
    /// <summary>Hoeveel dagen vóór het einde van de proefperiode we waarschuwen.</summary>
    public static readonly TimeSpan WaarschuwVoor = TimeSpan.FromDays(7);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Subscriptions:WorkerEnabled", true))
        {
            logger.LogInformation("Abonnementencontrole staat uit (Subscriptions:WorkerEnabled=false)");
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
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
                logger.LogError(ex, "Abonnementencontrole mislukt");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Eén ronde. Intern zodat een test hem direct kan aanroepen.</summary>
    internal async Task<(int Verlopen, int Gewaarschuwd)> RunAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var push = scope.ServiceProvider.GetRequiredService<IPushSender>();
        var now = clock.GetUtcNow();

        // 1. Afgelopen periodes afsluiten.
        var verlopen = await db.Subscriptions.IgnoreQueryFilters()
            .Where(s => s.Status == SubscriptionStatus.Active && s.EndsAt != null && s.EndsAt <= now)
            .ToListAsync(ct);

        foreach (var rij in verlopen)
        {
            rij.Status = SubscriptionStatus.Expired;
        }

        // 2. Rijen die klaarstonden en nu aan de beurt zijn, op actief zetten. Het antwoord aan de app
        // klopt ook zonder deze stap (zie EntitlementService), maar zo blijft de administratie leesbaar.
        var klaarstaand = await db.Subscriptions.IgnoreQueryFilters()
            .Where(s => s.Status == SubscriptionStatus.Pending && s.StartsAt <= now
                        && (s.EndsAt == null || s.EndsAt > now))
            .ToListAsync(ct);

        foreach (var rij in klaarstaand)
        {
            var nogIetsActief = await db.Subscriptions.IgnoreQueryFilters()
                .AnyAsync(s => s.HouseholdId == rij.HouseholdId
                               && s.Status == SubscriptionStatus.Active
                               && s.Id != rij.Id, ct);
            if (!nogIetsActief)
            {
                rij.Status = SubscriptionStatus.Active;
            }
        }

        // 3. Proefperiodes die binnen een week aflopen en nog geen waarschuwing kregen.
        var grens = now + WaarschuwVoor;
        var bijnaOm = await db.Subscriptions.IgnoreQueryFilters()
            .Where(s => s.Status == SubscriptionStatus.Active
                        && s.Source == SubscriptionSource.Trial
                        && s.TrialReminderSentAt == null
                        && s.EndsAt != null
                        && s.EndsAt > now
                        && s.EndsAt <= grens)
            .ToListAsync(ct);

        var gewaarschuwd = 0;
        foreach (var rij in bijnaOm)
        {
            var dagen = Math.Max(1, (int)Math.Ceiling((rij.EndsAt!.Value - now).TotalDays));
            var verstuurd = await push.SendToHouseholdAsync(
                rij.HouseholdId,
                "Je proefperiode loopt af",
                $"Nog {dagen} dagen Plus. Daarna blijft Wattzicht gewoon werken, met dertig dagen historie.",
                route: "settings",
                ct);

            // Ook noteren als er geen telefoon geregistreerd is: anders probeert hij het morgen weer,
            // en overmorgen, voor een woning waar helemaal geen app op staat.
            rij.TrialReminderSentAt = now;
            if (verstuurd > 0) gewaarschuwd++;
        }

        if (verlopen.Count > 0 || klaarstaand.Count > 0 || bijnaOm.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Abonnementen: {Verlopen} afgesloten, {Gestart} gestart, {Gewaarschuwd} proefperiodes gewaarschuwd",
                verlopen.Count, klaarstaand.Count, gewaarschuwd);
        }

        return (verlopen.Count, gewaarschuwd);
    }
}
