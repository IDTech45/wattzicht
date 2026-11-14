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

        var summary = await DagAsync(householdId, date, ct);

        if (summary is null)
        {
            summary = Nieuw(householdId, date, now);
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

    /// <summary>
    /// De dagopbrengst uit de opwekteller van het apparaat zelf.
    ///
    /// Het optellen van vermogen over de tijd werkt alleen bij een vaste meetfrequentie. Een kastje
    /// dat de app uitleest meet alleen als iemand thuis is en de app openmaakt, en daar stond de
    /// dagopbrengst dus altijd op nul of veel te laag. Een teller die alleen maar oploopt heeft dat
    /// probleem niet: het verschil tussen nu en gisteravond is wat er vandaag is opgewekt, hoe vaak
    /// wij ook gekeken hebben.
    ///
    /// De teller wint van de optelling als hij er is. De verdeling eronder (zelf gebruikt,
    /// teruggeleverd) blijft uit de optelling komen; daarvoor is een teller alleen niet genoeg.
    /// </summary>
    /// <summary>
    /// Meer dan dit op een dag bestaat niet op een huisdak.
    ///
    /// Honderdvijftig kilowattuur is al een installatie van vijftien kilowattpiek op een perfecte
    /// junidag. Komt er meer uit, dan is het geen opbrengst maar een sprong in de teller: een
    /// vervangen omvormer, of een bron die wisselt. Dat overkwam ons op 14-11-2026, toen een
    /// kapotte CT-klem plaatsmaakte voor de echte teller en de hele levensopbrengst van 8.036 kWh
    /// als dagopbrengst in de app verscheen.
    /// </summary>
    public const double MaximumDagKwh = 150;

    public async Task ApplyPvCounterAsync(Device apparaat, double tellerWh, DateTimeOffset gemetenOp, CancellationToken ct)
    {
        if (tellerWh <= 0) return;

        var dag = DutchTime.LocalDate(gemetenOp);

        if (apparaat.PvCounterDate != dag)
        {
            // Nieuwe dag. De beginstand is wat er gisteravond stond; staat die er niet, dan begint de
            // telling vandaag bij deze meting en is de eerste dag onvolledig. Dat is één keer, en
            // beter dan een dag die er nooit komt.
            apparaat.PvCounterStartWh = apparaat.PvCounterLastWh ?? tellerWh;
            apparaat.PvCounterDate = dag;
        }

        // Een teller die terugloopt betekent een vervangen of gereset apparaat. Dan is het verschil
        // met gisteren betekenisloos en beginnen we opnieuw, in plaats van een negatieve dag te tonen.
        if (apparaat.PvCounterStartWh is null || tellerWh < apparaat.PvCounterStartWh)
        {
            apparaat.PvCounterStartWh = tellerWh;
        }

        apparaat.PvCounterLastWh = tellerWh;

        var summary = await DagAsync(apparaat.HouseholdId, dag, ct)
                      ?? Nieuw(apparaat.HouseholdId, dag, gemetenOp);

        // Alle apparaten van deze woning die vandaag een teller hebben, bij elkaar. Zo klopt het ook
        // bij twee omvormers, die elk op hun eigen moment een stand doorgeven.
        //
        // Let op de volgorde: eerst wat er in dit verzoek al is aangepast, dan pas de database. Twee
        // omvormers die vlak na elkaar een stand doorgeven worden anders allebei tegen de oude stand
        // gelegd, en dan telt de ene de ander weg.
        var idsUitGeheugen = db.Devices.Local
            .Where(d => d.HouseholdId == apparaat.HouseholdId && d.PvCounterDate == dag)
            .ToList();

        var uitDatabase = await db.Devices.IgnoreQueryFilters()
            .Where(d => d.HouseholdId == apparaat.HouseholdId && d.PvCounterDate == dag)
            .Select(d => new { d.Id, d.PvCounterStartWh, d.PvCounterLastWh })
            .ToListAsync(ct);

        var standen = new Dictionary<Guid, (double? Begin, double? Laatst)>();
        foreach (var d in uitDatabase) standen[d.Id] = (d.PvCounterStartWh, d.PvCounterLastWh);
        foreach (var d in idsUitGeheugen) standen[d.Id] = (d.PvCounterStartWh, d.PvCounterLastWh);
        standen[apparaat.Id] = (apparaat.PvCounterStartWh, apparaat.PvCounterLastWh);

        var totaalWh = standen.Values.Sum(x => Verschil(x.Begin, x.Laatst));
        var totaalKwh = totaalWh / 1000d;

        // Een sprong die geen dag kan zijn: de teller is van bron gewisseld of het apparaat is
        // vervangen. Dan beginnen we hier opnieuw in plaats van die sprong als opbrengst te boeken.
        // De rest van de dag telt gewoon door; alleen het stuk tot nu toe is niet te achterhalen.
        if (totaalKwh > MaximumDagKwh)
        {
            apparaat.PvCounterStartWh = tellerWh;
            apparaat.PvCounterLastWh = tellerWh;
            summary.PvKwh = 0;
            summary.UpdatedAt = clock.GetUtcNow();
            return;
        }

        summary.PvKwh = Math.Round(totaalKwh, 3);
        summary.UpdatedAt = clock.GetUtcNow();

        var rates = await GetRatesAsync(apparaat.HouseholdId, dag, ct);
        summary.SavedEur = calculator.CalculateTotal(ToInput(summary), rates);
    }

    /// <summary>
    /// De dag van een woning, ook als hij in dit verzoek net is aangemaakt.
    ///
    /// Dit ging mis: een vraag aan de database ziet niet wat er in deze aanvraag al is toegevoegd
    /// maar nog niet opgeslagen. Twee stappen in hetzelfde verzoek maakten daardoor allebei een
    /// dagtotaal aan, en de tweede schreef in een rij die niemand meer opvroeg. Op het scherm bleef
    /// het dus op nul staan, precies het probleem dat dit alles moest oplossen.
    /// </summary>
    private async Task<DailySummary?> DagAsync(Guid householdId, DateOnly dag, CancellationToken ct) =>
        db.DailySummaries.Local.FirstOrDefault(s => s.HouseholdId == householdId && s.Date == dag)
        ?? await db.DailySummaries.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.HouseholdId == householdId && s.Date == dag, ct);

    private DailySummary Nieuw(Guid householdId, DateOnly dag, DateTimeOffset moment)
    {
        var summary = new DailySummary
        {
            Id = Guid.NewGuid(),
            HouseholdId = householdId,
            Date = dag,
            LastSampleAt = moment,
            UpdatedAt = moment,
        };

        db.DailySummaries.Add(summary);
        return summary;
    }

    private static double Verschil(double? begin, double? laatst) =>
        begin is { } b && laatst is { } l && l > b ? l - b : 0;

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
            : new TariffRates(tariff.ImportPricePerKwh, tariff.ExportPricePerKwh, tariff.PublicChargingPricePerKwh, tariff.FeedInCostPerKwh);
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
