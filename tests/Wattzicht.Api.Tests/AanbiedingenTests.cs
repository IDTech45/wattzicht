using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests;

/// <summary>
/// De aanbiedingen-tegel. Geen advertenties van derden, en alleen iets tonen als de eigen data van
/// deze bewoner het rechtvaardigt.
///
/// Dat laatste is de hele truc. Een batterij-tegel bij iemand die al een batterij heeft is geen
/// aanbieding maar ruis, en na twee van die tegels kijkt niemand er nog naar. Daarom: alleen bij
/// veel teruglevering, alleen zonder batterij, en nooit bij Plus.
/// </summary>
public sealed class AanbiedingenTests
{
    private static readonly Guid Installateur = Guid.NewGuid();
    private static readonly DateTimeOffset Nu = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly FakeTimeProvider _klok = new(Nu);

    public AanbiedingenTests()
    {
        using var db = Db();
        db.Installers.Add(new Installer
        {
            Id = Installateur, Name = "Triple Energie", Slug = "triple-energie", CreatedAt = Nu,
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task Veel_teruglevering_zonder_batterij_geeft_een_offerte_tegel()
    {
        await using var db = Db();
        var woning = await Woning(db, metBatterij: false);
        await Dagen(db, woning, opwekPerDag: 20, terugPerDag: 12);   // 60% terug

        var kaarten = await Aanbiedingen(db).VoorWoningAsync(woning, heeftPlus: false);

        var kaart = Assert.Single(kaarten);
        Assert.Equal(OfferKind.Battery, kaart.Kind);
        Assert.Contains("60%", kaart.Body);
        Assert.Contains("Triple Energie", kaart.ActionLabel);
    }

    [Fact]
    public async Task Met_een_batterij_tonen_we_geen_batterij_tegel()
    {
        await using var db = Db();
        var woning = await Woning(db, metBatterij: true);
        await Dagen(db, woning, opwekPerDag: 20, terugPerDag: 12);

        Assert.Empty(await Aanbiedingen(db).VoorWoningAsync(woning, heeftPlus: false));
    }

    [Fact]
    public async Task Weinig_teruglevering_is_geen_reden()
    {
        await using var db = Db();
        var woning = await Woning(db, metBatterij: false);
        await Dagen(db, woning, opwekPerDag: 20, terugPerDag: 4);    // 20% terug

        Assert.Empty(await Aanbiedingen(db).VoorWoningAsync(woning, heeftPlus: false));
    }

    [Fact]
    public async Task Zonder_opwek_zeggen_we_niets()
    {
        // Een percentage van nul is geen reden; dan weet je gewoon niets over deze woning.
        await using var db = Db();
        var woning = await Woning(db, metBatterij: false);
        await Dagen(db, woning, opwekPerDag: 0, terugPerDag: 0);

        Assert.Empty(await Aanbiedingen(db).VoorWoningAsync(woning, heeftPlus: false));
    }

    [Fact]
    public async Task Plus_gebruikers_zien_deze_tegel_nooit()
    {
        await using var db = Db();
        var woning = await Woning(db, metBatterij: false);
        await Dagen(db, woning, opwekPerDag: 20, terugPerDag: 12);

        Assert.Empty(await Aanbiedingen(db).VoorWoningAsync(woning, heeftPlus: true));
    }

    [Fact]
    public async Task Een_aanvraag_komt_als_lead_bij_de_installateur_terecht()
    {
        await using var db = Db();
        var woning = await Woning(db, metBatterij: false);

        var lead = await Aanbiedingen(db).AanvraagAsync(woning, OfferKind.Battery);

        Assert.Equal("Triple Energie", lead.Company);
        Assert.Contains(OfferKind.Battery, lead.Source);
        Assert.Contains(woning.ToString(), lead.Source);
        Assert.Equal(1, await db.Leads.CountAsync());
    }

    // ---- Hulpstukken ------------------------------------------------------

    private async Task<Guid> Woning(AppDbContext db, bool metBatterij)
    {
        var id = Guid.NewGuid();
        db.Households.Add(new Household { Id = id, Name = "Thuis", InstallerId = Installateur, CreatedAt = Nu });
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(), Email = $"{id:N}@voorbeeld.nl", PasswordHash = "x",
            Role = UserRole.Resident, HouseholdId = id, InstallerId = Installateur, CreatedAt = Nu,
        });

        if (metBatterij)
        {
            db.Devices.Add(new Device
            {
                Id = Guid.NewGuid(), HouseholdId = id, Type = DeviceType.Battery,
                Brand = DeviceBrand.Sessy, Name = "Batterij", ExternalId = "x",
                EncryptedCredentials = "x", CreatedAt = Nu,
            });
        }

        await db.SaveChangesAsync();
        return id;
    }

    private async Task Dagen(AppDbContext db, Guid woning, double opwekPerDag, double terugPerDag)
    {
        var vandaag = DateOnly.FromDateTime(Nu.UtcDateTime);
        for (var i = 1; i <= 30; i++)
        {
            db.DailySummaries.Add(new DailySummary
            {
                Id = Guid.NewGuid(),
                HouseholdId = woning,
                Date = vandaag.AddDays(-i),
                PvKwh = opwekPerDag,
                ExportKwh = terugPerDag,
            });
        }
        await db.SaveChangesAsync();
    }

    private AppDbContext Db() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, TenantContext.System);

    private Aanbiedingen Aanbiedingen(AppDbContext db) => new(db, _klok);
}
