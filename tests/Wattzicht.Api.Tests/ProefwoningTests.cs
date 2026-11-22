using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Wattzicht.Shared;
using Xunit;

namespace Wattzicht.Api.Tests;

/// <summary>
/// De proefwoning: vier apparaten en twee maanden geschiedenis in één handeling.
///
/// Wattzicht is pas te beoordelen als er iets in staat. Een nieuwe gebruiker ziet lege schermen tot hij
/// een echt apparaat heeft gekoppeld, en wie dat apparaat niet heeft ziet dus nooit waar de app voor
/// dient. Voor onszelf gold hetzelfde: de laadpaalkant testen kon alleen met een laadpaal in huis.
///
/// Wat hier vastligt is vooral wat de proefwoning NIET mag doen: over echte metingen heen schrijven, of
/// bij het weghalen de opwek van een echte omvormer meenemen.
/// </summary>
public sealed class ProefwoningTests
{
    private readonly string _dbNaam = Guid.NewGuid().ToString();
    private static readonly Guid Woning = Guid.NewGuid();
    private static readonly DateTimeOffset Nu = new(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);

    private AppDbContext Db() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbNaam).Options, TenantContext.System);

    private static Proefwoning Dienst(AppDbContext db)
    {
        var klok = new VasteKlok(Nu);
        var protector = new CredentialProtector(Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create("wattzicht-tests"));
        return new Proefwoning(db, protector, new SummaryService(db, new SavingsCalculator(), klok), klok,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Proefwoning>.Instance);
    }

    private sealed class VasteKlok(DateTimeOffset nu) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nu;
    }

    private async Task<AppDbContext> MetWoningAsync()
    {
        var db = Db();
        db.Households.Add(new Household
        {
            Id = Woning, Name = "Thuis", InstallerId = Guid.NewGuid(),
            CreatedAt = Nu.AddYears(-1), InstallerAccessChangedAt = Nu.AddYears(-1),
        });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Er_komen_vier_apparaten_te_staan()
    {
        await using var db = await MetWoningAsync();

        await Dienst(db).ZetNeerAsync(Woning);

        var soorten = await db.Devices.IgnoreQueryFilters()
            .Where(d => d.HouseholdId == Woning).Select(d => d.Type).ToListAsync();
        Assert.Equal(4, soorten.Count);
        Assert.Contains(DeviceType.Inverter, soorten);
        Assert.Contains(DeviceType.Battery, soorten);
        Assert.Contains(DeviceType.Charger, soorten);
        Assert.Contains(DeviceType.P1Meter, soorten);
    }

    /// <summary>Zonder geschiedenis laat een grafiek van één dag de rest van de app niet zien.</summary>
    [Fact]
    public async Task De_afgelopen_twee_maanden_staan_gevuld()
    {
        await using var db = await MetWoningAsync();

        await Dienst(db).ZetNeerAsync(Woning);

        var dagen = await db.DailySummaries.IgnoreQueryFilters().Where(s => s.HouseholdId == Woning).ToListAsync();
        Assert.Equal(Proefwoning.Dagen, dagen.Count);
        Assert.All(dagen, d => Assert.True(d.PvKwh >= 0));
        Assert.Contains(dagen, d => d.ChargedKwh > 0);
        Assert.Contains(dagen, d => d.ExportKwh > 0);
    }

    /// <summary>Vandaag loopt nog; die dag wordt door de gewone metingen gevuld.</summary>
    [Fact]
    public async Task Vandaag_blijft_leeg()
    {
        await using var db = await MetWoningAsync();

        await Dienst(db).ZetNeerAsync(Woning);

        var vandaag = DutchTime.LocalDate(Nu);
        Assert.DoesNotContain(await db.DailySummaries.IgnoreQueryFilters().Select(s => s.Date).ToListAsync(),
            d => d == vandaag);
    }

    /// <summary>
    /// Een dag waar al iets in staat blijft zoals hij is. Anders staat er ineens een zonnedag in een
    /// week dat het regende, bij iemand met een echte omvormer.
    /// </summary>
    [Fact]
    public async Task Een_bestaande_dag_wordt_niet_overschreven()
    {
        await using var db = await MetWoningAsync();
        var echteDag = DutchTime.LocalDate(Nu).AddDays(-3);
        db.DailySummaries.Add(new DailySummary
        {
            Id = Guid.NewGuid(), HouseholdId = Woning, Date = echteDag, PvKwh = 1.23, UpdatedAt = Nu,
        });
        await db.SaveChangesAsync();

        await Dienst(db).ZetNeerAsync(Woning);

        var dag = await db.DailySummaries.IgnoreQueryFilters().FirstAsync(s => s.Date == echteDag);
        Assert.Equal(1.23, dag.PvKwh);
    }

    [Fact]
    public async Task Twee_keer_neerzetten_levert_geen_dubbele_apparaten()
    {
        await using var db = await MetWoningAsync();
        var proef = Dienst(db);

        await proef.ZetNeerAsync(Woning);
        var tweede = await proef.ZetNeerAsync(Woning);

        Assert.Equal(0, tweede);
        Assert.Equal(4, await db.Devices.IgnoreQueryFilters().CountAsync(d => d.HouseholdId == Woning));
    }

    [Fact]
    public async Task Weghalen_ruimt_de_apparaten_en_de_dagen_op()
    {
        await using var db = await MetWoningAsync();
        var proef = Dienst(db);
        await proef.ZetNeerAsync(Woning);

        await proef.HaalWegAsync(Woning);

        Assert.Equal(0, await db.Devices.IgnoreQueryFilters().CountAsync(d => d.HouseholdId == Woning));
        Assert.Equal(0, await db.DailySummaries.IgnoreQueryFilters().CountAsync(s => s.HouseholdId == Woning));
    }

    /// <summary>
    /// Hangt er ook een echt apparaat, dan blijven de dagen staan bij het weghalen. Ze weggooien zou de
    /// opwek van een echte omvormer meenemen, en dat is erger dan een dag die te hoog staat.
    /// </summary>
    [Fact]
    public async Task Met_een_echt_apparaat_erbij_blijven_de_dagen_staan()
    {
        await using var db = await MetWoningAsync();
        var proef = Dienst(db);
        await proef.ZetNeerAsync(Woning);
        db.Devices.Add(new Device
        {
            Id = Guid.NewGuid(), HouseholdId = Woning, Type = DeviceType.Inverter, Brand = DeviceBrand.Enphase,
            Name = "Echt dak", ExternalId = "ECHT-1", EncryptedCredentials = "x", CreatedAt = Nu,
        });
        await db.SaveChangesAsync();

        await proef.HaalWegAsync(Woning);

        Assert.Equal(Proefwoning.Dagen, await db.DailySummaries.IgnoreQueryFilters().CountAsync(s => s.HouseholdId == Woning));
        Assert.Equal(1, await db.Devices.IgnoreQueryFilters().CountAsync(d => d.HouseholdId == Woning));
    }

    /// <summary>
    /// Een dag uit de simulatie moet op zichzelf kloppen: in de zomer meer zon dan in december, en
    /// zelfverbruik nooit meer dan de opwek. Anders staat er een besparing die niet kan.
    /// </summary>
    [Fact]
    public void Een_zomerdag_levert_meer_op_dan_een_winterdag()
    {
        var juni = Proefwoning.RekenDag(new DateOnly(2026, 6, 21));
        var december = Proefwoning.RekenDag(new DateOnly(2026, 12, 21));

        Assert.True(juni.PvKwh > december.PvKwh);
        Assert.True(juni.SelfUseKwh <= juni.PvKwh);
        Assert.True(december.SelfUseKwh <= december.PvKwh);
    }

    /// <summary>Dezelfde dag geeft altijd dezelfde uitkomst; anders verspringt de geschiedenis.</summary>
    [Fact]
    public void Dezelfde_dag_geeft_altijd_hetzelfde()
    {
        var eerste = Proefwoning.RekenDag(new DateOnly(2026, 5, 4));
        var tweede = Proefwoning.RekenDag(new DateOnly(2026, 5, 4));

        Assert.Equal(eerste.PvKwh, tweede.PvKwh);
        Assert.Equal(eerste.ChargedKwh, tweede.ChargedKwh);
    }
}
