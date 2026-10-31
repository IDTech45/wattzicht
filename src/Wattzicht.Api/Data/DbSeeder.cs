using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Data;

/// <summary>
/// Zet de basisdata klaar: installateur "Triple Energie" en een installateursaccount. Idempotent:
/// bestaande rijen worden niet overschreven.
///
/// Wachtwoorden komen uit configuratie (<c>Seed:AdminPassword</c>, <c>Seed:InstallerPassword</c>,
/// <c>Seed:DemoPassword</c>). Buiten Development is er geen terugvaloptie: staat een wachtwoord niet
/// ingesteld, dan maken we dat account niet aan. Anders zou er op de productieserver een beheerder
/// klaarstaan met een wachtwoord dat in deze broncode te lezen is.
///
/// Het demo-huishouden met Demo-apparaten (een gesimuleerde dagcurve) hoort bij ontwikkelen en
/// demonstreren, niet bij de echte klanten van een installateur. Het komt er daarom alleen bij als
/// <c>Seed:Demo</c> aan staat; buiten Development staat dat uit.
/// </summary>
public static class DbSeeder
{
    public static readonly Guid TripleEnergieId = new("11111111-1111-4111-8111-111111111111");
    public static readonly Guid DemoHouseholdId = new("22222222-2222-4222-8222-222222222222");

    public const string TripleEnergieSlug = "triple-energie";
    public const string DemoEmail = "demo@wattzicht.nl";
    public const string InstallerEmail = "installateur@triple-energie.nl";

    /// <summary>Het beheeraccount van Wattzicht zelf; wachtwoord uit <c>Seed:AdminPassword</c>.</summary>
    public const string AdminEmail = "admin@wattzicht.nl";

    /// <summary>Logo van Triple Energie (van hun eigen site) tot ze er zelf een uploaden in het portal.</summary>
    public const string TripleEnergieLogoUrl = "https://www.triple-energie.nl/wp-content/uploads/2025/04/Triple-energie-logo-768x351.png";

    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<PasswordHasher>();
        var protector = scope.ServiceProvider.GetRequiredService<CredentialProtector>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

        var now = DateTimeOffset.UtcNow;

