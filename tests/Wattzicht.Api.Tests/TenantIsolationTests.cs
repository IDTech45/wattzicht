using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Scheiding tussen klanten: wat ziet een bewoner, een installateur en een andere installateur?
/// Draait op de EF InMemory-provider; de queryfilters zitten in het model, dus gedragen zich hetzelfde als op Postgres.
/// </summary>
public sealed class TenantIsolationTests
{
    private static readonly Guid InstallerA = Guid.NewGuid();
    private static readonly Guid InstallerB = Guid.NewGuid();
    private static readonly Guid HouseWithConsent = Guid.NewGuid();
    private static readonly Guid HouseWithoutConsent = Guid.NewGuid();
    private static readonly Guid HouseOfB = Guid.NewGuid();

    private readonly string _dbName = Guid.NewGuid().ToString();

    public TenantIsolationTests()
    {
        using var db = Create(TenantContext.System);
        var now = DateTimeOffset.UtcNow;

        db.Installers.AddRange(
            new Installer { Id = InstallerA, Name = "A", Slug = "a", CreatedAt = now },
            new Installer { Id = InstallerB, Name = "B", Slug = "b", CreatedAt = now });

        AddHouse(db, HouseWithConsent, InstallerA, consent: true, now);
        AddHouse(db, HouseWithoutConsent, InstallerA, consent: false, now);
        AddHouse(db, HouseOfB, InstallerB, consent: true, now);

        db.SaveChanges();
    }

    private static void AddHouse(AppDbContext db, Guid id, Guid installer, bool consent, DateTimeOffset now)
    {
        var deviceId = Guid.NewGuid();
        db.Households.Add(new Household { Id = id, Name = id.ToString(), InstallerId = installer, InstallerAccessGranted = consent, CreatedAt = now });
        db.Devices.Add(new Device { Id = deviceId, HouseholdId = id, Type = DeviceType.Inverter, Brand = DeviceBrand.Demo, Name = "inv", ExternalId = id.ToString(), EncryptedCredentials = "x", CreatedAt = now });
        db.Readings.Add(new Reading { DeviceId = deviceId, Timestamp = now, PvPowerW = 1000 });
        db.Alerts.Add(new Alert { Id = Guid.NewGuid(), DeviceId = deviceId, Type = AlertType.NoData, Message = "m", Since = now });
        db.Tariffs.Add(new Tariff { Id = Guid.NewGuid(), HouseholdId = id, ValidFrom = new DateOnly(2026, 1, 1), ImportPricePerKwh = 0.3m, ExportPricePerKwh = 0.1m });
        db.DailySummaries.Add(new DailySummary { Id = Guid.NewGuid(), HouseholdId = id, Date = new DateOnly(2026, 10, 30), UpdatedAt = now });
        db.InstallerAccessLogs.Add(new InstallerAccessLog { HouseholdId = id, InstallerId = installer, UserId = Guid.NewGuid(), Action = "view_detail", At = now });
    }

