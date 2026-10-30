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

        return app;
    }

    private static async Task<IResult> AdviceAsync(
        HttpContext http,
        AppDbContext db,
        SummaryService summaries,
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
        return Results.Ok(advice with { PriceSource = hourPrices.Count > 0 ? prices.Source : null });
    }
}