        // Alleen tijdens ontwikkelen mogen de vaste wachtwoorden uit deze broncode gelden.
        var devFallback = env.IsDevelopment();
        var metDemo = config.GetValue("Seed:Demo", devFallback);

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
                LogoUrl = TripleEnergieLogoUrl,
                ContactEmail = "info@triple-energie.nl",
                CreatedAt = now
            };
            db.Installers.Add(installer);
            await db.SaveChangesAsync(ct); // installer eerst, zodat FK's van users/households kloppen
            logger.LogInformation("Installateur Triple Energie aangemaakt");
        }
        else if (string.IsNullOrEmpty(installer.LogoUrl))
        {
            installer.LogoUrl = TripleEnergieLogoUrl;
            await db.SaveChangesAsync(ct);
        }

        var installerPassword = Wachtwoord(config, "Seed:InstallerPassword", "Installateur123!", devFallback);
        await ZetWachtwoordOpnieuwAsync(db, InstallerEmail, Ingesteld(config, "Seed:InstallerPassword"), hasher, logger, ct);
        if (!await db.Users.AnyAsync(u => u.Email == InstallerEmail, ct))
        {
            if (installerPassword is null)
            {
                logger.LogWarning("Installateursaccount {Email} niet aangemaakt: Seed:InstallerPassword staat niet ingesteld.", InstallerEmail);
            }
            else
            {
                db.Users.Add(new User
                {
                    Id = Guid.NewGuid(),
                    Email = InstallerEmail,
                    PasswordHash = hasher.Hash(installerPassword),
                    Role = UserRole.Installer,
                    InstallerId = installer.Id,
                    CreatedAt = now
                });
            }
        }

        // Beheerder van Wattzicht zelf. Hij hangt administratief aan Triple Energie omdat elke
        // gebruiker een installateur nodig heeft, maar zijn rol bepaalt wat hij ziet: alles, dwars
        // door de tenantgrens heen. Zie AdminEndpoints.
        var adminPassword = Wachtwoord(config, "Seed:AdminPassword", "Admin123!", devFallback);
        await ZetWachtwoordOpnieuwAsync(db, AdminEmail, Ingesteld(config, "Seed:AdminPassword"), hasher, logger, ct);
        if (!await db.Users.AnyAsync(u => u.Email == AdminEmail, ct))
        {
            if (adminPassword is null)
            {
                logger.LogWarning("Beheeraccount {Email} niet aangemaakt: Seed:AdminPassword staat niet ingesteld. "
                                  + "Zet die variabele en herstart; een beheerder met een wachtwoord uit de broncode zetten we niet klaar.", AdminEmail);
            }
            else
            {
                db.Users.Add(new User
                {
                    Id = Guid.NewGuid(),
                    Email = AdminEmail,
                    PasswordHash = hasher.Hash(adminPassword),
                    Role = UserRole.Admin,
                    InstallerId = installer.Id,
                    CreatedAt = now
                });
                logger.LogInformation("Beheeraccount {Email} aangemaakt.", AdminEmail);
            }
        }

        if (!metDemo)
        {
            // Geen demo-woning op een echte omgeving: een installateur moet in zijn portal alleen zijn
            // eigen klanten zien, niet een huishouden met een verzonnen dagcurve ertussen.
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Demo-huishouden overgeslagen (Seed:Demo staat uit).");
            return;
        }

        var demoPassword = Wachtwoord(config, "Seed:DemoPassword", "Demo1234!", devFallback);
        if (demoPassword is null)
        {
            await db.SaveChangesAsync(ct);
            logger.LogWarning("Demo-huishouden overgeslagen: Seed:Demo staat aan maar Seed:DemoPassword niet ingesteld.");
            return;
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
                PasswordHash = hasher.Hash(demoPassword),
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

    /// <summary>
    /// Zet het wachtwoord van een al bestaand seed-account opnieuw als er er een is ingesteld. Nodig
    /// omdat de vorige versie van deze seeder op elke omgeving een beheerder aanmaakte met een
    /// wachtwoord uit de broncode: dat account staat dan al in de database en gaat niet weg door de
    /// regel hierboven. Zet je de variabele, dan is dat bekende wachtwoord na een herstart weg.
    /// Alleen bij een expliciet ingestelde waarde, zodat de terugvaloptie in Development niets overschrijft.
    /// </summary>
    private static async Task ZetWachtwoordOpnieuwAsync(AppDbContext db, string email, string? ingesteld,
        PasswordHasher hasher, ILogger logger, CancellationToken ct)
    {
        if (ingesteld is null) return;
        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null) return;

        // De hash heeft een eigen salt, dus twee hashes van hetzelfde wachtwoord zijn nooit gelijk:
        // vergelijken gaat met Verify, anders zou dit bij elke herstart opnieuw schrijven.
        if (hasher.Verify(ingesteld, user.PasswordHash)) return;

        user.PasswordHash = hasher.Hash(ingesteld);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Wachtwoord van {Email} opnieuw gezet uit de configuratie.", email);
    }

    /// <summary>De expliciet ingestelde waarde, of <c>null</c> bij een ontbrekende of lege instelling.</summary>
    private static string? Ingesteld(IConfiguration config, string key) =>
        string.IsNullOrWhiteSpace(config[key]) ? null : config[key];

    /// <summary>
    /// Het ingestelde wachtwoord, of <c>null</c> als het ontbreekt en er geen terugvaloptie mag gelden.
    /// Een lege env-variabele telt als niet gezet; compose geeft die namelijk leeg door zodra je hem
    /// vergeet te vullen, en juist dan mag er geen wachtwoord uit deze broncode gelden.
    /// </summary>
    private static string? Wachtwoord(IConfiguration config, string key, string devFallbackWaarde, bool devFallback)
    {
        var value = config[key];
        if (!string.IsNullOrWhiteSpace(value)) return value;
        return devFallback ? devFallbackWaarde : null;
    }
}
