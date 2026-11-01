using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Wanneer we iemand over LaadCash vertellen, en wanneer juist niet.
///
/// Dit is ons eigen product en het mag nadrukkelijk, maar precies daarom staan de grenzen hier
/// vast. Vier dingen die anders misgaan:
///
/// Een bedrag noemen zonder data. Zonder uitgelezen laadsessie is elk bedrag gegokt, en een getal
/// dat later niet blijkt te kloppen kost je het vertrouwen voor de hele app.
///
/// Iemand lastigvallen voor een tientje. Onder de vijftig euro per jaar is de moeite van een
/// aanmelding het niet waard.
///
/// Blijven pushen. Hoogstens vier keer, met weken ertussen, en nooit 's nachts.
///
/// Doorgaan na een nee. Wie "niet meer tonen" kiest hoort er nooit meer iets over, ook niet over
/// een jaar; anders zet hij de meldingen helemaal uit en ben je hem ook voor storingen kwijt.
/// </summary>
public sealed class LaadcashPromotieTests
{
    private static readonly Guid Installateur = Guid.NewGuid();
    private static readonly DateTimeOffset Nu = new(2026, 11, 2, 11, 0, 0, TimeSpan.Zero);

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly FakeTimeProvider _klok = new(Nu);

    public LaadcashPromotieTests()
    {
        using var db = Db();
        db.Installers.Add(new Installer
        {
            Id = Installateur, Name = "Triple Energie", Slug = "triple-energie", CreatedAt = Nu,
        });
        db.SaveChanges();
    }

    // ---- Wanneer tonen we iets -------------------------------------------

    [Fact]
    public async Task Zonder_laadpaal_promoten_we_niets()
    {
        await using var db = Db();
        var woning = await Woning(db, metLaadpaal: false);

        Assert.False((await Promotie(db).VoorWoningAsync(woning)).Show);
    }

    [Fact]
    public async Task Een_laadpaal_zonder_uitgelezen_sessie_krijgt_geen_bedrag()
    {
        // Het apparaat hangt er, maar er is nog nooit data binnengekomen. Een bedrag zou gokken zijn.
        await using var db = Db();
        var woning = await Woning(db, metLaadpaal: true);

        Assert.False((await Promotie(db).VoorWoningAsync(woning)).Show);
    }

    [Fact]
    public async Task Met_laadsessies_komt_er_een_bedrag_en_een_popup()
    {
        await using var db = Db();
        var woning = await Woning(db, metLaadpaal: true);
        await Laadsessies(db, woning, dagen: 90, kwhPerDag: 10);

        var promo = await Promotie(db).VoorWoningAsync(woning);

        Assert.True(promo.Show);
        Assert.True(promo.ShowPopup);
        // 10 kWh per dag maal 365 maal 0,118 is ongeveer 430 euro, afgerond op tientallen.
        Assert.Equal(430m, promo.EstimatedYearlyEur);
        Assert.Contains("430", promo.PopupBody);
        Assert.Contains("src=wattzicht", promo.DeepLink);
        Assert.Contains(woning.ToString(), promo.DeepLink);
    }

    [Fact]
    public async Task Onder_vijftig_euro_per_jaar_zeggen_we_niets()
    {
        // Een halve kilowattuur per dag is ongeveer 21 euro per jaar. Daar val je niemand voor lastig.
        await using var db = Db();
        var woning = await Woning(db, metLaadpaal: true);
        await Laadsessies(db, woning, dagen: 90, kwhPerDag: 0.5);

        Assert.False((await Promotie(db).VoorWoningAsync(woning)).Show);
    }

    [Fact]
    public async Task De_popup_komt_maar_een_keer()
    {
        await using var db = Db();
        var woning = await Woning(db, metLaadpaal: true);
        await Laadsessies(db, woning, dagen: 90, kwhPerDag: 10);
        var promotie = Promotie(db);

        Assert.True((await promotie.VoorWoningAsync(woning)).ShowPopup);

        await promotie.NoteerAsync(woning, PromoChannel.Popup, PromoAction.Shown);

        var daarna = await promotie.VoorWoningAsync(woning);
        Assert.False(daarna.ShowPopup);
        // De tegel blijft wel staan; die is niet opdringerig.
        Assert.True(daarna.Show);
    }

