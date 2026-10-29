using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Services;

/// <summary>
/// Werkt de <see cref="DailySummary"/> van een huishouden bij door vermogen over de tijd te integreren
/// (trapeziumbenadering met het huidige vermogen × verstreken tijd sinds het vorige sample).
/// Daarna wordt de besparing opnieuw berekend met het tarief dat op die dag geldt.
/// </summary>
public sealed class SummaryService(AppDbContext db, SavingsCalculator calculator, TimeProvider clock)
{
    /// <summary>Langer dan dit tussen twee samples tellen we niet mee (apparaat was weg; niet extrapoleren).</summary>
    private static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(15);

    /// <summary>Verwerkt de actuele momentopname van een huishouden in de dagsamenvatting van vandaag.</summary>
    public async Task ApplySnapshotAsync(Guid householdId, LiveSnapshotDto snapshot, CancellationToken ct)
    {
        var now = snapshot.UpdatedAt ?? clock.GetUtcNow();
        var date = DutchTime.LocalDate(now);

        var summary = await db.DailySummaries
            .FirstOrDefaultAsync(s => s.HouseholdId == householdId && s.Date == date, ct);

        if (summary is null)
        {
            summary = new DailySummary
            {
                Id = Guid.NewGuid(),
                HouseholdId = householdId,
                Date = date,
                LastSampleAt = now,
                UpdatedAt = now
            };
            db.DailySummaries.Add(summary);
            return; // Eerste sample van de dag: nog geen interval om te integreren.
        }

        var previous = summary.LastSampleAt ?? now;
        var gap = now - previous;
        if (gap <= TimeSpan.Zero)
        {
            return; // Geen nieuwe meting sinds de vorige ronde.
        }

        if (gap <= MaxGap)
        {
            var hours = gap.TotalHours;

            var import = Math.Max(0, snapshot.GridPowerW);
            var export = Math.Max(0, -snapshot.GridPowerW);
            var batCharge = Math.Max(0, -snapshot.BatteryPowerW);
            var batDischarge = Math.Max(0, snapshot.BatteryPowerW);

            // Deel van de batterijlading dat van het net komt: laden terwijl er tegelijk wordt afgenomen.
            var batFromGrid = Math.Min(batCharge, import);
            var batFromPv = batCharge - batFromGrid;

            // Zelfverbruik van zon = zon − teruglevering − wat van de zon de batterij in ging
            // (die energie telt later bij ontladen mee).
            var selfUse = Math.Max(0, snapshot.PvPowerW - export - batFromPv);

            summary.PvKwh += Wh(snapshot.PvPowerW, hours);
            summary.ImportKwh += Wh(import, hours);
            summary.ExportKwh += Wh(export, hours);
            summary.ChargedKwh += Wh(snapshot.ChargerPowerW, hours);
            summary.BatteryChargedKwh += Wh(batCharge, hours);
            summary.BatteryDischargedKwh += Wh(batDischarge, hours);
            summary.BatteryChargedFromGridKwh += Wh(batFromGrid, hours);
            summary.SelfUseKwh += Wh(selfUse, hours);
        }

        summary.LastSampleAt = now;
        summary.UpdatedAt = clock.GetUtcNow();

        var rates = await GetRatesAsync(householdId, date, ct);
        summary.SavedEur = calculator.CalculateTotal(ToInput(summary), rates);
    }

    /// <summary>Herberekent alleen de besparing (bijvoorbeeld na een tariefwijziging) voor een datumbereik.</summary>
    public async Task RecalculateSavingsAsync(Guid householdId, DateOnly from, CancellationToken ct)
    {
        var summaries = await db.DailySummaries
            .Where(s => s.HouseholdId == householdId && s.Date >= from)
            .ToListAsync(ct);

        foreach (var summary in summaries)
        {
            var rates = await GetRatesAsync(householdId, summary.Date, ct);
            summary.SavedEur = calculator.CalculateTotal(ToInput(summary), rates);
            summary.UpdatedAt = clock.GetUtcNow();
        }
    }

    /// <summary>Tarief dat op een dag geldt: het laatste tarief met <c>ValidFrom &lt;= date</c>, anders de standaard.</summary>
    public async Task<TariffRates> GetRatesAsync(Guid householdId, DateOnly date, CancellationToken ct)
    {
        var tariff = await db.Tariffs
            .Where(t => t.HouseholdId == householdId && t.ValidFrom <= date)
            .OrderByDescending(t => t.ValidFrom)
            .FirstOrDefaultAsync(ct);

        return tariff is null
            ? TariffRates.Default
            : new TariffRates(tariff.ImportPricePerKwh, tariff.ExportPricePerKwh, tariff.PublicChargingPricePerKwh);
    }

    public static SavingsInput ToInput(DailySummary s) => new(
        s.Date,
        s.SelfUseKwh,
        s.ExportKwh,
        s.ChargedKwh,
        s.BatteryDischargedKwh,
        s.BatteryChargedFromGridKwh);

    /// <summary>Watt × uren → kWh.</summary>
    private static double Wh(double watt, double hours) => watt * hours / 1000d;
}
