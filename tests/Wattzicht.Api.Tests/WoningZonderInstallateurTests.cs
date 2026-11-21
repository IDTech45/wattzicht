using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Xunit;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Een woning hoort bij een installateur, en wie er geen heeft hoorde daardoor bij de verkeerde.
///
/// Meldde iemand zich aan zonder code, dan pakte de registratie de eerste installateur in de lijst. In de
/// praktijk was dat Triple Energie: op 21-11-2026 stonden alle 144 woningen op hun naam, terwijl de
/// meesten hen nooit hebben gezien. Zolang de bewoner geen inzage geeft ziet dat bedrijf hem niet, maar
/// de dag dat hij dat vinkje aanzet verschijnt hij in een portal waar hij niet thuishoort.
///
/// Deze tests leggen vast wie er wordt losgekoppeld en, belangrijker, wie niet: wie ooit met een code
/// binnenkwam en zijn inzage later heeft ingetrokken, blijft bij zijn eigen installateur staan.
/// </summary>
public sealed class WoningZonderInstallateurTests
{
    private readonly string _dbNaam = Guid.NewGuid().ToString();
    private static readonly Guid EchteInstallateur = Guid.NewGuid();
    private static readonly DateTimeOffset Aanmelding = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private AppDbContext Db() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbNaam).Options, TenantContext.System);

    private static Household Woning(string naam, bool inzage, DateTimeOffset gewijzigd) => new()
    {
        Id = Guid.NewGuid(), Name = naam, InstallerId = EchteInstallateur,
        InstallerAccessGranted = inzage, InstallerAccessChangedAt = gewijzigd, CreatedAt = Aanmelding,
    };

    [Fact]
    public async Task De_systeeminstallateur_wordt_een_keer_aangemaakt()
    {
        await using var db = Db();

        await SysteemInstallateur.EnsureAsync(db, TimeProvider.System);
        await SysteemInstallateur.EnsureAsync(db, TimeProvider.System);

        var alle = await db.Installers.IgnoreQueryFilters().Where(i => i.Id == SysteemInstallateur.Id).ToListAsync();
        Assert.Single(alle);
    }

    /// <summary>
    /// Zijn code mag nooit werken bij het aanmelden. Een goedgekeurde installateur kun je invullen; deze
    /// is bewust niet goedgekeurd, dus wijst de registratie hem af als iemand "wattzicht" intikt.
    /// </summary>
    [Fact]
    public async Task Zijn_code_is_niet_goedgekeurd_en_dus_niet_te_gebruiken()
    {
        await using var db = Db();
        await SysteemInstallateur.EnsureAsync(db, TimeProvider.System);

        var systeem = await db.Installers.IgnoreQueryFilters().FirstAsync(i => i.Id == SysteemInstallateur.Id);

        Assert.Null(systeem.ApprovedAt);
        Assert.Null(systeem.QuoteUrl);
        Assert.Null(systeem.LogoUrl);
    }

    [Fact]
    public async Task Wie_nooit_inzage_gaf_wordt_losgekoppeld()
    {
        await using var db = Db();
        await SysteemInstallateur.EnsureAsync(db, TimeProvider.System);
        var zelf = Woning("Zelf aangemeld", inzage: false, gewijzigd: Aanmelding);
        db.Households.Add(zelf);
        await db.SaveChangesAsync();

        var aantal = await SysteemInstallateur.LosseWoningenAsync(db);

        Assert.Equal(1, aantal);
        Assert.Equal(SysteemInstallateur.Id, zelf.InstallerId);
    }

    /// <summary>Met inzage hoort hij bij zijn installateur; daar blijven we vanaf.</summary>
    [Fact]
    public async Task Wie_inzage_geeft_blijft_bij_zijn_installateur()
    {
        await using var db = Db();
        await SysteemInstallateur.EnsureAsync(db, TimeProvider.System);
        var viaCode = Woning("Via de code", inzage: true, gewijzigd: Aanmelding);
        db.Households.Add(viaCode);
        await db.SaveChangesAsync();

        await SysteemInstallateur.LosseWoningenAsync(db);

        Assert.Equal(EchteInstallateur, viaCode.InstallerId);
    }

    /// <summary>
    /// Het randgeval dat dit filter nodig maakt: binnengekomen met een code en de inzage later
    /// ingetrokken. Die staat nu op "geen inzage", maar hoort wel degelijk bij zijn eigen installateur.
    /// Zet hij het vinkje ooit weer aan, dan verschijnt hij bij het bedrijf dat zijn panelen legde.
    /// </summary>
    [Fact]
    public async Task Wie_zijn_inzage_later_introk_blijft_staan_waar_hij_staat()
    {
        await using var db = Db();
        await SysteemInstallateur.EnsureAsync(db, TimeProvider.System);
        var ingetrokken = Woning("Later ingetrokken", inzage: false, gewijzigd: Aanmelding.AddDays(30));
        db.Households.Add(ingetrokken);
        await db.SaveChangesAsync();

        var aantal = await SysteemInstallateur.LosseWoningenAsync(db);

        Assert.Equal(0, aantal);
        Assert.Equal(EchteInstallateur, ingetrokken.InstallerId);
    }

    /// <summary>Twee keer draaien verandert niets; dit loopt bij elke start.</summary>
    [Fact]
    public async Task Nog_een_keer_draaien_koppelt_niets_extra_los()
    {
        await using var db = Db();
        await SysteemInstallateur.EnsureAsync(db, TimeProvider.System);
        db.Households.Add(Woning("Zelf aangemeld", inzage: false, gewijzigd: Aanmelding));
        await db.SaveChangesAsync();
        await SysteemInstallateur.LosseWoningenAsync(db);

        var tweede = await SysteemInstallateur.LosseWoningenAsync(db);

        Assert.Equal(0, tweede);
    }
}
