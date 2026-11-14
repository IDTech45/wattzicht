using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Wat je vandaag hebt opgewekt.
///
/// Dit stond bij een hele groep bewoners op nul, maandenlang. De dagopbrengst werd opgeteld uit
/// losse vermogensmetingen, en dat werkt alleen bij een vaste meetfrequentie. Wie een kastje heeft
/// dat de app zelf uitleest (Enphase, HomeWizard, Sessy, Alfen, Peblar) levert alleen een meting
/// als hij thuis is en de app openmaakt. Zo'n optelling komt dan nooit in de buurt.
///
/// Een omvormer houdt zelf een teller bij die alleen maar oploopt. Deze test legt vast dat we die
/// gebruiken, en vooral dat hij blijft kloppen in de gevallen waar het misgaat: een app die een dag
/// dicht stond, een omvormer die vervangen wordt, en twee omvormers op een dak.
/// </summary>
public sealed class DagopwekTests
{
    private readonly string _dbNaam = Guid.NewGuid().ToString();
    private static readonly Guid Woning = Guid.NewGuid();

    private AppDbContext Db() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbNaam).Options, TenantContext.System);

    private static SummaryService Dienst(AppDbContext db) =>
        new(db, new SavingsCalculator(), TimeProvider.System);

    private static Device Apparaat(Guid id) => new()
    {
        Id = id,
        HouseholdId = Woning,
        Type = DeviceType.Inverter,
        Brand = DeviceBrand.Enphase,
        Name = "Zonnepanelen",
        ExternalId = id.ToString(),
    };

    private static DateTimeOffset Op(int dag, int uur) =>
        new(2026, 11, dag, uur, 0, 0, TimeSpan.FromHours(1));

    private static async Task<double> OpwekAsync(AppDbContext db, int dag) =>
        (await db.DailySummaries.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.HouseholdId == Woning && s.Date == new DateOnly(2026, 11, dag)))?.PvKwh ?? 0;

    [Fact]
    public async Task Het_verschil_met_gisteravond_is_de_opwek_van_vandaag()
    {
        var id = Guid.NewGuid();
        await using var db = Db();
        var apparaat = Apparaat(id);
        db.Devices.Add(apparaat);
        await db.SaveChangesAsync();

        var dienst = Dienst(db);

        // Gisteren eindigde de teller op 1.000 kWh.
        await dienst.ApplyPvCounterAsync(apparaat, 1_000_000, Op(10, 21), default);
        await db.SaveChangesAsync();

        // Vandaag loopt hij door naar 1.012 kWh: twaalf kilowattuur erbij.
        await dienst.ApplyPvCounterAsync(apparaat, 1_006_000, Op(11, 12), default);
        await dienst.ApplyPvCounterAsync(apparaat, 1_012_000, Op(11, 19), default);
        await db.SaveChangesAsync();

        Assert.Equal(12, await OpwekAsync(db, 11));
    }

    [Fact]
    public async Task Een_nul_wist_een_dag_niet_die_al_opwek_heeft()
    {
        // Een omvormer of gateway die zijn dagstand kwijt is, meldt nul. Dat overschreef de opwek die
        // er die ochtend al stond, en dan staat de app op nul terwijl de zon gewoon geschenen heeft.
        var id = Guid.NewGuid();
        await using var db = Db();
        var apparaat = Apparaat(id);
        db.Devices.Add(apparaat);
        await db.SaveChangesAsync();

        var dienst = Dienst(db);
        await dienst.ApplyPvTodayAsync(apparaat, 4_120, Op(14, 12), default);
        await db.SaveChangesAsync();
        Assert.Equal(4.12, await OpwekAsync(db, 14));

        await dienst.ApplyPvTodayAsync(apparaat, 0, Op(14, 14), default);
        await db.SaveChangesAsync();

        Assert.Equal(4.12, await OpwekAsync(db, 14));
    }

    [Fact]
    public async Task Een_dag_waarop_de_app_dicht_bleef_telt_gewoon_mee()
    {
        // Dit is het hele punt. De app stond de 12e niet open, dus er kwam die dag geen enkele
        // meting binnen. Op de 13e staat de teller alsnog hoger, en die aangroei hoort bij de 13e.
        // Met een optelling van vermogen was de 12e nul geweest en de 13e ook.
        var id = Guid.NewGuid();
        await using var db = Db();
        var apparaat = Apparaat(id);
        db.Devices.Add(apparaat);
        await db.SaveChangesAsync();

        var dienst = Dienst(db);
        await dienst.ApplyPvCounterAsync(apparaat, 1_000_000, Op(11, 20), default);
        await dienst.ApplyPvCounterAsync(apparaat, 1_030_000, Op(13, 18), default);
        await db.SaveChangesAsync();

        Assert.Equal(30, await OpwekAsync(db, 13));
    }

    [Fact]
    public async Task Een_vervangen_omvormer_geeft_geen_negatieve_dag()
    {
        var id = Guid.NewGuid();
        await using var db = Db();
        var apparaat = Apparaat(id);
        db.Devices.Add(apparaat);
        await db.SaveChangesAsync();

        var dienst = Dienst(db);
        await dienst.ApplyPvCounterAsync(apparaat, 9_000_000, Op(11, 20), default);

        // Nieuwe omvormer, teller begint weer bij nul en loopt die dag naar 4 kWh.
        await dienst.ApplyPvCounterAsync(apparaat, 1_000, Op(12, 9), default);
        await dienst.ApplyPvCounterAsync(apparaat, 5_000, Op(12, 17), default);
        await db.SaveChangesAsync();

        Assert.Equal(4, await OpwekAsync(db, 12));
    }

    [Fact]
    public async Task Twee_omvormers_worden_bij_elkaar_opgeteld()
    {
        var een = Guid.NewGuid();
        var twee = Guid.NewGuid();
        await using var db = Db();
        var voor = Apparaat(een);
        var achter = Apparaat(twee);
        db.Devices.AddRange(voor, achter);
        await db.SaveChangesAsync();

        var dienst = Dienst(db);
        await dienst.ApplyPvCounterAsync(voor, 500_000, Op(11, 21), default);
        await dienst.ApplyPvCounterAsync(achter, 300_000, Op(11, 21), default);
        await db.SaveChangesAsync();

        await dienst.ApplyPvCounterAsync(voor, 508_000, Op(12, 19), default);
        await dienst.ApplyPvCounterAsync(achter, 305_000, Op(12, 19), default);
        await db.SaveChangesAsync();

        Assert.Equal(13, await OpwekAsync(db, 12));
    }

    [Fact]
    public async Task Een_lege_teller_verandert_niets()
    {
        var id = Guid.NewGuid();
        await using var db = Db();
        var apparaat = Apparaat(id);
        db.Devices.Add(apparaat);
        await db.SaveChangesAsync();

        var dienst = Dienst(db);
        await dienst.ApplyPvCounterAsync(apparaat, 0, Op(11, 12), default);
        await db.SaveChangesAsync();

        Assert.Equal(0, await OpwekAsync(db, 11));
        Assert.Null(apparaat.PvCounterDate);
    }
}