    [Fact]
    public async Task Na_niet_meer_tonen_hoort_hij_er_nooit_meer_iets_over()
    {
        await using var db = Db();
        var woning = await Woning(db, metLaadpaal: true);
        await Laadsessies(db, woning, dagen: 90, kwhPerDag: 10);
        var promotie = Promotie(db);

        await promotie.NoteerAsync(woning, PromoChannel.Popup, PromoAction.Optout);

        Assert.False((await promotie.VoorWoningAsync(woning)).Show);

        _klok.Advance(TimeSpan.FromDays(400));
        Assert.False((await promotie.VoorWoningAsync(woning)).Show);
    }

    [Fact]
    public async Task Wie_al_bij_LaadCash_zit_krijgt_geen_promotie_maar_de_opbrengst()
    {
        await using var db = Db();
        var woning = await Woning(db, metLaadpaal: true);
        await Laadsessies(db, woning, dagen: 90, kwhPerDag: 10);

        var huis = await db.Households.SingleAsync(h => h.Id == woning);
        huis.LaadcashLinkedAt = Nu;
        await db.SaveChangesAsync();

        var promo = await Promotie(db).VoorWoningAsync(woning);

        Assert.True(promo.Linked);
        Assert.False(promo.ShowPopup);
        Assert.Null(promo.DeepLink);
    }

    // ---- Pushmeldingen ----------------------------------------------------

    [Fact]
    public async Task De_eerste_melding_komt_pas_een_week_na_het_koppelen()
    {
        await using var db = Db();
        var woning = await Woning(db, metLaadpaal: true);
        await Laadsessies(db, woning, dagen: 90, kwhPerDag: 10);
        var promotie = Promotie(db);

        Assert.False(await promotie.MagPushAsync(woning, Nu));
        Assert.False(await promotie.MagPushAsync(woning, Nu.AddDays(6)));
        Assert.True(await promotie.MagPushAsync(woning, Nu.AddDays(7)));
    }

    [Fact]
    public async Task Daarna_elke_maand_en_hoogstens_vier_keer()
    {
        await using var db = Db();
        var woning = await Woning(db, metLaadpaal: true);
        await Laadsessies(db, woning, dagen: 90, kwhPerDag: 10);
        var promotie = Promotie(db);

        // De klok echt vooruitzetten, want NoteerAsync stempelt met de klok. Een los moment meegeven
        // aan MagPushAsync terwijl de klok stilstaat, toetst iets anders dan wat er in productie gebeurt.
        _klok.Advance(TimeSpan.FromDays(7));

        for (var nummer = 1; nummer <= 4; nummer++)
        {
            Assert.True(await promotie.MagPushAsync(woning, _klok.GetUtcNow()), $"melding {nummer} hoort te mogen");
            await promotie.NoteerAsync(woning, PromoChannel.Push, PromoAction.Shown);

            // Een dag later mag er nog niets; pas na een maand weer.
            _klok.Advance(TimeSpan.FromDays(1));
            Assert.False(await promotie.MagPushAsync(woning, _klok.GetUtcNow()));

            _klok.Advance(TimeSpan.FromDays(29));
        }

        // De vijfde komt niet meer, hoe lang hij ook blijft zitten.
        Assert.False(await promotie.MagPushAsync(woning, _klok.GetUtcNow()));
        _klok.Advance(TimeSpan.FromDays(365));
        Assert.False(await promotie.MagPushAsync(woning, _klok.GetUtcNow()));
    }

    [Theory]
    [InlineData(3, false)]   // nacht
    [InlineData(8, false)]   // te vroeg
    [InlineData(9, true)]
    [InlineData(19, true)]
    [InlineData(20, false)]  // te laat
    [InlineData(23, false)]
    public async Task Buiten_negen_tot_acht_pushen_we_niet(int uurNederlandseTijd, bool verwacht)
    {
        await using var db = Db();
        var woning = await Woning(db, metLaadpaal: true);
        await Laadsessies(db, woning, dagen: 90, kwhPerDag: 10);

        // November: Nederlandse tijd is UTC+1.
        var moment = new DateTimeOffset(2026, 11, 20, uurNederlandseTijd - 1, 0, 0, TimeSpan.Zero);

        Assert.Equal(verwacht, await Promotie(db).MagPushAsync(woning, moment));
    }

    [Fact]
    public async Task Iemand_die_zich_aanmeldt_krijgt_geen_meldingen_meer()
    {
        await using var db = Db();
        var woning = await Woning(db, metLaadpaal: true);
        await Laadsessies(db, woning, dagen: 90, kwhPerDag: 10);
        var promotie = Promotie(db);

        Assert.True(await promotie.MagPushAsync(woning, Nu.AddDays(7)));

        var huis = await db.Households.SingleAsync(h => h.Id == woning);
        huis.LaadcashLinkedAt = Nu.AddDays(8);
        await db.SaveChangesAsync();

        Assert.False(await promotie.MagPushAsync(woning, Nu.AddDays(40)));
    }

