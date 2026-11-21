using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Api.Endpoints;
using Wattzicht.Api.Services;
using Wattzicht.Shared;
using Xunit;

namespace Wattzicht.Api.Tests;

/// <summary>
/// De bewoner koppelt zelf zijn installateur.
///
/// Dat kon alleen op het moment van aanmelden, met een code in het registratieformulier. Wie zich zelf
/// aanmeldde en pas daarna panelen liet leggen, kon zijn installateur daarna nooit meer laten meekijken.
/// Wie van bedrijf wisselde bleef aan het oude hangen. En sinds woningen zonder code bij niemand meer
/// horen, gold dat voor 132 van de 144 woningen.
///
/// De bewoner doet het, niet de installateur. Zou een installateur een woning aan zich kunnen koppelen
/// door een adres of e-mailadres in te typen, dan kan hij klanten van een ander naar zich toe trekken.
///
/// Deze tests leggen de regels van het koppelen vast. Ze roepen de endpoints via reflectie aan, want het
/// zijn private methodes van een minimal-API-groep.
/// </summary>
public sealed class KoppelInstallateurTests
{
    private readonly string _dbNaam = Guid.NewGuid().ToString();
    private static readonly Guid Woning = Guid.NewGuid();
    private static readonly Guid Bewoner = Guid.NewGuid();
    private static readonly Guid EchtBedrijf = Guid.NewGuid();

