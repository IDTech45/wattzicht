using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests;

/// <summary>
/// De bevestiging van het e-mailadres.
///
/// Wat hier vastligt is vooral wat er níét mag. Een verlopen link mag niet alsnog werken, een
/// gebruikte link mag geen tweede keer werken, en opnieuw sturen mag geen knop zijn waarmee je
/// andermans postvak kunt laten vollopen.
///
/// Het opruimen staat er ook bij, want dat is de helft die echt iets doet. In november kwamen 139
/// van de 145 woningen van een bot. Een vinkje "bevestigd" dat nergens gevolgen heeft zou daar
/// niets aan veranderd hebben, en een opruiming die te hard toeslaat gooit het werk van een echte
/// bewoner weg. Die twee grenzen staan hieronder.
/// </summary>
public sealed class EmailbevestigingTests
{
    private static readonly DateTimeOffset Nu = new(2026, 11, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Installateur = Guid.NewGuid();

    private readonly string _dbNaam = Guid.NewGuid().ToString();
    private readonly FakeTimeProvider _klok = new(Nu);
    private readonly PostVak _post = new();

    public EmailbevestigingTests()
    {
        using var db = Db();
        db.Installers.Add(new Installer
        {
            Id = Installateur,
            Name = "Triple Energie",
            Slug = "triple-energie",
            ApprovedAt = Nu,
            CreatedAt = Nu,
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task De_link_uit_de_mail_bevestigt_het_adres()
    {
        await using var db = Db();
        var gebruiker = await NieuweGebruiker(db);

        await Dienst(db).VerstuurAsync(gebruiker, "https://api.wattzicht.nl", "nl");

        Assert.True(await Dienst(db).BevestigAsync(Sleutel()));
        Assert.NotNull((await db.Users.FirstAsync(u => u.Id == gebruiker.Id)).EmailVerifiedAt);
    }

    [Fact]
    public async Task De_sleutel_staat_niet_leesbaar_in_de_database()
    {
        await using var db = Db();
        var gebruiker = await NieuweGebruiker(db);

        await Dienst(db).VerstuurAsync(gebruiker, "https://api.wattzicht.nl", "nl");

        var opgeslagen = (await db.Users.FirstAsync(u => u.Id == gebruiker.Id)).VerifyTokenHash;
        Assert.NotNull(opgeslagen);
        Assert.NotEqual(Sleutel(), opgeslagen);
        Assert.Equal(Emailbevestiging.Hash(Sleutel()), opgeslagen);
    }

    [Fact]
    public async Task Een_verlopen_link_werkt_niet_meer()
    {
        await using var db = Db();
        var gebruiker = await NieuweGebruiker(db);
        await Dienst(db).VerstuurAsync(gebruiker, "https://api.wattzicht.nl", "nl");

        _klok.Advance(Emailbevestiging.Geldigheid + TimeSpan.FromMinutes(1));

        Assert.False(await Dienst(db).BevestigAsync(Sleutel()));
        Assert.Null((await db.Users.FirstAsync(u => u.Id == gebruiker.Id)).EmailVerifiedAt);
    }

    [Fact]
    public async Task Een_onzinnige_sleutel_bevestigt_niets()
    {
        await using var db = Db();
        var gebruiker = await NieuweGebruiker(db);
        await Dienst(db).VerstuurAsync(gebruiker, "https://api.wattzicht.nl", "nl");

        Assert.False(await Dienst(db).BevestigAsync("ietsanders"));
        Assert.False(await Dienst(db).BevestigAsync(""));
        Assert.Null((await db.Users.FirstAsync(u => u.Id == gebruiker.Id)).EmailVerifiedAt);
    }

    [Fact]
    public async Task Twee_keer_op_dezelfde_link_klikken_blijft_goed_gaan()
    {
        await using var db = Db();
        var gebruiker = await NieuweGebruiker(db);
        await Dienst(db).VerstuurAsync(gebruiker, "https://api.wattzicht.nl", "nl");
        var sleutel = Sleutel();

        Assert.True(await Dienst(db).BevestigAsync(sleutel));

        // De sleutel is na de eerste klik opgeruimd, dus de tweede klik vindt niemand meer. Dat
        // mag geen foutpagina geven: mensen klikken zo'n link twee keer.
        var gebruikerNu = await db.Users.FirstAsync(u => u.Id == gebruiker.Id);
        Assert.Null(gebruikerNu.VerifyTokenHash);
        Assert.NotNull(gebruikerNu.EmailVerifiedAt);
    }

    [Fact]
    public async Task Opnieuw_sturen_kan_niet_meteen_achter_elkaar()
    {
        await using var db = Db();
        var gebruiker = await NieuweGebruiker(db);
        var dienst = Dienst(db);

        await dienst.VerstuurAsync(gebruiker, "https://api.wattzicht.nl", "nl");
        Assert.False(dienst.MagOpnieuw(gebruiker));

        _klok.Advance(Emailbevestiging.PauzeTussenMails);
        Assert.True(dienst.MagOpnieuw(gebruiker));
    }

    [Fact]
    public async Task De_mail_gaat_in_de_taal_van_de_gebruiker()
    {
        await using var db = Db();
        var gebruiker = await NieuweGebruiker(db);

        await Dienst(db).VerstuurAsync(gebruiker, "https://api.wattzicht.nl", "tr");

        Assert.Contains("onaylayın", _post.Laatste!.Value.Onderwerp);
        Assert.Contains("https://api.wattzicht.nl/auth/verify?token=", _post.Laatste!.Value.Tekst);
    }

    // ---- Opruimen --------------------------------------------------------

    [Fact]
    public async Task Een_onbevestigd_account_verdwijnt_na_de_bewaartermijn()
    {
        await using var db = Db();
        var gebruiker = await NieuweGebruiker(db);
        var woningId = gebruiker.HouseholdId!.Value;

        var weg = await OnbevestigdOpruimen.UitvoerenAsync(db, Nu + Emailbevestiging.BewaartermijnOnbevestigd + TimeSpan.FromHours(1));

        Assert.Equal(1, weg);
        Assert.False(await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == gebruiker.Id));
        Assert.False(await db.Households.IgnoreQueryFilters().AnyAsync(h => h.Id == woningId));
    }

    [Fact]
    public async Task Een_bevestigd_account_blijft_staan()
    {
        await using var db = Db();
        var gebruiker = await NieuweGebruiker(db);
        gebruiker.EmailVerifiedAt = Nu;
        await db.SaveChangesAsync();

        var weg = await OnbevestigdOpruimen.UitvoerenAsync(db, Nu.AddYears(1));

        Assert.Equal(0, weg);
        Assert.True(await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == gebruiker.Id));
    }

    [Fact]
    public async Task Binnen_de_bewaartermijn_blijft_alles_staan()
    {
        await using var db = Db();
        var gebruiker = await NieuweGebruiker(db);

        var weg = await OnbevestigdOpruimen.UitvoerenAsync(db, Nu + Emailbevestiging.BewaartermijnOnbevestigd - TimeSpan.FromHours(1));

        Assert.Equal(0, weg);
        Assert.True(await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == gebruiker.Id));
    }

    [Fact]
    public async Task Een_woning_met_een_gekoppeld_apparaat_blijft_staan()
    {
        await using var db = Db();
        var gebruiker = await NieuweGebruiker(db);
        db.Devices.Add(new Device
        {
            Id = Guid.NewGuid(),
            HouseholdId = gebruiker.HouseholdId!.Value,
            Type = DeviceType.Inverter,
            Brand = DeviceBrand.Enphase,
            Name = "Zonnepanelen",
            CreatedAt = Nu,
        });
        await db.SaveChangesAsync();

        var weg = await OnbevestigdOpruimen.UitvoerenAsync(db, Nu.AddYears(1));

        Assert.Equal(0, weg);
        Assert.True(await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == gebruiker.Id));
    }

