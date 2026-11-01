using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Wat een installateur betaalt, en wie er voor een woning opdraait.
///
/// Twee dingen liggen hier vast. Het eerste: een gratis installateur (de afspraak met Triple
/// Energie) geeft zijn klanten twaalf maanden Plus, een betalende installateur betaalt zolang de
/// klant er is. Het tweede, en dat is de regel die het meeste kan misgaan: een klant die zelf in de
/// store betaalt telt niet mee voor de factuur van zijn installateur. Anders betaalt de installateur
/// voor iemand die al zelf betaalt, en dat is precies de dubbele betaling die we nergens willen.
/// </summary>
public sealed class InstallerBillingTests
{
    private static readonly Guid Gratis = Guid.NewGuid();
    private static readonly Guid Betalend = Guid.NewGuid();
    private static readonly DateTimeOffset Nu = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly FakeTimeProvider _klok = new(Nu);

    public InstallerBillingTests()
    {
        using var db = Db();
        db.Installers.AddRange(
            new Installer
            {
                Id = Gratis, Name = "Triple Energie", Slug = "triple-energie",
                BillingMode = InstallerBillingMode.Free, ApprovedAt = Nu, CreatedAt = Nu,
            },
            new Installer
            {
                Id = Betalend, Name = "Betalend Installatiebedrijf", Slug = "betalend",
                BillingMode = InstallerBillingMode.PerHousehold, PricePerHousehold = 1.25m,
                ApprovedAt = Nu, CreatedAt = Nu,
            });
        db.SaveChanges();
    }

    [Fact]
    public async Task Een_gratis_installateur_geeft_twaalf_maanden_Plus()
    {
        await using var db = Db();
        var woning = await Woning(db, Gratis);

        await Writer(db).StartAsync(woning, SubscriptionSource.Installer, TimeSpan.FromDays(365), installerId: Gratis);

        var rij = await db.Subscriptions.SingleAsync(s => s.HouseholdId == woning);
        Assert.Equal(Nu.AddDays(365), rij.EndsAt);
        Assert.Equal(Gratis, rij.InstallerId);
    }

    [Fact]
    public async Task Een_betalende_installateur_laat_het_doorlopen()
    {
        await using var db = Db();
        var woning = await Woning(db, Betalend);

        await Writer(db).StartAsync(woning, SubscriptionSource.Installer, null, installerId: Betalend);

        var rij = await db.Subscriptions.SingleAsync(s => s.HouseholdId == woning);
        Assert.Null(rij.EndsAt);
    }

    [Fact]
    public async Task Het_maandbedrag_is_actieve_huishoudens_maal_de_prijs()
    {
        await using var db = Db();
        var writer = Writer(db);
        for (var i = 0; i < 4; i++)
        {
            await writer.StartAsync(await Woning(db, Betalend), SubscriptionSource.Installer, null, installerId: Betalend);
        }

        Assert.Equal(4, await Actief(db, Betalend));
        Assert.Equal(5.00m, 4 * 1.25m);
    }

    [Fact]
    public async Task Een_klant_die_zelf_in_de_store_betaalt_telt_niet_mee_voor_zijn_installateur()
    {
        await using var db = Db();
        var writer = Writer(db);
        var woning = await Woning(db, Betalend);

        await writer.StartAsync(woning, SubscriptionSource.Installer, null, installerId: Betalend);
        Assert.Equal(1, await Actief(db, Betalend));

        // Hij koopt zelf. Dan betaalt hij, niet zijn installateur.
        await writer.StartAsync(woning, SubscriptionSource.Apple, TimeSpan.FromDays(365), "apple-1");

        Assert.Equal(0, await Actief(db, Betalend));
    }

    [Fact]
    public async Task Een_afgelopen_installateursperiode_telt_niet_meer_mee()
    {
        await using var db = Db();
        var woning = await Woning(db, Gratis);
        await Writer(db).StartAsync(woning, SubscriptionSource.Installer, TimeSpan.FromDays(365), installerId: Gratis);

        Assert.Equal(1, await Actief(db, Gratis));

        _klok.Advance(TimeSpan.FromDays(366));
        Assert.Equal(0, await Actief(db, Gratis));
    }

    [Fact]
    public async Task Een_klaarstaande_periode_telt_mee_zodra_hij_begonnen_is()
    {
        // Iemand had een jaar bij Apple afgerekend en zijn installateur neemt het over. Zolang Apple
        // loopt betaalt de installateur niet; daarna wel.
        await using var db = Db();
        var writer = Writer(db);
        var woning = await Woning(db, Betalend);

        await writer.StartAsync(woning, SubscriptionSource.Apple, TimeSpan.FromDays(30), "apple-1");
        await writer.StartAsync(woning, SubscriptionSource.Installer, null, installerId: Betalend);

        Assert.Equal(0, await Actief(db, Betalend));

        _klok.Advance(TimeSpan.FromDays(31));
        Assert.Equal(1, await Actief(db, Betalend));
    }

    // ---- Hulpstukken ------------------------------------------------------

    private async Task<Guid> Woning(AppDbContext db, Guid installateur)
    {
        var id = Guid.NewGuid();
        db.Households.Add(new Household { Id = id, Name = "Thuis", InstallerId = installateur, CreatedAt = Nu });
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>Dezelfde telling als het maandoverzicht in het portal.</summary>
    private async Task<int> Actief(AppDbContext db, Guid installateur)
    {
        var now = _klok.GetUtcNow();
        return await db.Subscriptions.IgnoreQueryFilters()
            .CountAsync(s => s.InstallerId == installateur
                             && s.Source == SubscriptionSource.Installer
                             && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Pending)
                             && s.StartsAt <= now
                             && (s.EndsAt == null || s.EndsAt > now));
    }

    private AppDbContext Db() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, TenantContext.System);

    private SubscriptionWriter Writer(AppDbContext db) =>
        new(db, _klok, NullLogger<SubscriptionWriter>.Instance);
}