    private AppDbContext Db() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbNaam).Options, TenantContext.System);

    private static Installer Bedrijf(Guid id, string slug, DateTimeOffset? goedgekeurd) => new()
    {
        Id = id, Name = slug, Slug = slug, ApprovedAt = goedgekeurd, CreatedAt = DateTimeOffset.UtcNow,
    };

    private async Task<AppDbContext> KlaarAsync(Guid installateur, bool inzage = false)
    {
        var db = Db();
        await SysteemInstallateur.EnsureAsync(db, TimeProvider.System);
        db.Installers.Add(Bedrijf(EchtBedrijf, "zonnepietje", DateTimeOffset.UtcNow));
        db.Households.Add(new Household
        {
            Id = Woning, Name = "Thuis", InstallerId = installateur,
            InstallerAccessGranted = inzage, InstallerAccessChangedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return db;
    }

    /// <summary>De bewoner die is ingelogd; de endpoints lezen zijn woning uit de claims.</summary>
    private static HttpContext Context()
    {
        var http = new DefaultHttpContext();
        http.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
        [
            new System.Security.Claims.Claim("household_id", Woning.ToString()),
            new System.Security.Claims.Claim("sub", Bewoner.ToString()),
        ], "test"));
        return http;
    }

    private static async Task<object?> AanroepenAsync(string naam, object?[] argumenten)
    {
        var methode = typeof(HouseholdEndpoints).GetMethod(naam,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var taak = (Task)methode.Invoke(null, argumenten)!;
        await taak;
        return taak.GetType().GetProperty("Result")!.GetValue(taak);
    }

    private static Task<object?> KoppelAsync(AppDbContext db, string code) =>
        AanroepenAsync("KoppelInstallateurAsync",
            [new KoppelInstallateurRequest(code), Context(), db, TimeProvider.System, CancellationToken.None]);

    private static Task<object?> OntkoppelAsync(AppDbContext db) =>
        AanroepenAsync("OntkoppelInstallateurAsync", [Context(), db, TimeProvider.System, CancellationToken.None]);

    [Fact]
    public async Task Met_de_juiste_code_hangt_de_woning_aan_dat_bedrijf()
    {
        await using var db = await KlaarAsync(SysteemInstallateur.Id);

        await KoppelAsync(db, "zonnepietje");

        var woning = await db.Households.IgnoreQueryFilters().FirstAsync(h => h.Id == Woning);
        Assert.Equal(EchtBedrijf, woning.InstallerId);
    }

    /// <summary>
    /// Koppelen is tegelijk toestemming geven: een installateur koppelen die niets mag zien heeft geen
    /// betekenis. Dat staat ook zo op het scherm.
    /// </summary>
    [Fact]
    public async Task Koppelen_geeft_meteen_inzage()
    {
        await using var db = await KlaarAsync(SysteemInstallateur.Id);

        await KoppelAsync(db, "zonnepietje");

        var woning = await db.Households.IgnoreQueryFilters().FirstAsync(h => h.Id == Woning);
        Assert.True(woning.InstallerAccessGranted);
    }

    /// <summary>Hoofdletters of spaties in de code mogen niet uitmaken; hij staat op een sticker.</summary>
    [Fact]
    public async Task De_code_mag_rommelig_zijn_ingetikt()
    {
        await using var db = await KlaarAsync(SysteemInstallateur.Id);

        await KoppelAsync(db, "  ZonnePietje ");

        var woning = await db.Households.IgnoreQueryFilters().FirstAsync(h => h.Id == Woning);
        Assert.Equal(EchtBedrijf, woning.InstallerId);
    }

    [Fact]
    public async Task Een_onbekende_code_verandert_niets()
    {
        await using var db = await KlaarAsync(SysteemInstallateur.Id);

        await KoppelAsync(db, "bestaat-niet");

        var woning = await db.Households.IgnoreQueryFilters().FirstAsync(h => h.Id == Woning);
        Assert.Equal(SysteemInstallateur.Id, woning.InstallerId);
    }

    /// <summary>
    /// Een installateur die nog op goedkeuring wacht, mag geen klanten binnenhalen. Anders meldt iemand
    /// zich aan als installateur en haalt hij woningen naar zijn eigen portal.
    /// </summary>
    [Fact]
    public async Task Een_installateur_die_nog_wacht_op_goedkeuring_kan_niemand_koppelen()
    {
        await using var db = Db();
        await SysteemInstallateur.EnsureAsync(db, TimeProvider.System);
        db.Installers.Add(Bedrijf(Guid.NewGuid(), "nog-niet-gekeurd", goedgekeurd: null));
        db.Households.Add(new Household
        {
            Id = Woning, Name = "Thuis", InstallerId = SysteemInstallateur.Id,
            InstallerAccessChangedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        await KoppelAsync(db, "nog-niet-gekeurd");

        var woning = await db.Households.IgnoreQueryFilters().FirstAsync(h => h.Id == Woning);
        Assert.Equal(SysteemInstallateur.Id, woning.InstallerId);
    }

    /// <summary>"Geen installateur" is geen bedrijf waar je je aan kunt koppelen.</summary>
    [Fact]
    public async Task De_code_van_de_systeeminstallateur_werkt_niet()
    {
        await using var db = await KlaarAsync(EchtBedrijf, inzage: true);

        await KoppelAsync(db, SysteemInstallateur.Slug);

        var woning = await db.Households.IgnoreQueryFilters().FirstAsync(h => h.Id == Woning);
        Assert.Equal(EchtBedrijf, woning.InstallerId);
    }

    /// <summary>
    /// Loskoppelen zet de woning bij niemand. Alleen de inzage uitzetten is niet genoeg: dan blijft de
    /// naam van dat bedrijf in de app staan en komt hij terug zodra het vinkje weer aangaat.
    /// </summary>
    [Fact]
    public async Task Ontkoppelen_zet_de_woning_bij_niemand()
    {
        await using var db = await KlaarAsync(EchtBedrijf, inzage: true);

        await OntkoppelAsync(db);

        var woning = await db.Households.IgnoreQueryFilters().FirstAsync(h => h.Id == Woning);
        Assert.Equal(SysteemInstallateur.Id, woning.InstallerId);
        Assert.False(woning.InstallerAccessGranted);
    }

    /// <summary>Wisselen van installateur moet in één handeling kunnen.</summary>
    [Fact]
    public async Task Wisselen_van_installateur_vervangt_de_oude()
    {
        await using var db = await KlaarAsync(EchtBedrijf, inzage: true);
        var ander = Guid.NewGuid();
        db.Installers.Add(Bedrijf(ander, "andere-club", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        await KoppelAsync(db, "andere-club");

        var woning = await db.Households.IgnoreQueryFilters().FirstAsync(h => h.Id == Woning);
        Assert.Equal(ander, woning.InstallerId);
    }

    /// <summary>Koppelen en loskoppelen komen in het logboek dat de bewoner zelf kan inzien.</summary>
    [Fact]
    public async Task Koppelen_en_ontkoppelen_staan_in_het_logboek()
    {
        await using var db = await KlaarAsync(SysteemInstallateur.Id);

        await KoppelAsync(db, "zonnepietje");
        await OntkoppelAsync(db);

        var acties = await db.InstallerAccessLogs.IgnoreQueryFilters()
            .Where(l => l.HouseholdId == Woning).Select(l => l.Action).ToListAsync();
        Assert.Contains("gekoppeld", acties);
        Assert.Contains("ontkoppeld", acties);
    }
}