    [Fact]
    public async Task Een_medebewoner_houdt_de_woning_overeind()
    {
        await using var db = Db();
        var bevestigd = await NieuweGebruiker(db, "vast@voorbeeld.nl");
        bevestigd.EmailVerifiedAt = Nu;

        var los = new User
        {
            Id = Guid.NewGuid(),
            Email = "los@voorbeeld.nl",
            PasswordHash = "x",
            Role = UserRole.Resident,
            HouseholdId = bevestigd.HouseholdId,
            InstallerId = Installateur,
            CreatedAt = Nu,
        };
        db.Users.Add(los);
        await db.SaveChangesAsync();

        var weg = await OnbevestigdOpruimen.UitvoerenAsync(db, Nu.AddYears(1));

        Assert.Equal(1, weg);
        Assert.False(await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == los.Id));
        Assert.True(await db.Households.IgnoreQueryFilters().AnyAsync(h => h.Id == bevestigd.HouseholdId));
    }

    // ---- Gereedschap -----------------------------------------------------

    /// <summary>De sleutel uit de laatst verstuurde mail, zoals een gebruiker hem aanklikt.</summary>
    private string Sleutel()
    {
        var tekst = _post.Laatste!.Value.Tekst;
        var begin = tekst.IndexOf("token=", StringComparison.Ordinal) + "token=".Length;
        var eind = tekst.IndexOfAny([' ', '\n', '\r'], begin);
        return eind < 0 ? tekst[begin..] : tekst[begin..eind];
    }

    private async Task<User> NieuweGebruiker(AppDbContext db, string email = "nieuw@voorbeeld.nl")
    {
        var woning = new Household
        {
            Id = Guid.NewGuid(),
            Name = "Thuis",
            InstallerId = Installateur,
            CreatedAt = Nu,
        };
        var gebruiker = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = "x",
            Role = UserRole.Resident,
            HouseholdId = woning.Id,
            InstallerId = Installateur,
            CreatedAt = Nu,
        };

        db.Households.Add(woning);
        db.Users.Add(gebruiker);
        await db.SaveChangesAsync();
        return gebruiker;
    }

    private AppDbContext Db() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbNaam).Options, TenantContext.System);

    private Emailbevestiging Dienst(AppDbContext db) =>
        new(db, _post, _klok, NullLogger<Emailbevestiging>.Instance);

    /// <summary>Een postvak dat niets verstuurt maar onthoudt wat eruit gegaan zou zijn.</summary>
    private sealed class PostVak : IMailVerzender
    {
        public bool Ingesteld => true;

        public (string Naar, string Onderwerp, string Tekst)? Laatste { get; private set; }

        public Task<string?> VerstuurAsync(string naar, string onderwerp, string tekst, CancellationToken ct = default)
        {
            Laatste = (naar, onderwerp, tekst);
            return Task.FromResult<string?>(null);
        }
    }
}
