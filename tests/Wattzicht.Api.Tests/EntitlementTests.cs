using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Wie betaalt er voor deze woning, en wat ziet de klant.
///
/// De regel waar alles om draait: per woning betaalt precies één partij. Twee actieve rijen zou
/// betekenen dat een installateur én de bewoner zelf voor hetzelfde huishouden betalen. In de
/// database staat daar een unieke index op; hier ligt vast dat je er in de praktijk niet tegenaan
/// loopt, en dat niemand bij een bronwissel zonder Plus komt te zitten.
///
/// Het tweede punt: zolang Plus nog niet open staat is iedereen gratis, maar worden de rijen wél
/// bijgehouden. Zo klopt de administratie al op 1 januari 2027 en hoeven proefperiodes die intussen
/// zijn gaan lopen niet met terugwerkende kracht rechtgezet te worden.
/// </summary>
public sealed class EntitlementTests
{
    private static readonly Guid Woning = Guid.NewGuid();
    private static readonly Guid AndereWoning = Guid.NewGuid();
    private static readonly Guid Installateur = Guid.NewGuid();
    private static readonly DateTimeOffset Nu = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly FakeTimeProvider _klok = new(Nu);

    public EntitlementTests()
    {
        using var db = Db();
        db.Installers.Add(new Installer
        {
            Id = Installateur, Name = "Triple Energie", Slug = "triple-energie",
            ApprovedAt = Nu, CreatedAt = Nu,
        });
        foreach (var id in new[] { Woning, AndereWoning })
        {
            db.Households.Add(new Household
            {
                Id = id, Name = "Thuis", InstallerId = Installateur, CreatedAt = Nu,
            });
        }
        db.SaveChanges();
    }

    // ---- Gratis en proefperiode ------------------------------------------

    [Fact]
    public async Task Zonder_abonnement_is_een_woning_gratis()
    {
        var antwoord = await Entitlement(plusAan: true).GetForHomeAsync(Woning);

        Assert.False(antwoord.Plus);
        Assert.Null(antwoord.Source);
    }

    [Fact]
    public async Task De_eerste_koppeling_geeft_dertig_dagen_proef()
    {
        await using var db = Db();
        var rij = await Writer(db).StartTrialIfFirstAsync(Woning);

        Assert.NotNull(rij);
        Assert.Equal(SubscriptionSource.Trial, rij!.Source);
        Assert.Equal(Nu.AddDays(30), rij.EndsAt);

        var antwoord = await Entitlement(plusAan: true, db).GetForHomeAsync(Woning);
        Assert.True(antwoord.Plus);
        Assert.Equal(SubscriptionSource.Trial, antwoord.Source);
    }

    [Fact]
    public async Task Een_tweede_keer_koppelen_geeft_geen_tweede_proefperiode()
    {
        await using var db = Db();
        var writer = Writer(db);

        Assert.NotNull(await writer.StartTrialIfFirstAsync(Woning));
        Assert.Null(await writer.StartTrialIfFirstAsync(Woning));

        Assert.Equal(1, await db.Subscriptions.CountAsync(s => s.HouseholdId == Woning));
    }

    [Fact]
    public async Task Een_afgelopen_proefperiode_maakt_de_woning_weer_gratis()
    {
        await using var db = Db();
        await Writer(db).StartTrialIfFirstAsync(Woning);

        _klok.Advance(TimeSpan.FromDays(31));
        var antwoord = await Entitlement(plusAan: true, db).GetForHomeAsync(Woning);

        Assert.False(antwoord.Plus);
    }

    // ---- Bronwissel -------------------------------------------------------

    [Fact]
    public async Task Van_proef_naar_Apple_gaat_meteen_in()
    {
        await using var db = Db();
        var writer = Writer(db);
        await writer.StartTrialIfFirstAsync(Woning);

        var wissel = await writer.StartAsync(Woning, SubscriptionSource.Apple, TimeSpan.FromDays(365), "apple-1");

        // Niets verloren: voor een proefperiode is niet betaald, dus die mag nu stoppen.
        Assert.False(wissel.StartedLater);
        Assert.Equal(Nu, wissel.Subscription.StartsAt);
        Assert.Equal(1, await ActieveRijen(db));

        var antwoord = await Entitlement(plusAan: true, db).GetForHomeAsync(Woning);
        Assert.Equal(SubscriptionSource.Apple, antwoord.Source);
    }

