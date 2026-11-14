using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Wattzicht.Api.Tests.Connectors;
using Wattzicht.Shared;
using Xunit;

namespace Wattzicht.Api.Tests;

/// <summary>
/// De geladen kilowatturen bij LaadCash ophalen.
///
/// Wie zijn laadpaal bij LaadCash heeft hangen hoeft hem hier niet nog eens te koppelen: wij halen
/// op wat er per dag doorheen ging en zetten het bij de dag. Twee dingen moeten daarbij kloppen.
///
/// Het mag niet dubbel tellen: meet deze woning zelf een laadpaal, dan is dat de bron en vult
/// LaadCash alleen de dagen waar wij niets hebben. En het moet stil blijven voor woningen die daar
/// geen klant zijn; daar gaat geen enkel verzoek heen.
/// </summary>
public class LaadcashLaaddataTests
{
    private static readonly Guid Woning = Guid.NewGuid();

    private static AppDbContext Db(string naam) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(naam).Options, TenantContext.System);

    private static LaadcashLaaddata Dienst(FakeVendor vendor, string? geheim = "geheim") =>
        new(vendor.Factory(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Laadcash:SharedSecret"] = geheim,
                ["Laadcash:ApiBase"] = "https://api.laadcash.nl",
            }).Build(),
            TimeProvider.System,
            NullLogger<LaadcashLaaddata>.Instance);

    private static Household Huis(bool gekoppeld = true) => new()
    {
        Id = Woning,
        Name = "Thuis",
        LaadcashLinkedAt = gekoppeld ? DateTimeOffset.UtcNow.AddDays(-3) : null,
    };

    private const string TweeDagen = """
    { "dagen": [ { "dag": "2026-11-12", "kwh": 11.5 }, { "dag": "2026-11-13", "kwh": 7.25 } ] }
    """;

    [Fact]
    public async Task De_dagen_komen_bij_de_woning_te_staan()
    {
        var vendor = new FakeVendor().Json("wattzicht/laaddata", TweeDagen);
        await using var db = Db(Guid.NewGuid().ToString());
        var huis = Huis();
        db.Households.Add(huis);
        await db.SaveChangesAsync();

        var gevuld = await Dienst(vendor).HaalAsync(db, huis, default);
        await db.SaveChangesAsync();

        Assert.Equal(2, gevuld);
        var rijen = await db.DailySummaries.IgnoreQueryFilters().OrderBy(s => s.Date).ToListAsync();
        Assert.Equal(11.5, rijen[0].ChargedKwh);
        Assert.Equal(7.25, rijen[1].ChargedKwh);
        Assert.NotNull(huis.LaadcashLaaddataAt);
    }

    [Fact]
    public async Task Het_geheim_gaat_mee_in_de_header()
    {
        var vendor = new FakeVendor().Json("wattzicht/laaddata", TweeDagen);
        await using var db = Db(Guid.NewGuid().ToString());
        var huis = Huis();
        db.Households.Add(huis);
        await db.SaveChangesAsync();

        await Dienst(vendor).HaalAsync(db, huis, default);

        var verzoek = Assert.Single(vendor.Requests);
        Assert.Equal("geheim", verzoek.Headers["X-Laadcash-Secret"]);
        Assert.Contains($"home={Woning}", verzoek.Uri.ToString());
    }

    [Fact]
    public async Task Een_eigen_laadpaal_wint_van_laadcash()
    {
        // Anders telt dezelfde laadbeurt twee keer: één keer door onze eigen meter en één keer
        // doordat LaadCash hem ook doorgeeft.
        var vendor = new FakeVendor().Json("wattzicht/laaddata", TweeDagen);
        await using var db = Db(Guid.NewGuid().ToString());
        var huis = Huis();
        db.Households.Add(huis);
        db.Devices.Add(new Device
        {
            Id = Guid.NewGuid(), HouseholdId = Woning, Type = DeviceType.Charger,
            Brand = DeviceBrand.Zaptec, Name = "Laadpaal", ExternalId = "zap-1",
        });
        db.DailySummaries.Add(new DailySummary
        {
            Id = Guid.NewGuid(), HouseholdId = Woning, Date = new DateOnly(2026, 11, 12),
            ChargedKwh = 9.4, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        await Dienst(vendor).HaalAsync(db, huis, default);
        await db.SaveChangesAsync();

        var eigen = await db.DailySummaries.IgnoreQueryFilters()
            .FirstAsync(s => s.Date == new DateOnly(2026, 11, 12));
        Assert.Equal(9.4, eigen.ChargedKwh);

        // De dag waar wij niets van hebben wordt wel gevuld.
        var gat = await db.DailySummaries.IgnoreQueryFilters()
            .FirstAsync(s => s.Date == new DateOnly(2026, 11, 13));
        Assert.Equal(7.25, gat.ChargedKwh);
    }

    [Fact]
    public async Task Een_woning_zonder_laadcash_vragen_we_niets()
    {
        var vendor = new FakeVendor().Json("wattzicht/laaddata", TweeDagen);
        await using var db = Db(Guid.NewGuid().ToString());
        var huis = Huis(gekoppeld: false);
        db.Households.Add(huis);
        await db.SaveChangesAsync();

        var gevuld = await Dienst(vendor).HaalAsync(db, huis, default);

        Assert.Equal(0, gevuld);
        Assert.Empty(vendor.Requests);
    }

    [Fact]
    public async Task Zonder_geheim_gebeurt_er_niets()
    {
        var vendor = new FakeVendor().Json("wattzicht/laaddata", TweeDagen);
        await using var db = Db(Guid.NewGuid().ToString());
        var huis = Huis();
        db.Households.Add(huis);
        await db.SaveChangesAsync();

        var dienst = Dienst(vendor, geheim: null);

        Assert.False(dienst.Ingesteld);
        Assert.Equal(0, await dienst.HaalAsync(db, huis, default));
        Assert.Empty(vendor.Requests);
    }

    [Fact]
    public async Task Een_server_die_niet_opneemt_laat_de_datum_staan()
    {
        // Dan probeert hij het morgen gewoon opnieuw in plaats van die dag over te slaan.
        var vendor = new FakeVendor().Unreachable("wattzicht/laaddata");
        await using var db = Db(Guid.NewGuid().ToString());
        var huis = Huis();
        db.Households.Add(huis);
        await db.SaveChangesAsync();

        var gevuld = await Dienst(vendor).HaalAsync(db, huis, default);

        Assert.Equal(0, gevuld);
        Assert.Null(huis.LaadcashLaaddataAt);
    }
}
