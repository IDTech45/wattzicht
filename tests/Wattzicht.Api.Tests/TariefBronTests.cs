using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Met welke tarieven er gerekend wordt, en waarom dat vanaf 2027 pas echt uitmaakt.
///
/// Tot en met 31 december 2026 telt elke teruggeleverde kilowattuur voor de volle leveringsprijs,
/// dus komt iedereen op hetzelfde uit. Daarna verschilt het per leverancier wat je terugkrijgt, en
/// rekenen de meeste er ook nog terugleverkosten bovenop. Bij een gemiddeld dak loopt het verschil
/// tussen twee leveranciers op tot honderden euro's per jaar.
///
/// Daar zit het verschil tussen Gratis en Plus: Gratis rekent met het landelijke gemiddelde (dan
/// klopt de orde van grootte), Plus met de leverancier van deze bewoner. Een gemiddelde tonen
/// alsof het zijn eigen cijfer is, zou erger zijn dan niets tonen.
/// </summary>
public sealed class TariefBronTests
{
    private static readonly Guid Installateur = Guid.NewGuid();
    private static readonly DateOnly In2026 = new(2026, 6, 1);
    private static readonly DateOnly In2027 = new(2027, 6, 1);
    private static readonly DateTimeOffset Nu = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dbName = Guid.NewGuid().ToString();

    public TariefBronTests()
    {
        using var db = Db();
        db.Installers.Add(new Installer
        {
            Id = Installateur, Name = "Triple Energie", Slug = "triple-energie", CreatedAt = Nu,
        });

        // Het landelijke gemiddelde dat Gratis krijgt, en twee echte leveranciers.
        db.SupplierTariffs.AddRange(
            Tarief(TariefBron.StandaardNaam, new DateOnly(2027, 1, 1), 0.05m, 0.11m),
            Tarief("Zuinige Energie", new DateOnly(2027, 1, 1), 0.09m, 0.02m),
            Tarief("Dure Energie", new DateOnly(2027, 1, 1), 0.02m, 0.19m));
        db.SaveChanges();
    }

    [Fact]
    public async Task Zonder_Plus_geldt_het_landelijke_gemiddelde()
    {
        await using var db = Db();
        var woning = await Woning(db, leverancier: "Zuinige Energie");

        var tarief = await new TariefBron(db).VoorWoningAsync(woning, In2027, heeftPlus: false);

        // Zijn eigen leverancier is bekend, maar zonder Plus rekenen we met het gemiddelde.
        Assert.Equal(0.05m, tarief.ExportPricePerKwh);
        Assert.Equal(0.11m, tarief.FeedInCostPerKwh);
    }

    [Fact]
    public async Task Met_Plus_geldt_de_eigen_leverancier()
    {
        await using var db = Db();
        var woning = await Woning(db, leverancier: "Zuinige Energie");

        var tarief = await new TariefBron(db).VoorWoningAsync(woning, In2027, heeftPlus: true);

        Assert.Equal(0.09m, tarief.ExportPricePerKwh);
        Assert.Equal(0.02m, tarief.FeedInCostPerKwh);
    }

    [Fact]
    public async Task Het_verschil_tussen_twee_leveranciers_is_geen_kleingeld()
    {
        await using var db = Db();
        var zuinig = await Woning(db, leverancier: "Zuinige Energie");
        var duur = await Woning(db, leverancier: "Dure Energie");
        var bron = new TariefBron(db);
        var rekenaar = new SavingsCalculator();

        // Een gemiddeld dak dat 2000 kWh per jaar teruglevert.
        var dag = new SavingsInput(In2027, SelfUseKwh: 0, ExportKwh: 2000, ChargedKwh: 0,
            BatteryDischargedKwh: 0, BatteryChargedFromGridKwh: 0);

        var bijZuinig = rekenaar.CalculateTotal(dag, await bron.VoorWoningAsync(zuinig, In2027, true));
        var bijDuur = rekenaar.CalculateTotal(dag, await bron.VoorWoningAsync(duur, In2027, true));

        // 2000 × (0,09 − 0,02) = 140 tegen 2000 × (0,02 − 0,19) = −340.
        Assert.Equal(140m, bijZuinig);
        Assert.Equal(-340m, bijDuur);
    }