    [Fact]
    public async Task Van_Apple_naar_installateur_maakt_de_betaalde_periode_eerst_af()
    {
        await using var db = Db();
        var writer = Writer(db);
        await writer.StartAsync(Woning, SubscriptionSource.Apple, TimeSpan.FromDays(365), "apple-1");

        var wissel = await writer.StartAsync(
            Woning, SubscriptionSource.Installer, TimeSpan.FromDays(365), installerId: Installateur);

        // Hij heeft een jaar vooruitbetaald bij Apple; dat geld staat daar en kunnen wij niet
        // terugbetalen. Dus loopt die periode uit en begint de installateur daarna.
        Assert.True(wissel.StartedLater);
        Assert.True(wissel.AdviseStoreCancel);
        Assert.Equal(Nu.AddDays(365), wissel.Subscription.StartsAt);

        // Tot die datum geldt gewoon nog Apple.
        var nu = await Entitlement(plusAan: true, db).GetForHomeAsync(Woning);
        Assert.True(nu.Plus);
        Assert.Equal(SubscriptionSource.Apple, nu.Source);
    }

    [Fact]
    public async Task Na_de_betaalde_periode_neemt_de_installateur_het_over_zonder_gat()
    {
        await using var db = Db();
        var writer = Writer(db);
        await writer.StartAsync(Woning, SubscriptionSource.Apple, TimeSpan.FromDays(30), "apple-1");
        await writer.StartAsync(Woning, SubscriptionSource.Installer, TimeSpan.FromDays(365), installerId: Installateur);

        _klok.Advance(TimeSpan.FromDays(31));
        var antwoord = await Entitlement(plusAan: true, db).GetForHomeAsync(Woning);

        Assert.True(antwoord.Plus);
        Assert.Equal(SubscriptionSource.Installer, antwoord.Source);
    }

    [Fact]
    public async Task Een_installateur_die_stopt_laat_de_woning_gratis_achter_met_de_geschiedenis_erin()
    {
        await using var db = Db();
        var writer = Writer(db);
        await writer.StartAsync(Woning, SubscriptionSource.Installer, null, installerId: Installateur);

        await writer.StopAsync(Woning, "installateur heeft de klant losgelaten");

        var antwoord = await Entitlement(plusAan: true, db).GetForHomeAsync(Woning);
        Assert.False(antwoord.Plus);

        // De rij blijft staan: we moeten later kunnen zien dat deze woning Plus heeft gehad.
        Assert.Equal(1, await db.Subscriptions.CountAsync(s => s.HouseholdId == Woning));
        Assert.Equal(0, await ActieveRijen(db));
    }

    [Fact]
    public async Task Er_is_nooit_meer_dan_een_actieve_rij_per_woning()
    {
        await using var db = Db();
        var writer = Writer(db);

        await writer.StartTrialIfFirstAsync(Woning);
        await writer.StartAsync(Woning, SubscriptionSource.Apple, TimeSpan.FromDays(30), "apple-1");
        await writer.StartAsync(Woning, SubscriptionSource.Google, TimeSpan.FromDays(30), "google-1");
        await writer.StartAsync(Woning, SubscriptionSource.Manual, null);

        Assert.Equal(1, await ActieveRijen(db));
    }

    [Fact]
    public async Task Een_abonnement_van_de_ene_woning_raakt_de_andere_niet()
    {
        await using var db = Db();
        var writer = Writer(db);
        await writer.StartAsync(Woning, SubscriptionSource.Manual, null);

        Assert.False((await Entitlement(plusAan: true, db).GetForHomeAsync(AndereWoning)).Plus);
    }

    [Fact]
    public async Task Dezelfde_store_aankoop_twee_keer_doorgeven_verlengt_en_dupliceert_niet()
    {
        // Apple en Google sturen meldingen soms dubbel. Dat moet hetzelfde antwoord blijven geven.
        await using var db = Db();
        var writer = Writer(db);

        await writer.StartAsync(Woning, SubscriptionSource.Apple, TimeSpan.FromDays(30), "apple-origineel-1");
        await writer.StartAsync(Woning, SubscriptionSource.Apple, TimeSpan.FromDays(30), "apple-origineel-1");

        Assert.Equal(1, await db.Subscriptions.CountAsync(s => s.HouseholdId == Woning));
        Assert.Equal(1, await ActieveRijen(db));
    }

    // ---- De vlag ----------------------------------------------------------

    [Fact]
    public async Task Met_Plus_uit_is_iedereen_gratis_maar_staat_het_abonnement_er_wel()
    {
        await using var db = Db();
        await Writer(db).StartAsync(Woning, SubscriptionSource.Apple, TimeSpan.FromDays(365), "apple-1");

        var antwoord = await Entitlement(plusAan: false, db).GetForHomeAsync(Woning);

        Assert.False(antwoord.Plus);
        // Wel de bron teruggeven: het scherm kan dan tonen wat er klaarstaat, en wij zien bij een
        // vraag van een klant dat de administratie klopt.
        Assert.Equal(SubscriptionSource.Apple, antwoord.Source);
        Assert.Contains("1 januari 2027", antwoord.Reason);
    }