    private AppDbContext Create(TenantContext tenant) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, tenant);

    private static TenantContext As(string role, Guid installerId, Guid? householdId = null, bool authenticated = true)
    {
        var claims = new List<Claim>
        {
            new(WattzichtClaims.UserId, Guid.NewGuid().ToString()),
            new(WattzichtClaims.Role, role),
            new(WattzichtClaims.InstallerId, installerId.ToString())
        };
        if (householdId is { } h)
        {
            claims.Add(new Claim(WattzichtClaims.HouseholdId, h.ToString()));
        }

        var identity = authenticated
            ? new ClaimsIdentity(claims, "test", WattzichtClaims.Email, WattzichtClaims.Role)
            : new ClaimsIdentity();
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        return new TenantContext(new HttpContextAccessor { HttpContext = http });
    }

    private static HashSet<Guid> HouseIds(AppDbContext db) =>
    [
        .. db.Households.Select(h => h.Id),
    ];

    private static void AssertOnlyHouses(AppDbContext db, params Guid[] expected)
    {
        Same(expected, HouseIds(db), "households");
        Same(expected, db.Devices.Select(d => d.HouseholdId).ToList(), "devices");
        Same(expected, db.Readings.Select(r => r.Device.HouseholdId).ToList(), "readings");
        Same(expected, db.Alerts.Select(a => a.Device.HouseholdId).ToList(), "alerts");
        Same(expected, db.Tariffs.Select(t => t.HouseholdId).ToList(), "tariffs");
        Same(expected, db.DailySummaries.Select(s => s.HouseholdId).ToList(), "daily_summaries");
    }

    private static void Same(IEnumerable<Guid> expected, IEnumerable<Guid> actual, string table) =>
        Assert.True(expected.ToHashSet().SetEquals(actual), $"Onverwachte huishoudens zichtbaar in {table}.");

    [Fact]
    public void Bewoner_ziet_alleen_eigen_huishouden()
    {
        using var db = Create(As(nameof(UserRole.Resident), InstallerA, HouseWithoutConsent));

        AssertOnlyHouses(db, HouseWithoutConsent);
    }

    [Fact]
    public void Bewoner_kan_ander_huishouden_niet_opvragen_op_id()
    {
        using var db = Create(As(nameof(UserRole.Resident), InstallerA, HouseWithConsent));

        Assert.Null(db.Households.FirstOrDefault(h => h.Id == HouseOfB));
        Assert.Empty(db.Readings.Where(r => r.Device.HouseholdId == HouseOfB));
    }

    [Fact]
    public void Installateur_ziet_alleen_eigen_klanten_met_toestemming()
    {
        using var db = Create(As(nameof(UserRole.Installer), InstallerA));

        AssertOnlyHouses(db, HouseWithConsent);
    }

    [Fact]
    public void Andere_installateur_ziet_niets_van_A()
    {
        using var db = Create(As(nameof(UserRole.Installer), InstallerB));

        AssertOnlyHouses(db, HouseOfB);
        Assert.DoesNotContain(db.InstallerAccessLogs, l => l.InstallerId == InstallerA);
    }

    [Fact]
    public void Toestemming_intrekken_maakt_direct_onzichtbaar()
    {
        using (var system = Create(TenantContext.System))
        {
            system.Households.Single(h => h.Id == HouseWithConsent).InstallerAccessGranted = false;
            system.SaveChanges();
        }

        using var db = Create(As(nameof(UserRole.Installer), InstallerA));
        AssertOnlyHouses(db);
    }

    [Fact]
    public void Bewoner_met_installer_claim_is_geen_installateur()
    {
        // Elk bewonerstoken draagt ook installer_id; dat mag geen toegang tot andere klanten geven.
        using var db = Create(As(nameof(UserRole.Resident), InstallerA, HouseWithoutConsent));

        Assert.DoesNotContain(HouseWithConsent, HouseIds(db));
    }

    [Fact]
    public void Ingelogd_zonder_rol_of_huishouden_ziet_niets()
    {
        using var db = Create(As("Onbekend", InstallerA));

        AssertOnlyHouses(db);
    }

    [Fact]
    public void Anoniem_request_ziet_niets()
    {
        using var db = Create(As(nameof(UserRole.Resident), InstallerA, HouseWithConsent, authenticated: false));

        AssertOnlyHouses(db);
        Assert.Empty(db.InstallerAccessLogs);
    }

    [Fact]
    public void Installateur_ziet_geen_inzagelog_na_intrekken_toestemming()
    {
        using (var system = Create(TenantContext.System))
        {
            system.Households.Single(h => h.Id == HouseWithConsent).InstallerAccessGranted = false;
            system.SaveChanges();
        }

        using var db = Create(As(nameof(UserRole.Installer), InstallerA));
        Assert.Empty(db.InstallerAccessLogs);
    }

    [Fact]
    public void Systeem_ziet_alles_voor_de_pollworker()
    {
        using var db = Create(TenantContext.System);

        AssertOnlyHouses(db, HouseWithConsent, HouseWithoutConsent, HouseOfB);
    }

    [Fact]
    public void Bewoner_ziet_inzagelog_van_eigen_huishouden()
    {
        using var db = Create(As(nameof(UserRole.Resident), InstallerA, HouseWithConsent));

        var log = Assert.Single(db.InstallerAccessLogs);
        Assert.Equal(HouseWithConsent, log.HouseholdId);
    }
}