    // ---- Mail -------------------------------------------------------------

    [Fact]
    public async Task De_uitlegmail_komt_na_drie_dagen_en_een_keer()
    {
        await using var db = Db();
        var woning = await Woning(db, metLaadpaal: true);
        await Laadsessies(db, woning, dagen: 90, kwhPerDag: 10);
        var promotie = Promotie(db);

        Assert.False(await promotie.MagMailAsync(woning, Nu.AddDays(2)));
        Assert.True(await promotie.MagMailAsync(woning, Nu.AddDays(3)));

        await promotie.NoteerAsync(woning, PromoChannel.Email, PromoAction.Shown);
        Assert.False(await promotie.MagMailAsync(woning, Nu.AddDays(30)));
    }

    // ---- Rekenen ----------------------------------------------------------

    [Fact]
    public async Task Minder_dan_een_jaar_data_wordt_doorgerekend_naar_een_jaar()
    {
        await using var db = Db();
        var woning = await Woning(db, metLaadpaal: true);
        await Laadsessies(db, woning, dagen: 60, kwhPerDag: 10);

        var (perJaar, heeft) = await Promotie(db).GeladenPerJaarAsync(woning);

        Assert.True(heeft);
        // Zestig dagen van tien kilowattuur, doorgerekend: ongeveer 3650 per jaar.
        Assert.InRange(perJaar, 3500, 3800);
    }

    [Fact]
    public async Task Een_week_data_wordt_niet_naar_een_heel_jaar_opgeblazen()
    {
        // Uit één week valt geen jaar te voorspellen; daarom rekenen we met minstens dertig dagen.
        // Zonder die ondergrens zou één vakantieweek met veel laden een bedrag van duizenden euro's
        // opleveren, en dat is precies het getal waar iemand je later op afrekent.
        await using var db = Db();
        var woning = await Woning(db, metLaadpaal: true);
        await Laadsessies(db, woning, dagen: 7, kwhPerDag: 30);

        var (perJaar, _) = await Promotie(db).GeladenPerJaarAsync(woning);

        // 7 × 30 = 210 kWh, gedeeld door 30 dagen in plaats van 7, maal 365.
        Assert.InRange(perJaar, 2400, 2700);
    }

    [Theory]
    [InlineData(12.0, 10)]
    [InlineData(47.0, 50)]
    [InlineData(234.0, 230)]
    [InlineData(235.0, 240)]
    public void Bedragen_worden_op_tientallen_afgerond(double bedrag, decimal verwacht)
    {
        // "€ 237 per jaar" doet alsof we het preciezer weten dan we doen.
        Assert.Equal(verwacht, LaadcashPromotie.Afgerond((decimal)bedrag));
    }

    // ---- Hulpstukken ------------------------------------------------------

    private async Task<Guid> Woning(AppDbContext db, bool metLaadpaal)
    {
        var id = Guid.NewGuid();
        db.Households.Add(new Household { Id = id, Name = "Thuis", InstallerId = Installateur, CreatedAt = Nu });
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(), Email = $"{id:N}@voorbeeld.nl", PasswordHash = "x",
            Role = UserRole.Resident, HouseholdId = id, InstallerId = Installateur, CreatedAt = Nu,
        });

        if (metLaadpaal)
        {
            db.Devices.Add(new Device
            {
                Id = Guid.NewGuid(), HouseholdId = id, Type = DeviceType.Charger,
                Brand = DeviceBrand.Solax, Name = "Laadpaal", ExternalId = "x",
                EncryptedCredentials = "x", CreatedAt = Nu,
            });
        }

        await db.SaveChangesAsync();
        return id;
    }

    private async Task Laadsessies(AppDbContext db, Guid woning, int dagen, double kwhPerDag)
    {
        var vandaag = DateOnly.FromDateTime(Nu.UtcDateTime);
        for (var i = 1; i <= dagen; i++)
        {
            db.DailySummaries.Add(new DailySummary
            {
                Id = Guid.NewGuid(),
                HouseholdId = woning,
                Date = vandaag.AddDays(-i),
                ChargedKwh = kwhPerDag,
            });
        }
        await db.SaveChangesAsync();
    }

    private AppDbContext Db() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, TenantContext.System);

    private LaadcashPromotie Promotie(AppDbContext db) =>
        new(db, _klok, new ConfigurationBuilder().Build());
}
