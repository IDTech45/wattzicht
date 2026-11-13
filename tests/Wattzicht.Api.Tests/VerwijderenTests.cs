using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Je account weghalen moet echt weghalen.
///
/// Google eist dit van elke app waarin je een account maakt, maar dat is niet de reden dat deze test
/// bestaat. De reden is dat je het achteraf niet kunt controleren: als er iets blijft staan merk je
/// dat nooit, want je kijkt niet meer. Dus wordt per tabel vastgelegd dat hij leeg is, én dat de
/// buren eraf blijven.
/// </summary>
public sealed class VerwijderenTests
{
    private static readonly Guid Installateur = Guid.NewGuid();
    private static readonly Guid Mijn = Guid.NewGuid();
    private static readonly Guid Buurman = Guid.NewGuid();

    private readonly string _dbNaam = Guid.NewGuid().ToString();

    public VerwijderenTests()
    {
        using var db = Db();
        var nu = DateTimeOffset.UtcNow;

        db.Installers.Add(new Installer { Id = Installateur, Name = "Triple", Slug = "triple", CreatedAt = nu });
        Vul(db, Mijn, nu);
        Vul(db, Buurman, nu);
        db.SaveChanges();
    }

    /// <summary>Een woning met in elke tabel die eraan hangt minstens één rij.</summary>
    private static void Vul(AppDbContext db, Guid home, DateTimeOffset nu)
    {
        var apparaat = Guid.NewGuid();

        db.Households.Add(new Household
        {
            Id = home, Name = "Thuis", InstallerId = Installateur, Plan = "free", CreatedAt = nu
        });
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(), Email = $"{home}@voorbeeld.nl", PasswordHash = "x",
            Role = UserRole.Resident, HouseholdId = home, InstallerId = Installateur, CreatedAt = nu
        });
        db.Devices.Add(new Device
        {
            Id = apparaat, HouseholdId = home, Type = DeviceType.Inverter, Brand = DeviceBrand.Demo,
            Name = "omvormer", ExternalId = home.ToString(), EncryptedCredentials = "geheim", CreatedAt = nu
        });
        db.Readings.Add(new Reading { DeviceId = apparaat, Timestamp = nu, PvPowerW = 1200 });
        db.Alerts.Add(new Alert { Id = Guid.NewGuid(), DeviceId = apparaat, Type = AlertType.NoData, Message = "stil", Since = nu });
        db.DailySummaries.Add(new DailySummary { Id = Guid.NewGuid(), HouseholdId = home, Date = new DateOnly(2026, 11, 1), UpdatedAt = nu });
        db.SupplierUsages.Add(new SupplierUsage
        {
            Id = Guid.NewGuid(), HouseholdId = home, Month = new DateOnly(2026, 10, 1),
            ImportKwh = 230, ExportKwh = 120, CreatedAt = nu, UpdatedAt = nu
        });
        db.FailedImports.Add(new FailedImport
        {
            Id = Guid.NewGuid(), HouseholdId = home, FileName = "nota.pdf", Text = "onleesbaar",
            CreatedAt = nu, ExpiresAt = nu.AddDays(30)
        });
        db.Tariffs.Add(new Tariff { Id = Guid.NewGuid(), HouseholdId = home, ValidFrom = new DateOnly(2026, 1, 1), ImportPricePerKwh = 0.30m, ExportPricePerKwh = 0.08m });
        db.PushDevices.Add(new PushDevice { Id = Guid.NewGuid(), HouseholdId = home, Token = "t", Platform = "android", CreatedAt = nu, LastSeenAt = nu });
        db.Subscriptions.Add(new Subscription
        {
            Id = Guid.NewGuid(), HouseholdId = home, Source = SubscriptionSource.Trial,
            Status = SubscriptionStatus.Active, StartsAt = nu, CreatedAt = nu
        });
        db.PromoEvents.Add(new PromoEvent
        {
            Id = Guid.NewGuid(), HouseholdId = home, Campaign = PromoCampaign.Laadcash,
            Channel = PromoChannel.Tile, Action = PromoAction.Shown, SentAt = nu
        });
        db.InstallerAccessLogs.Add(new InstallerAccessLog
        {
            HouseholdId = home, InstallerId = Installateur, UserId = Guid.NewGuid(), Action = "view_detail", At = nu
        });
    }

    private AppDbContext Db() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbNaam).Options, TenantContext.System);

    [Fact]
    public async Task Alles_van_die_woning_is_daarna_weg()
    {
        await using (var db = Db())
        {
            await HuishoudenVerwijderen.UitvoerenAsync(db, Mijn);
        }

        await using var na = Db();
        Assert.Empty(na.Households.IgnoreQueryFilters().Where(x => x.Id == Mijn));
        Assert.Empty(na.Users.IgnoreQueryFilters().Where(x => x.HouseholdId == Mijn));
        Assert.Empty(na.Devices.IgnoreQueryFilters().Where(x => x.HouseholdId == Mijn));
        Assert.Empty(na.Readings.IgnoreQueryFilters().Where(x => x.Device!.HouseholdId == Mijn));
        Assert.Empty(na.Alerts.IgnoreQueryFilters().Where(x => x.Device!.HouseholdId == Mijn));
        Assert.Empty(na.DailySummaries.IgnoreQueryFilters().Where(x => x.HouseholdId == Mijn));
        Assert.Empty(na.SupplierUsages.IgnoreQueryFilters().Where(x => x.HouseholdId == Mijn));
        Assert.Empty(na.FailedImports.IgnoreQueryFilters().Where(x => x.HouseholdId == Mijn));
        Assert.Empty(na.Tariffs.IgnoreQueryFilters().Where(x => x.HouseholdId == Mijn));
        Assert.Empty(na.PushDevices.IgnoreQueryFilters().Where(x => x.HouseholdId == Mijn));
        Assert.Empty(na.Subscriptions.IgnoreQueryFilters().Where(x => x.HouseholdId == Mijn));
        Assert.Empty(na.PromoEvents.IgnoreQueryFilters().Where(x => x.HouseholdId == Mijn));
        Assert.Empty(na.InstallerAccessLogs.IgnoreQueryFilters().Where(x => x.HouseholdId == Mijn));
    }

    [Fact]
    public async Task De_buren_blijven_volledig_staan()
    {
        await using (var db = Db())
        {
            await HuishoudenVerwijderen.UitvoerenAsync(db, Mijn);
        }

        await using var na = Db();
        Assert.Single(na.Households.IgnoreQueryFilters().Where(x => x.Id == Buurman));
        Assert.Single(na.Users.IgnoreQueryFilters().Where(x => x.HouseholdId == Buurman));
        Assert.Single(na.Devices.IgnoreQueryFilters().Where(x => x.HouseholdId == Buurman));
        Assert.Single(na.Readings.IgnoreQueryFilters().Where(x => x.Device!.HouseholdId == Buurman));
        Assert.Single(na.DailySummaries.IgnoreQueryFilters().Where(x => x.HouseholdId == Buurman));
        Assert.Single(na.SupplierUsages.IgnoreQueryFilters().Where(x => x.HouseholdId == Buurman));
        Assert.Single(na.FailedImports.IgnoreQueryFilters().Where(x => x.HouseholdId == Buurman));
        Assert.Single(na.Subscriptions.IgnoreQueryFilters().Where(x => x.HouseholdId == Buurman));
    }

    [Fact]
    public async Task De_installateur_blijft_bestaan()
    {
        // Die is van iemand anders en heeft andere klanten; zijn bedrijf hoort niet te verdwijnen
        // omdat één bewoner ermee stopt.
        await using (var db = Db())
        {
            await HuishoudenVerwijderen.UitvoerenAsync(db, Mijn);
        }

        await using var na = Db();
        Assert.Single(na.Installers.Where(x => x.Id == Installateur));
    }

    [Fact]
    public async Task Twee_keer_verwijderen_mag_gewoon()
    {
        // Een verzoek dat opnieuw binnenkomt, bijvoorbeeld omdat de verbinding wegviel, hoort niet
        // te klappen: het resultaat is precies wat de aanvrager wilde.
        await using (var db = Db())
        {
            await HuishoudenVerwijderen.UitvoerenAsync(db, Mijn);
        }

        await using (var db = Db())
        {
            await HuishoudenVerwijderen.UitvoerenAsync(db, Mijn);
        }

        await using var na = Db();
        Assert.Empty(na.Households.IgnoreQueryFilters().Where(x => x.Id == Mijn));
    }
}
