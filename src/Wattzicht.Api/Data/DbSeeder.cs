using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Data;

/// <summary>
/// Zet de basisdata klaar: installateur "Triple Energie", een installateursaccount en een demo-huishouden
/// met Demo-apparaten en een standaardtarief. Idempotent: bestaande rijen worden niet overschreven.
/// Wachtwoorden komen uit configuratie (<c>Seed:InstallerPassword</c>, <c>Seed:DemoPassword</c>) met een dev-fallback.
/// </summary>
public static class DbSeeder
{
    public static readonly Guid TripleEnergieId = new("11111111-1111-4111-8111-111111111111");
    public static readonly Guid DemoHouseholdId = new("22222222-2222-4222-8222-222222222222");

    public const string TripleEnergieSlug = "triple-energie";
    public const string DemoEmail = "demo@wattzicht.nl";
    public const string InstallerEmail = "installateur@triple-energie.nl";

    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<PasswordHasher>();
        var protector = scope.ServiceProvider.GetRequiredService<CredentialProtector>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

        var now = DateTimeOffset.UtcNow;

        var installer = await db.Installers.FirstOrDefaultAsync(i => i.Id == TripleEnergieId, ct);
        if (installer is null)
        {
            installer = new Installer
            {
                Id = TripleEnergieId,
                Name = "Triple Energie",
                Slug = TripleEnergieSlug,
                PrimaryColor = "#0F172A",
                AccentColor = "#F59E0B",
                QuoteUrl = "https://www.triple-energie.nl/offerte",
                ContactEmail = "info@triple-energie.nl",
                CreatedAt = now
            };
            db.Installers.Add(installer);
            await db.SaveChangesAsync(ct); // installer eerst, zodat FK's van users/households kloppen
            logger.LogInformation("Installateur Triple Energie aangemaakt");
        }

        if (!await db.Users.AnyAsync(u => u.Email == InstallerEmail, ct))
        {
            db.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                Email = InstallerEmail,
                PasswordHash = hasher.Hash(Setting(config, "Seed:InstallerPassword", "Installateur123!")),
                Role = UserRole.Installer,
                InstallerId = installer.Id,
                CreatedAt = now
            });
        }

        var household = await db.Households.FirstOrDefaultAsync(h => h.Id == DemoHouseholdId, ct);
        if (household is null)
        {
            household = new Household
            {
                Id = DemoHouseholdId,
                Name = "Demo-woning Enschede",
                PostalCode = "7511",
                City = "Enschede",
                InstallerId = installer.Id,
                InstallerAccessGranted = true, // demo: zichtbaar in het portal
                InstallerAccessChangedAt = now,
                CreatedAt = now
            };
            db.Households.Add(household);

            db.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                Email = DemoEmail,
                PasswordHash = hasher.Hash(Setting(config, "Seed:DemoPassword", "Demo1234!")),
                Role = UserRole.Resident,
                HouseholdId = household.Id,
                InstallerId = installer.Id,
                CreatedAt = now
            });

            db.Tariffs.Add(new Tariff
            {
                Id = Guid.NewGuid(),
                HouseholdId = household.Id,
                ValidFrom = new DateOnly(2025, 1, 1),
                ImportPricePerKwh = 0.28m,
                ExportPricePerKwh = 0.08m,
                IsDynamic = false,
                PublicChargingPricePerKwh = 0.59m
            });

            var demoCredentials = protector.Protect(new Dictionary<string, string> { ["mode"] = "demo" });

            db.Devices.AddRange(
                new Device
                {
                    Id = Guid.NewGuid(),
                    HouseholdId = household.Id,
                    Type = DeviceType.Inverter,
                    Brand = DeviceBrand.Demo,
                    Name = "Demo omvormer 4,2 kWp",
                    ExternalId = "DEMO-INV-001",
                    EncryptedCredentials = demoCredentials,
                    CreatedAt = now
                },
                new Device
                {
                    Id = Guid.NewGuid(),
                    HouseholdId = household.Id,
                    Type = DeviceType.Battery,
                    Brand = DeviceBrand.Demo,
                    Name = "Demo thuisbatterij 5 kWh",
                    ExternalId = "DEMO-BAT-001",
                    EncryptedCredentials = demoCredentials,
                    CreatedAt = now
                },
                new Device
                {
                    Id = Guid.NewGuid(),
                    HouseholdId = household.Id,
                    Type = DeviceType.Charger,
                    Brand = DeviceBrand.Demo,
                    Name = "Demo laadpaal 11 kW",
                    ExternalId = "DEMO-CHG-001",
                    EncryptedCredentials = demoCredentials,
                    CreatedAt = now
                });

            logger.LogInformation("Demo-huishouden met Demo-apparaten aangemaakt ({Email})", DemoEmail);
        }
        else if (household.InstallerAccessChangedAt is null)
        {
            // Bestaande demo-woning van vóór de toestemmingsregel: zichtbaar houden in het portal.
            household.InstallerAccessGranted = true;
            household.InstallerAccessChangedAt = now;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Configuratiewaarde met fallback; een lege string (lege env-variabele) telt als niet gezet.</summary>
    private static string Setting(IConfiguration config, string key, string fallback)
    {
        var value = config[key];
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
