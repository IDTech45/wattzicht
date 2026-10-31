using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Services;

/// <summary>
/// Stuurt een melding vlak voordat het voordeligste laadmoment begint.
///
/// De adviezen bestonden al, maar alleen voor wie de app toevallig opende. Een tip dat je tussen
/// twee en drie uur het goedkoopst laadt is niets waard als je hem om vijf uur leest. Dit is het
/// stuk dat het advies naar de bewoner brengt in plaats van andersom.
///
/// Draait elk kwartier en kijkt per huishouden of het beste uur binnen drie kwartier begint. Zo ja,
/// dan gaat er één melding uit, hoogstens één per dag en nooit 's nachts (zie PushPolicy). De
/// melding is een zetje, geen opdracht: wij sturen niets aan, de bewoner doet de stekker erin.
/// </summary>
public sealed class SmartMomentWorker(
    IServiceScopeFactory scopeFactory,
    IPriceProvider prices,
    IWeatherProvider weather,
    TimeProvider clock,
    IConfiguration configuration,
    ILogger<SmartMomentWorker> logger) : BackgroundService
{
    /// <summary>Hoe vaak we kijken. Een kwartier is fijn genoeg voor een melding drie kwartier vooruit.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    /// <summary>Onder deze besparing sturen we niets: voor twee cent hoef je niemand te storen.</summary>
    public const decimal MinimumSavingEur = 0.15m;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("SmartMoment:Enabled", true))
        {
            logger.LogInformation("Laadmoment-meldingen staan uit (SmartMoment:Enabled=false).");
            return;
        }

        try
        {
            // Even wachten tot de rest van de API staat; de eerste ronde heeft geen haast.
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
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
                logger.LogError(ex, "Ronde laadmoment-meldingen mislukt");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = DutchTime.LocalDate(now);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var summaries = scope.ServiceProvider.GetRequiredService<SummaryService>();
        // IPushSender is scoped; daarom hier uit de scope en niet in de constructor van deze worker.
        var push = scope.ServiceProvider.GetRequiredService<IPushSender>();

        // Alleen huishoudens met een laadpaal: zonder auto aan de stekker is dit advies zinloos.
        var kandidaten = await db.Households
            .Where(h => h.NotifySmartMoment && h.Devices.Any(d => d.Type == DeviceType.Charger))
            .Select(h => new { Household = h, h.Latitude, h.Longitude })
            .ToListAsync(ct);

        if (kandidaten.Count == 0)
        {
            return;
        }

        var uurprijzen = await prices.GetPricesAsync(today, ct);

        foreach (var kandidaat in kandidaten)
        {
            ct.ThrowIfCancellationRequested();
            var huishouden = kandidaat.Household;
            if (!PushPolicy.MaySendSmartMoment(huishouden, now))
            {
                continue;
            }

            var tariff = await db.Tariffs
                .Where(t => t.HouseholdId == huishouden.Id && t.ValidFrom <= today)
                .OrderByDescending(t => t.ValidFrom)
                .FirstOrDefaultAsync(ct);
            var rates = await summaries.GetRatesAsync(huishouden.Id, today, ct);

            var verwachting = kandidaat is { Latitude: { } lat, Longitude: { } lon }
                ? await weather.HourlyForecastAsync(lat, lon, ct)
                : [];

            // Hoeveel er nog in moet weten we niet; we rekenen met wat deze klant gemiddeld laadt.
            var gemiddeld = await db.DailySummaries
                .Where(s => s.HouseholdId == huishouden.Id && s.Date >= today.AddDays(-14) && s.Date < today)
                .Select(s => (double?)s.ChargedKwh)
                .AverageAsync(ct) ?? AdviceEngine.DefaultChargeKwh;

            var plan = ChargePlanner.Build(new ChargePlanInput(
                now,
                DutchTime.StartOfLocalDayUtc(today.AddDays(1)).AddHours(7),
                Math.Max(AdviceEngine.DefaultChargeKwh / 2, gemiddeld),
                configuration.GetValue("Advice:ChargerKw", 11.0),
                uurprijzen,
                verwachting,
                configuration.GetValue("Advice:PvPeakKw", 4.0),
                tariff?.IsDynamic ?? false,
                rates,
                configuration.GetValue("Advice:SurchargePerKwh", AdviceEngine.DefaultSurchargePerKwh)));

            if (!IsHetBijnaZover(plan, now))
            {
                continue;
            }

            var start = plan.Slots.Min(s => s.StartUtc);
            var (titel, tekst) = PushPolicy.SmartMomentMessage(start, plan.SavingEur, plan.SolarKwh);
            var verstuurd = await push.SendToHouseholdAsync(huishouden.Id, titel, tekst, "/advies", ct);
            if (verstuurd <= 0)
            {
                continue;
            }

            huishouden.LastSmartMomentPushAt = now;
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Laadmoment-melding naar huishouden {Id}: start {Start}, besparing {Saving}",
                huishouden.Id, start, plan.SavingEur);
        }
    }

    /// <summary>
    /// Begint het beste uur binnen de aankondigingstijd, en is het de moeite waard?
    ///
    /// Apart gehouden zodat deze afweging zonder database en zonder netwerk te testen is; het is de
    /// regel die bepaalt of iemands telefoon trilt.
    /// </summary>
    public static bool IsHetBijnaZover(ChargePlanner.Plan plan, DateTimeOffset now)
    {
        if (plan.Slots.Count == 0)
        {
            return false;
        }

        // Alleen melden als er iets te winnen valt: of een paar dubbeltjes, of eigen zon in de auto.
        if (plan.SavingEur < MinimumSavingEur && plan.SolarKwh < 0.5)
        {
            return false;
        }

        var start = plan.Slots.Min(s => s.StartUtc);
        var tot = start - now;
        return tot > TimeSpan.Zero && tot <= PushPolicy.SmartMomentLeadTime;
    }
}
