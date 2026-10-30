using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Endpoints;

/// <summary>Slim sturen: advies wanneer je het beste laadt en verbruikt.</summary>
public static class AdviceEndpoints
{
    public static IEndpointRouteBuilder MapAdviceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/advice", AdviceAsync)
            .RequireAuthorization(AuthPolicies.Resident)
            .WithTags("Slim sturen")
            .WithSummary("Tips voor vandaag op basis van uurprijzen, zon en de eigen apparaten, met de besparing in euro.")
            .Produces<AdviceDto>();

        app.MapGet("/feed-in", FeedInAsync)
            .RequireAuthorization(AuthPolicies.Resident)
            .WithTags("Slim sturen")
            .WithSummary("Wat terugleveren kost (terugleverkosten) en wat zelf gebruiken scheelt, nu en vanaf 2027.")
            .Produces<FeedInDto>();

        app.MapGet("/sustainability", async (HttpContext http, InsightsService insights, TimeProvider clock, CancellationToken ct) =>
                Results.Ok(await insights.SustainabilityAsync(http.User.GetHouseholdId()!.Value, DutchTime.LocalDate(clock.GetUtcNow()), ct)))
            .RequireAuthorization(AuthPolicies.Resident)
            .WithTags("Slim sturen")
            .WithSummary("Dit jaar: opwek, vermeden CO₂, zelfvoorziening en de prestatiecheck van de panelen.")
            .Produces<SustainabilityDto>();

        return app;
    }

    private static async Task<IResult> AdviceAsync(
        HttpContext http,
        AppDbContext db,
        SummaryService summaries,
        HouseholdQueries queries,
        IPriceProvider prices,
        IConfiguration configuration,
        TimeProvider clock,
        CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var now = clock.GetUtcNow();
        var today = DutchTime.LocalDate(now);

        var types = await db.Devices
            .Where(d => d.HouseholdId == householdId)
            .Select(d => d.Type)
            .Distinct()
            .ToListAsync(ct);

        var tariff = await db.Tariffs
            .Where(t => t.HouseholdId == householdId && t.ValidFrom <= today)
            .OrderByDescending(t => t.ValidFrom)
            .FirstOrDefaultAsync(ct);
        var rates = await summaries.GetRatesAsync(householdId, today, ct);

        var since = today.AddDays(-14);
        var history = await db.DailySummaries
            .Where(s => s.HouseholdId == householdId && s.Date >= since && s.Date < today)
            .Select(s => new { s.ChargedKwh, s.ExportKwh, s.BatteryDischargedKwh })
            .ToListAsync(ct);

        var hourPrices = await prices.GetPricesAsync(today, ct);
        var surcharge = configuration.GetValue("Advice:SurchargePerKwh", AdviceEngine.DefaultSurchargePerKwh);

        var input = new AdviceInput(
            now,
            hourPrices,
            HasPv: types.Contains(DeviceType.Inverter),
            HasBattery: types.Contains(DeviceType.Battery),
            HasCharger: types.Contains(DeviceType.Charger),
            IsDynamic: tariff?.IsDynamic ?? false,
            rates,
            AvgDailyChargedKwh: history.Count > 0 ? history.Average(h => h.ChargedKwh) : 0,
            AvgDailyExportKwh: history.Count > 0 ? history.Average(h => h.ExportKwh) : 0,
            TypicalBatteryKwh: history.Count > 0 ? history.Max(h => h.BatteryDischargedKwh) : 0,
            SurchargePerKwh: surcharge);

        var advice = AdviceEngine.Build(input);

        // Levert het huis nu flink terug? Dan bovenaan een tip voor nu.
        if (input.HasPv)
        {
            var live = await queries.LiveAsync(householdId, ct);
            var liveTip = FeedInCalculator.LiveSurplusTip(live, now, input.HasCharger, rates);
            if (liveTip is not null)
            {
                advice = advice with { Tips = [liveTip, .. advice.Tips] };
            }
        }

        return Results.Ok(advice with { PriceSource = hourPrices.Count > 0 ? prices.Source : null });
    }

    private static async Task<IResult> FeedInAsync(
        HttpContext http,
        AppDbContext db,
        SummaryService summaries,
        HouseholdQueries queries,
        TimeProvider clock,
        CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var now = clock.GetUtcNow();
        var today = DutchTime.LocalDate(now);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var yearAgo = today.AddDays(-364);

        var rates = await summaries.GetRatesAsync(householdId, today, ct);

        var days = await db.DailySummaries
            .Where(s => s.HouseholdId == householdId && s.Date >= yearAgo && s.Date <= today)
            .Select(s => new { s.Date, s.ExportKwh })
            .ToListAsync(ct);

        var live = await queries.LiveAsync(householdId, ct);

        return Results.Ok(FeedInCalculator.Build(
            today,
            rates,
            monthExportKwh: days.Where(d => d.Date >= monthStart).Sum(d => d.ExportKwh),
            // Jaar: alleen volle dagen, vandaag is nog niet af.
            exportKwhLastYear: days.Where(d => d.Date < today).Sum(d => d.ExportKwh),
            daysWithData: days.Count(d => d.Date < today),
            liveExportW: FeedInCalculator.LiveExportW(live, now)));
    }
}