    [Fact]
    public async Task Met_Plus_uit_loopt_de_proefperiode_gewoon_door_in_de_administratie()
    {
        await using var db = Db();
        var rij = await Writer(db).StartTrialIfFirstAsync(Woning);

        Assert.NotNull(rij);
        Assert.Equal(SubscriptionStatus.Active, rij!.Status);
        Assert.False((await Entitlement(plusAan: false, db).GetForHomeAsync(Woning)).Plus);
    }

    [Fact]
    public async Task Een_onbekende_bron_wordt_geweigerd()
    {
        await using var db = Db();
        await Assert.ThrowsAsync<ArgumentException>(
            () => Writer(db).StartAsync(Woning, "ideal", TimeSpan.FromDays(30)));
    }

    // ---- Dagelijkse controle ---------------------------------------------

    [Fact]
    public async Task De_dagelijkse_controle_sluit_verlopen_abonnementen_af()
    {
        await using var db = Db();
        await Writer(db).StartAsync(Woning, SubscriptionSource.Apple, TimeSpan.FromDays(30), "apple-1");

        _klok.Advance(TimeSpan.FromDays(31));
        var (verlopen, _) = await Worker(db).RunAsync(default);

        Assert.Equal(1, verlopen);
        Assert.Equal(SubscriptionStatus.Expired,
            (await db.Subscriptions.SingleAsync(s => s.HouseholdId == Woning)).Status);
    }

    [Fact]
    public async Task Een_proefperiode_die_bijna_om_is_krijgt_een_melding_en_maar_een()
    {
        await using var db = Db();
        await Writer(db).StartTrialIfFirstAsync(Woning);

        // Dag 24 van de dertig: nog zes dagen te gaan, dus binnen het venster van zeven.
        _klok.Advance(TimeSpan.FromDays(24));
        var worker = Worker(db);

        Assert.Equal(1, (await worker.RunAsync(default)).Gewaarschuwd);

        _klok.Advance(TimeSpan.FromDays(1));
        Assert.Equal(0, (await worker.RunAsync(default)).Gewaarschuwd);
    }

    [Fact]
    public async Task Een_proefperiode_die_nog_lang_loopt_krijgt_niets()
    {
        await using var db = Db();
        await Writer(db).StartTrialIfFirstAsync(Woning);

        _klok.Advance(TimeSpan.FromDays(5));

        Assert.Equal(0, (await Worker(db).RunAsync(default)).Gewaarschuwd);
    }

    // ---- Hulpstukken ------------------------------------------------------

    private AppDbContext Db() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, TenantContext.System);

    private async Task<int> ActieveRijen(AppDbContext db) =>
        await db.Subscriptions.CountAsync(s => s.HouseholdId == Woning && s.Status == SubscriptionStatus.Active);

    private EntitlementService Entitlement(bool plusAan, AppDbContext? db = null) =>
        new(db ?? Db(), _klok, new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Plus:Enabled"] = plusAan ? "true" : "false" })
            .Build());

    private SubscriptionWriter Writer(AppDbContext db) =>
        new(db, _klok, NullLogger<SubscriptionWriter>.Instance);

    private SubscriptionWorker Worker(AppDbContext db) =>
        new(new EenScope(db), _klok, new ConfigurationBuilder().Build(), NullLogger<SubscriptionWorker>.Instance);

    /// <summary>Een scope-fabriek die altijd dezelfde in-memory database teruggeeft.</summary>
    private sealed class EenScope(AppDbContext db) : IServiceScopeFactory, IServiceScope, IServiceProvider
    {
        public IServiceScope CreateScope() => this;
        public IServiceProvider ServiceProvider => this;
        public void Dispose() { }

        public object? GetService(Type serviceType) =>
            serviceType == typeof(AppDbContext) ? db
            : serviceType == typeof(IPushSender) ? new TelPushSender()
            : null;
    }

    /// <summary>Verstuurt niets, telt alleen; de echte push-infra hoort niet in een test.</summary>
    private sealed class TelPushSender : IPushSender
    {
        public bool Enabled => true;

        public Task<int> SendToHouseholdAsync(
            Guid householdId, string title, string body, string? route = null, CancellationToken ct = default) =>
            Task.FromResult(1);
    }
}
