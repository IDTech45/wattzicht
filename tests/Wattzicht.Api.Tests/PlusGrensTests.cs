using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Wat een woning zonder Plus wel en niet krijgt.
///
/// Deze grens stond alleen in docs/VERDIENMODEL.md. In de code was er twee van afgedwongen: de
/// aanbiedingen-tegel verdween bij Plus, en de besparing rekende met het eigen tarief. Al het
/// andere uit die tabel kreeg iedereen gratis, dus op de dag dat Plus opengaat viel er niets te
/// verkopen.
///
/// Het tweede punt is net zo belangrijk: zolang Plus nog niet open staat is <b>alles</b> gratis.
/// Zonder dat onderscheid zouden de huidige gebruikers hun laadplan en hun export vandaag al kwijt
/// zijn, maanden voordat ze iets kunnen kopen.
/// </summary>
public sealed class PlusGrensTests
{
    private static readonly DateTimeOffset Nu = new(2026, 12, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Vandaag = new(2026, 12, 15);
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly FakeTimeProvider _klok = new(Nu);

    // ---- De historiegrens -------------------------------------------------

    [Fact]
    public void Zonder_plus_zie_je_dertig_dagen_terug()
    {
        var gevraagd = Vandaag.AddDays(-365);

        var (vanaf, ingekort) = PlusGrens.HistorieVanaf(gevraagd, Vandaag, heeftPlus: false);

        Assert.True(ingekort);
        Assert.Equal(Vandaag.AddDays(-29), vanaf);
        Assert.Equal(30, Vandaag.DayNumber - vanaf.DayNumber + 1);
    }

    [Fact]
    public void Met_plus_krijg_je_waar_je_om_vraagt()
    {
        var gevraagd = Vandaag.AddDays(-365);

        var (vanaf, ingekort) = PlusGrens.HistorieVanaf(gevraagd, Vandaag, heeftPlus: true);

        Assert.False(ingekort);
        Assert.Equal(gevraagd, vanaf);
    }

    [Fact]
    public void Een_korte_vraag_wordt_nooit_opgerekt()
    {
        var gevraagd = Vandaag.AddDays(-7);

        var (vanaf, ingekort) = PlusGrens.HistorieVanaf(gevraagd, Vandaag, heeftPlus: false);

        Assert.False(ingekort);
        Assert.Equal(gevraagd, vanaf);
    }

    [Fact]
    public void De_grens_zelf_wordt_niet_ingekort()
    {
        var precies = Vandaag.AddDays(-29);

        var (vanaf, ingekort) = PlusGrens.HistorieVanaf(precies, Vandaag, heeftPlus: false);

        Assert.False(ingekort);
        Assert.Equal(precies, vanaf);
    }

    // ---- Zolang Plus niet open staat is alles gratis ----------------------

    [Fact]
    public async Task Staat_plus_nog_niet_open_dan_mag_iedereen_alles()
    {
        var woning = await WoningAsync();

        var mag = await Entitlement(plusAan: false).MagPlusFunctieAsync(woning);

        Assert.True(mag, "tot 1 januari 2027 is alles gratis; niemand mag nu iets kwijtraken");
    }

    [Fact]
    public async Task Staat_plus_open_dan_mag_een_gratis_woning_het_niet()
    {
        var woning = await WoningAsync();

        var mag = await Entitlement(plusAan: true).MagPlusFunctieAsync(woning);

        Assert.False(mag);
    }

    [Fact]
    public async Task Staat_plus_open_dan_mag_een_woning_met_abonnement_het_wel()
    {
        var woning = await WoningAsync();
        await using (var db = Db())
        {
            db.Subscriptions.Add(new Subscription
            {
                Id = Guid.NewGuid(), HouseholdId = woning, Source = SubscriptionSource.Apple,
                Status = SubscriptionStatus.Active, StartsAt = Nu.AddDays(-1), EndsAt = Nu.AddMonths(1),
                CreatedAt = Nu,
            });
            await db.SaveChangesAsync();
        }

        var mag = await Entitlement(plusAan: true).MagPlusFunctieAsync(woning);

        Assert.True(mag);
    }

    // ---- De proefperiode van voor de opening telt niet --------------------

    [Fact]
    public async Task Een_proef_die_afliep_voor_plus_openging_blokkeert_geen_nieuwe()
    {
        var woning = await WoningAsync();
        await VerlopenProefAsync(woning, eindigdeOp: new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero));

        await using var db = Db();
        var nieuw = await Writer(db).StartTrialIfFirstAsync(woning);

        Assert.NotNull(nieuw);
    }

    [Fact]
    public async Task Een_proef_van_na_de_opening_blokkeert_wel()
    {
        var woning = await WoningAsync();
        await VerlopenProefAsync(woning, eindigdeOp: EntitlementService.PlusOpenVanaf.AddDays(14));

        await using var db = Db();
        var nieuw = await Writer(db).StartTrialIfFirstAsync(woning);

        Assert.Null(nieuw);
    }

    [Fact]
    public async Task Een_betaald_abonnement_van_voor_de_opening_blokkeert_ook()
    {
        var woning = await WoningAsync();
        await using (var db = Db())
        {
            db.Subscriptions.Add(new Subscription
            {
                Id = Guid.NewGuid(), HouseholdId = woning, Source = SubscriptionSource.Apple,
                Status = SubscriptionStatus.Expired,
                StartsAt = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
                EndsAt = new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero),
                CreatedAt = Nu,
            });
            await db.SaveChangesAsync();
        }

        await using var db2 = Db();
        var nieuw = await Writer(db2).StartTrialIfFirstAsync(woning);

        Assert.Null(nieuw);
    }

    private async Task VerlopenProefAsync(Guid woning, DateTimeOffset eindigdeOp)
    {
        await using var db = Db();
        db.Subscriptions.Add(new Subscription
        {
            Id = Guid.NewGuid(), HouseholdId = woning, Source = SubscriptionSource.Trial,
            Status = SubscriptionStatus.Expired,
            StartsAt = eindigdeOp.AddDays(-7), EndsAt = eindigdeOp, CreatedAt = Nu,
        });
        await db.SaveChangesAsync();
    }

    private SubscriptionWriter Writer(AppDbContext db) =>
        new(db, _klok, Microsoft.Extensions.Logging.Abstractions.NullLogger<SubscriptionWriter>.Instance);

    // ---- Hulpjes ----------------------------------------------------------

    private async Task<Guid> WoningAsync()
    {
        var id = Guid.NewGuid();
        await using var db = Db();
        db.Households.Add(new Household { Id = id, Name = "Thuis", CreatedAt = Nu });
        await db.SaveChangesAsync();
        return id;
    }

    private AppDbContext Db() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName).Options);

    private EntitlementService Entitlement(bool plusAan) =>
        new(Db(), _klok, new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Plus:Enabled"] = plusAan ? "true" : "false" })
            .Build());
}
