using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Connectors;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Services;

/// <summary>
/// De proefwoning: zon, thuisbatterij, laadpaal en slimme meter in één keer, met geschiedenis.
///
/// Wattzicht is pas te beoordelen als er iets in staat. Een nieuwe gebruiker ziet lege schermen tot
/// hij een echt apparaat heeft gekoppeld, en wie dat apparaat niet heeft, ziet dus nooit waar de app
/// voor dient. Datzelfde gold voor onszelf: om de laadpaalkant te testen moest er een laadpaal in
/// huis hangen, en om de besparing te testen een slimme meter in de meterkast.
///
/// Er was al een demo-apparaat, maar maar één, en alleen vanaf vandaag. Een omvormer zonder meterkast
/// laat precies de helft van de app zien, en een grafiek van één dag laat de rest niet zien.
///
/// Deze proefwoning zet alle vier de soorten neer en vult de afgelopen weken alsof ze gemeten waren.
/// De simulatie is dezelfde als die van het demo-apparaat en is deterministisch: dezelfde dag geeft
/// altijd dezelfde curve, dus wat je gisteren zag staat er morgen nog. Zo is elk scherm te
/// beoordelen, van de live-stromen tot de jaargrafiek, en is het met één tik weer weg.
/// </summary>
public sealed class Proefwoning(
    AppDbContext db,
    CredentialProtector protector,
    SummaryService summaries,
    TimeProvider clock,
    ILogger<Proefwoning> logger)
{
    /// <summary>Hoeveel dagen geschiedenis we neerzetten. Twee maanden vult week, maand en jaargrafiek.</summary>
    public const int Dagen = 60;

    /// <summary>Om de hoeveel minuten we de curve bemonsteren bij het terugrekenen van een dag.</summary>
    private const int StapMinuten = 15;

    private static readonly (DeviceType Type, string Id, string Naam)[] Apparaten =
    [
        (DeviceType.Inverter, "DEMO-INV-001", "Demo omvormer 4,2 kWp"),
        (DeviceType.Battery, "DEMO-BAT-001", "Demo thuisbatterij 5 kWh"),
        (DeviceType.Charger, "DEMO-CHG-001", "Demo laadpaal 11 kW"),
        (DeviceType.P1Meter, "DEMO-P1-001", "Demo slimme meter"),
    ];

    /// <summary>Of deze woning al een proefopstelling heeft staan.</summary>
    public Task<bool> AanwezigAsync(Guid woning, CancellationToken ct = default) =>
        db.Devices.IgnoreQueryFilters()
            .AnyAsync(d => d.HouseholdId == woning && d.Brand == DeviceBrand.Demo, ct);

    /// <summary>
    /// Zet de proefopstelling neer en vul de geschiedenis.
    ///
    /// Bestaande dagen laten we met rust. Heeft iemand echte metingen staan, dan hoort de demo daar
    /// niet overheen te schrijven: dan staat er ineens een zonnedag in een week dat het regende.
    /// </summary>
    public async Task<int> ZetNeerAsync(Guid woning, CancellationToken ct = default)
    {
        var nu = clock.GetUtcNow();
        var leeg = protector.Protect(new Dictionary<string, string>());
        var bestaand = await db.Devices.IgnoreQueryFilters()
            .Where(d => d.HouseholdId == woning && d.Brand == DeviceBrand.Demo)
            .Select(d => d.ExternalId)
            .ToListAsync(ct);

        foreach (var (type, id, naam) in Apparaten)
        {
            if (bestaand.Contains(id)) continue;
            db.Devices.Add(new Device
            {
                Id = Guid.NewGuid(),
                HouseholdId = woning,
                Type = type,
                Brand = DeviceBrand.Demo,
                Name = naam,
                ExternalId = id,
                EncryptedCredentials = leeg,
                CreatedAt = nu,
            });
        }

        var geschreven = await VulGeschiedenisAsync(woning, nu, ct);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Proefwoning gezet voor {Woning}: {Dagen} dagen gevuld.", woning, geschreven);
        return geschreven;
    }

    /// <summary>
    /// De proefopstelling weghalen: de apparaten en alleen de dagen die wij hebben verzonnen.
    ///
    /// Een dag waar ook echte metingen in zitten blijft staan. Hem weggooien zou de opwek van een
    /// echte omvormer meenemen, en dat is erger dan een dag die te hoog staat.
    /// </summary>
    public async Task<int> HaalWegAsync(Guid woning, CancellationToken ct = default)
    {
        var demo = await db.Devices.IgnoreQueryFilters()
            .Where(d => d.HouseholdId == woning && d.Brand == DeviceBrand.Demo)
            .ToListAsync(ct);
        if (demo.Count == 0) return 0;

        var echteApparaten = await db.Devices.IgnoreQueryFilters()
            .AnyAsync(d => d.HouseholdId == woning && d.Brand != DeviceBrand.Demo, ct);

        db.Devices.RemoveRange(demo);

        var dagen = 0;
        if (!echteApparaten)
        {
            var rijen = await db.DailySummaries.IgnoreQueryFilters()
                .Where(s => s.HouseholdId == woning)
                .ToListAsync(ct);
            db.DailySummaries.RemoveRange(rijen);
            dagen = rijen.Count;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Proefwoning weggehaald bij {Woning}: {Dagen} dagen gewist.", woning, dagen);
        return dagen;
    }

    /// <summary>
    /// De afgelopen dagen vullen door de curve van elke dag na te rekenen en op te tellen.
    ///
    /// Vandaag slaan we over: die dag loopt nog en wordt gevuld door de gewone metingen, anders staat
    /// er om acht uur 's ochtends al een hele dag zon.
    /// </summary>
    private async Task<int> VulGeschiedenisAsync(Guid woning, DateTimeOffset nu, CancellationToken ct)
    {
        var vandaag = DutchTime.LocalDate(nu);
        var bestaandeDagen = await db.DailySummaries.IgnoreQueryFilters()
            .Where(s => s.HouseholdId == woning)
            .Select(s => s.Date)
            .ToListAsync(ct);
        var bekend = bestaandeDagen.ToHashSet();

        var geschreven = 0;
        for (var terug = Dagen; terug >= 1; terug--)
        {
            var dag = vandaag.AddDays(-terug);
            if (!bekend.Add(dag)) continue;

            var totaal = RekenDag(dag);
            var tarieven = await summaries.GetRatesAsync(woning, dag, ct);
            totaal.HouseholdId = woning;
            totaal.Id = Guid.NewGuid();
            totaal.SavedEur = new SavingsCalculator().CalculateTotal(SummaryService.ToInput(totaal), tarieven);
            totaal.UpdatedAt = nu;
            db.DailySummaries.Add(totaal);
            geschreven++;
        }

        return geschreven;
    }

    /// <summary>
    /// Eén dag uit de simulatie, opgeteld tot kilowatturen. Publiek zodat een test de dag kan nalopen.
    ///
    /// De verdeling volgt dezelfde regel als de echte metingen: zelfverbruik is zon min teruglevering
    /// min wat van de zon de batterij in ging. Zo reageert de proefwoning op precies dezelfde manier
    /// op het besparingsscherm als een echte woning.
    /// </summary>
    public static DailySummary RekenDag(DateOnly dag)
    {
        var totaal = new DailySummary { Date = dag };
        var uren = StapMinuten / 60d;
        var begin = new DateTimeOffset(dag.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        for (var minuut = 0; minuut < 24 * 60; minuut += StapMinuten)
        {
            var moment = begin.AddMinutes(minuut);
            var zon = DemoConnector.Simulate(DeviceType.Inverter, moment, "DEMO-INV-001");
            var meter = DemoConnector.Simulate(DeviceType.P1Meter, moment, "DEMO-P1-001");
            var accu = DemoConnector.Simulate(DeviceType.Battery, moment, "DEMO-BAT-001");
            var paal = DemoConnector.Simulate(DeviceType.Charger, moment, "DEMO-CHG-001");

            var afname = Math.Max(0, meter.GridPowerW);
            var teruglevering = Math.Max(0, -meter.GridPowerW);
            var accuLaadt = Math.Max(0, -accu.BatteryPowerW);
            var accuOntlaadt = Math.Max(0, accu.BatteryPowerW);
            var accuVanNet = Math.Min(accuLaadt, afname);
            var accuVanZon = accuLaadt - accuVanNet;
            var zelf = Math.Max(0, zon.PvPowerW - teruglevering - accuVanZon);

            totaal.PvKwh += Kwh(zon.PvPowerW, uren);
            totaal.ImportKwh += Kwh(afname, uren);
            totaal.ExportKwh += Kwh(teruglevering, uren);
            totaal.SelfUseKwh += Kwh(zelf, uren);
            totaal.ChargedKwh += Kwh(paal.ChargerPowerW, uren);
            totaal.BatteryChargedKwh += Kwh(accuLaadt, uren);
            totaal.BatteryDischargedKwh += Kwh(accuOntlaadt, uren);
            totaal.BatteryChargedFromGridKwh += Kwh(accuVanNet, uren);
        }

        Afronden(totaal);
        return totaal;
    }

    private static double Kwh(double watt, double uren) => watt * uren / 1000d;

    private static void Afronden(DailySummary s)
    {
        s.PvKwh = Math.Round(s.PvKwh, 3);
        s.ImportKwh = Math.Round(s.ImportKwh, 3);
        s.ExportKwh = Math.Round(s.ExportKwh, 3);
        s.SelfUseKwh = Math.Round(s.SelfUseKwh, 3);
        s.ChargedKwh = Math.Round(s.ChargedKwh, 3);
        s.BatteryChargedKwh = Math.Round(s.BatteryChargedKwh, 3);
        s.BatteryDischargedKwh = Math.Round(s.BatteryDischargedKwh, 3);
        s.BatteryChargedFromGridKwh = Math.Round(s.BatteryChargedFromGridKwh, 3);
    }
}