    [Fact]
    public async Task In_2026_maakt_de_leverancier_niets_uit_want_er_wordt_gesaldeerd()
    {
        await using var db = Db();
        var zuinig = await Woning(db, leverancier: "Zuinige Energie");
        var duur = await Woning(db, leverancier: "Dure Energie");
        var bron = new TariefBron(db);
        var rekenaar = new SavingsCalculator();

        var dag = new SavingsInput(In2026, SelfUseKwh: 0, ExportKwh: 100, ChargedKwh: 0,
            BatteryDischargedKwh: 0, BatteryChargedFromGridKwh: 0);

        // Vóór 2027 gelden de tarieven van 2027 nog niet, dus beide woningen rekenen met wat ze zelf
        // hebben ingevuld en krijgen de volle leveringsprijs voor teruglevering.
        var a = rekenaar.CalculateTotal(dag, await bron.VoorWoningAsync(zuinig, In2026, true));
        var b = rekenaar.CalculateTotal(dag, await bron.VoorWoningAsync(duur, In2026, true));

        Assert.Equal(a, b);
    }

    [Fact]
    public async Task Een_oude_berekening_houdt_het_tarief_van_toen()
    {
        // Zuinige Energie verlaagt zijn vergoeding in juli. Wie in maart terugleverde moet het tarief
        // van maart houden, anders verandert zijn jaaroverzicht met terugwerkende kracht.
        await using var db = Db();
        db.SupplierTariffs.Add(Tarief("Zuinige Energie", new DateOnly(2027, 7, 1), 0.04m, 0.05m));
        await db.SaveChangesAsync();

        var woning = await Woning(db, leverancier: "Zuinige Energie");
        var bron = new TariefBron(db);

        Assert.Equal(0.09m, (await bron.VoorWoningAsync(woning, new DateOnly(2027, 3, 1), true)).ExportPricePerKwh);
        Assert.Equal(0.04m, (await bron.VoorWoningAsync(woning, new DateOnly(2027, 8, 1), true)).ExportPricePerKwh);
    }

    [Fact]
    public async Task Een_onbekende_leverancier_valt_terug_op_het_eigen_tarief()
    {
        await using var db = Db();
        var woning = await Woning(db, leverancier: "Nooit Van Gehoord");

        var tarief = await new TariefBron(db).VoorWoningAsync(woning, In2027, heeftPlus: true);

        // Zijn eigen ingevulde terugleververgoeding, niet die van het gemiddelde.
        Assert.Equal(0.08m, tarief.ExportPricePerKwh);
    }

    [Fact]
    public async Task De_keuzelijst_bevat_de_leveranciers_maar_niet_het_gemiddelde()
    {
        await using var db = Db();
        var lijst = await new TariefBron(db).LeveranciersAsync();

        Assert.Equal(["Dure Energie", "Zuinige Energie"], lijst);
        Assert.DoesNotContain(TariefBron.StandaardNaam, lijst);
    }

    // ---- Hulpstukken ------------------------------------------------------

    private static SupplierTariff Tarief(string leverancier, DateOnly vanaf, decimal vergoeding, decimal kosten) =>
        new()
        {
            Id = Guid.NewGuid(),
            Supplier = leverancier,
            ValidFrom = vanaf,
            ExportPricePerKwh = vergoeding,
            FeedInCostPerKwh = kosten,
            Source = "voorwaarden van de leverancier",
            CreatedAt = Nu,
        };

    private async Task<Guid> Woning(AppDbContext db, string leverancier)
    {
        var id = Guid.NewGuid();
        db.Households.Add(new Household
        {
            Id = id, Name = "Thuis", InstallerId = Installateur, SupplierName = leverancier, CreatedAt = Nu,
        });
        db.Tariffs.Add(new Tariff
        {
            Id = Guid.NewGuid(),
            HouseholdId = id,
            ValidFrom = new DateOnly(2026, 1, 1),
            ImportPricePerKwh = 0.28m,
            ExportPricePerKwh = 0.08m,
            PublicChargingPricePerKwh = 0.59m,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private AppDbContext Db() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, TenantContext.System);
}
