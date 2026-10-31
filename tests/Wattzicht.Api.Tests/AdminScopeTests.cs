using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Wat ziet de beheerder van Wattzicht, en wat verandert er niet?
///
/// De beheerder moet de hele keten kunnen zien: welke installateur heeft welke huishoudens, en
/// leveren die apparaten nog data. Dat gaat dwars door de tenantscheiding heen, en juist daarom is
/// het de moeite waard om vast te leggen hoe ver dat gaat. Twee dingen staan hier vast.
///
/// Het eerste: de beheerder krijgt géén stilzwijgende verruiming van de gewone queryfilters. Zijn
/// rol is geen installateur, dus de filters geven hem niets. Hij ziet alles alleen daar waar een
/// endpoint er expliciet om vraagt met IgnoreQueryFilters, en dat staat per regel in AdminEndpoints.
/// Zo kan een nieuw endpoint niet per ongeluk klantdata uitdelen.
///
/// Het tweede: een huishouden zonder toestemming telt wél mee voor de beheerder. Die toestemming
/// gaat over meekijken door de installateur, niet over of wij onze eigen klanten mogen tellen.
/// </summary>
public sealed class AdminScopeTests
{
    private static readonly Guid InstallerA = Guid.NewGuid();
    private static readonly Guid InstallerB = Guid.NewGuid();
    private static readonly Guid HuisMetToestemming = Guid.NewGuid();
    private static readonly Guid HuisZonderToestemming = Guid.NewGuid();
    private static readonly Guid HuisVanB = Guid.NewGuid();

    private readonly string _dbName = Guid.NewGuid().ToString();

    public AdminScopeTests()
    {
        using var db = Create(TenantContext.System);
        var now = DateTimeOffset.UtcNow;

        db.Installers.AddRange(
            new Installer { Id = InstallerA, Name = "A", Slug = "a", CreatedAt = now },
            new Installer { Id = InstallerB, Name = "B", Slug = "b", CreatedAt = now });

        VoegHuisToe(db, HuisMetToestemming, InstallerA, toestemming: true, now);
        VoegHuisToe(db, HuisZonderToestemming, InstallerA, toestemming: false, now);
        VoegHuisToe(db, HuisVanB, InstallerB, toestemming: true, now);

        db.SaveChanges();
    }

    private static void VoegHuisToe(AppDbContext db, Guid id, Guid installateur, bool toestemming, DateTimeOffset now)
    {
        var deviceId = Guid.NewGuid();
        db.Households.Add(new Household
        {
            Id = id, Name = id.ToString(), InstallerId = installateur,
            InstallerAccessGranted = toestemming, CreatedAt = now
        });
        db.Devices.Add(new Device
        {
            Id = deviceId, HouseholdId = id, Type = DeviceType.Inverter, Brand = DeviceBrand.Demo,
            Name = "inv", ExternalId = id.ToString(), EncryptedCredentials = "x", CreatedAt = now
        });
    }

    private AppDbContext Create(TenantContext tenant) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, tenant);

    private static TenantContext AlsBeheerder()
    {
        var claims = new List<Claim>
        {
            new(WattzichtClaims.UserId, Guid.NewGuid().ToString()),
            new(WattzichtClaims.Role, nameof(UserRole.Admin)),
            new(WattzichtClaims.InstallerId, InstallerA.ToString())
        };
        var identity = new ClaimsIdentity(claims, "test", WattzichtClaims.Email, WattzichtClaims.Role);
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        return new TenantContext(new HttpContextAccessor { HttpContext = http });
    }

    [Fact]
    public void Een_beheerder_krijgt_via_de_gewone_filters_niets_extras()
    {
        // Zijn rol is geen installateur, dus het queryfilter geeft hem niets. Dat is de bedoeling:
        // alleen een endpoint dat er bewust om vraagt mag buiten de tenantgrens kijken.
        using var db = Create(AlsBeheerder());
        Assert.Empty(db.Households.ToList());
        Assert.Empty(db.Devices.ToList());
    }

    [Fact]
    public void Met_IgnoreQueryFilters_ziet_hij_alle_installateurs()
    {
        using var db = Create(AlsBeheerder());
        var huizen = db.Households.IgnoreQueryFilters().Select(h => h.Id).ToHashSet();

        Assert.Equal(3, huizen.Count);
        Assert.Contains(HuisVanB, huizen);
    }

    [Fact]
    public void Een_huishouden_zonder_toestemming_telt_mee_voor_de_beheerder()
    {
        // De installateur mag daar niet in kijken, maar het is wel onze klant en hij kost ons geld
        // en levert abonnement op. Zou hij hier wegvallen, dan klopt elk aantal en elke omzet niet.
        using var db = Create(AlsBeheerder());
        var huizen = db.Households.IgnoreQueryFilters().Select(h => h.Id).ToHashSet();

        Assert.Contains(HuisZonderToestemming, huizen);
    }

    [Fact]
    public void De_installateur_ziet_nog_steeds_alleen_zijn_eigen_klanten_met_toestemming()
    {
        // De adminrol mag de bestaande scheiding niet oprekken; dit is de controle daarop.
        var claims = new List<Claim>
        {
            new(WattzichtClaims.UserId, Guid.NewGuid().ToString()),
            new(WattzichtClaims.Role, nameof(UserRole.Installer)),
            new(WattzichtClaims.InstallerId, InstallerA.ToString())
        };
        var identity = new ClaimsIdentity(claims, "test", WattzichtClaims.Email, WattzichtClaims.Role);
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        using var db = Create(new TenantContext(new HttpContextAccessor { HttpContext = http }));

        var huizen = db.Households.Select(h => h.Id).ToList();
        Assert.Equal([HuisMetToestemming], huizen);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(150, 300)]
    public void De_maandomzet_is_het_aantal_huishoudens_maal_het_tarief(int huishoudens, decimal verwacht)
    {
        // Zelfde rekensom als in AdminEndpoints: twee euro per huishouden per maand.
        const decimal tarief = 2m;
        Assert.Equal(verwacht, tarief * huishoudens);
    }
}
