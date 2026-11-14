using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Zelf gebruikt en teruggeleverd komen uit de netmeting, niet uit de panelen.
///
/// De rekenregel is zon min teruglevering min wat van de zon de batterij in ging. Dat is juist,
/// maar het heeft een gevolg dat niet in de formule te zien is: bij een woning met alleen een
/// omvormer staat het net altijd op nul watt, dus is de teruglevering nul, dus is zelfverbruik
/// gelijk aan de hele opwek.
///
/// De app toonde dat als "100% zelfverbruik" en "0 kWh teruggeleverd" bij iemand die de halve dag
/// aan het net stond te leveren. Geen afrondingsfout, geen nul te veel: een verzonnen uitkomst, en
/// precies het getal waar een zonnepaneeleigenaar naar kijkt.
///
/// Deze twee tests leggen de reden vast waarom de app die twee cijfers wegstopt zolang
/// HasGridData onwaar is. De rekenregel zelf blijft zoals hij is; alleen het tonen verandert.
/// </summary>
public sealed class ZelfverbruikTests
{
    private readonly string _dbNaam = Guid.NewGuid().ToString();
    private static readonly Guid Woning = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private AppDbContext Db() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbNaam).Options, TenantContext.System);

    private static SummaryService Dienst(AppDbContext db) =>
        new(db, new SavingsCalculator(), TimeProvider.System);

    /// <summary>Een uur lang dit vermogen, en dan de dagsamenvatting terug.</summary>
    private async Task<DailySummary> EenUurAsync(double pv, double net)
    {
        await using var db = Db();
        var dienst = Dienst(db);

        // Het eerste sample zet alleen het beginpunt; vanaf het tweede wordt er geïntegreerd.
        await dienst.ApplySnapshotAsync(Woning, Momentopname(pv, net, Start), default);
        await db.SaveChangesAsync();
        await dienst.ApplySnapshotAsync(Woning, Momentopname(pv, net, Start.AddMinutes(12)), default);
        await db.SaveChangesAsync();

        return await db.DailySummaries.IgnoreQueryFilters()
            .FirstAsync(s => s.HouseholdId == Woning);
    }

    private static LiveSnapshotDto Momentopname(double pv, double net, DateTimeOffset op) =>
        new(PvPowerW: pv, LoadPowerW: 0, GridPowerW: net, BatteryPowerW: 0, BatterySoc: null,
            ChargerPowerW: 0, UpdatedAt: op);

    [Fact]
    public async Task Zonder_netmeting_lijkt_alles_zelf_gebruikt()
    {
        // Vijf kilowatt zon, en een net dat op nul staat omdat er niets te meten valt.
        var dag = await EenUurAsync(pv: 5000, net: 0);

        // Twaalf minuten van vijf kilowatt is één kilowattuur.
        Assert.Equal(1.0, dag.PvKwh, 2);
        Assert.Equal(0.0, dag.ExportKwh, 2);

        // En dit is waarom de app dit getal niet mag tonen zonder netmeting: het is de hele opwek.
        Assert.Equal(dag.PvKwh, dag.SelfUseKwh, 2);
    }

    [Fact]
    public async Task Met_netmeting_gaat_de_teruglevering_eraf()
    {
        // Dezelfde vijf kilowatt zon, maar nu levert de woning er vier van terug.
        var dag = await EenUurAsync(pv: 5000, net: -4000);

        Assert.Equal(1.0, dag.PvKwh, 2);
        Assert.Equal(0.8, dag.ExportKwh, 2);
        Assert.Equal(0.2, dag.SelfUseKwh, 2);
    }
}
