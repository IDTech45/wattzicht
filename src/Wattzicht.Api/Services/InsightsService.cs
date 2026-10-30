using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Services;

/// <summary>Laadt de gegevens voor prestatiecheck, duurzaamheid en kansen van één huishouden.</summary>
public sealed class InsightsService(AppDbContext db, SummaryService summaries, IConfiguration configuration)
{
    public async Task<PerformanceDto> PerformanceAsync(Guid householdId, DateOnly today, CancellationToken ct)
    {
        var from = today.AddDays(-31);
        var days = await db.DailySummaries
            .Where(s => s.HouseholdId == householdId && s.Date >= from && s.Date <= today)
            .Select(s => new PerformanceDay(s.Date, s.PvKwh, s.IrradianceKwhM2))
            .ToListAsync(ct);

        var rates = await summaries.GetRatesAsync(householdId, today, ct);
        return PerformanceAnalyzer.Analyze(days, today, rates.ImportPricePerKwh);
    }

    public async Task<SustainabilityDto> SustainabilityAsync(Guid householdId, DateOnly today, CancellationToken ct)
    {
        var yearStart = new DateOnly(today.Year, 1, 1);
        var days = await LoadDaysAsync(householdId, yearStart, today, ct);
        var performance = await PerformanceAsync(householdId, today, ct);
        var co2 = configuration.GetValue("Sustainability:GridCo2KgPerKwh", InsightsCalculator.DefaultGridCo2KgPerKwh);
        return InsightsCalculator.Sustainability(today.Year, days, performance, co2);
    }

    // Kansen en prestatie veranderen langzaam (weer elke 6 uur): 15 minuten bewaren scheelt veel query's in het klantoverzicht.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, (DateTimeOffset At, PerformanceDto Performance, List<OpportunityDto> Opportunities)> InstallerCache = new();
    private static readonly TimeSpan InstallerCacheDuration = TimeSpan.FromMinutes(15);

    public async Task<(PerformanceDto Performance, List<OpportunityDto> Opportunities)> ForInstallerAsync(
        Guid householdId, DateOnly today, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (InstallerCache.TryGetValue(householdId, out var hit) && now - hit.At < InstallerCacheDuration)
        {
            return (hit.Performance, hit.Opportunities);
        }

        var result = await ComputeForInstallerAsync(householdId, today, ct);
        InstallerCache[householdId] = (now, result.Performance, result.Opportunities);
        return result;
    }

    private async Task<(PerformanceDto Performance, List<OpportunityDto> Opportunities)> ComputeForInstallerAsync(
        Guid householdId, DateOnly today, CancellationToken ct)
    {
        var performance = await PerformanceAsync(householdId, today, ct);
        var last30 = await LoadDaysAsync(householdId, today.AddDays(-30), today.AddDays(-1), ct);

        var types = await db.Devices
            .Where(d => d.HouseholdId == householdId)
            .Select(d => d.Type)
            .Distinct()
            .ToListAsync(ct);

        var isDynamic = await db.Tariffs
            .Where(t => t.HouseholdId == householdId && t.ValidFrom <= today)
            .OrderByDescending(t => t.ValidFrom)
            .Select(t => t.IsDynamic)
            .FirstOrDefaultAsync(ct);

        var rates = await summaries.GetRatesAsync(householdId, today, ct);
        var opportunities = InsightsCalculator.Opportunities(
            last30,
            hasBattery: types.Contains(DeviceType.Battery),
            hasCharger: types.Contains(DeviceType.Charger),
            isDynamic,
            rates,
            performance);

        return (performance, opportunities);
    }

    private Task<List<InsightDay>> LoadDaysAsync(Guid householdId, DateOnly from, DateOnly to, CancellationToken ct) =>
        db.DailySummaries
            .Where(s => s.HouseholdId == householdId && s.Date >= from && s.Date <= to)
            .OrderBy(s => s.Date)
            .Select(s => new InsightDay(s.Date, s.PvKwh, s.SelfUseKwh, s.ExportKwh, s.ImportKwh, s.ChargedKwh, s.BatteryDischargedKwh))
            .ToListAsync(ct);
}
