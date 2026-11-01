using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Wat zet de seeder klaar op een echte omgeving, en wat juist niet.
///
/// Twee dingen liepen hier mis. Het eerste: de wachtwoorden van het beheer-, installateurs- en
/// demo-account hadden een vaste terugvaloptie die in de broncode te lezen staat, en compose geeft
/// die variabelen leeg door zodra je ze vergeet te vullen. Op de productieserver stond daarmee een
/// beheerder klaar met een bekend wachtwoord. Het tweede: de demo-woning met een gesimuleerde
/// dagcurve werd overal aangemaakt, dus ook in het portal van een installateur die daar zijn echte
/// klanten verwacht.
/// </summary>
public sealed class DbSeederTests
{
    [Fact]
    public async Task Zonder_wachtwoorden_komt_er_buiten_Development_geen_enkel_account()
    {
        await using var services = Bouw("Production", new Dictionary<string, string?>());

        await DbSeeder.SeedAsync(services);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // De installateur zelf mag er wel staan: dat is geen inlog, alleen een rij met naam en logo.
        Assert.True(await db.Installers.IgnoreQueryFilters().AnyAsync(i => i.Id == DbSeeder.TripleEnergieId));
        Assert.Empty(await db.Users.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Met_ingestelde_wachtwoorden_komen_beheerder_en_installateur_er_wel()
    {
        await using var services = Bouw("Production", new Dictionary<string, string?>
        {
            ["Seed:AdminPassword"] = "een-echt-ingesteld-wachtwoord",
            ["Seed:InstallerPassword"] = "ook-echt-ingesteld",
        });

        await DbSeeder.SeedAsync(services);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = await db.Users.IgnoreQueryFilters().ToListAsync();

        Assert.Contains(users, u => u.Email == DbSeeder.AdminEmail && u.Role == UserRole.Admin);
        Assert.Contains(users, u => u.Email == DbSeeder.InstallerEmail && u.Role == UserRole.Installer);
        Assert.DoesNotContain(users, u => u.Email == DbSeeder.DemoEmail);
    }

    [Fact]
    public async Task Een_lege_omgevingsvariabele_telt_als_niet_ingesteld()
    {
        // Precies wat compose doet met ${SEED_ADMIN_PASSWORD:-} als je hem vergeet te vullen.
        await using var services = Bouw("Production", new Dictionary<string, string?>
        {
            ["Seed:AdminPassword"] = "",
            ["Seed:InstallerPassword"] = "   ",
        });

        await DbSeeder.SeedAsync(services);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.Users.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task De_demo_woning_blijft_weg_tenzij_je_hem_expliciet_aanzet()
    {
        await using var services = Bouw("Production", new Dictionary<string, string?>
        {
            ["Seed:AdminPassword"] = "ingesteld",
            ["Seed:InstallerPassword"] = "ingesteld",
            ["Seed:DemoPassword"] = "ingesteld",
        });

        await DbSeeder.SeedAsync(services);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Empty(await db.Households.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.Devices.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Met_Seed_Demo_aan_komt_de_demo_woning_er_met_drie_apparaten()
    {
        await using var services = Bouw("Production", new Dictionary<string, string?>
        {
            ["Seed:AdminPassword"] = "ingesteld",
            ["Seed:InstallerPassword"] = "ingesteld",
            ["Seed:Demo"] = "true",
            ["Seed:DemoPassword"] = "ingesteld",
        });

        await DbSeeder.SeedAsync(services);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var devices = await db.Devices.IgnoreQueryFilters().ToListAsync();

        Assert.Single(await db.Households.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(3, devices.Count);
        Assert.All(devices, d => Assert.Equal(DeviceBrand.Demo, d.Brand));
    }

    [Fact]
    public async Task In_Development_mag_de_vaste_terugvaloptie_nog_wel()
    {
        await using var services = Bouw("Development", new Dictionary<string, string?>());

        await DbSeeder.SeedAsync(services);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = await db.Users.IgnoreQueryFilters().ToListAsync();

        Assert.Contains(users, u => u.Email == DbSeeder.AdminEmail);
        Assert.Contains(users, u => u.Email == DbSeeder.DemoEmail);
    }

    [Fact]
    public async Task Twee_keer_seeden_verandert_niets_meer()
    {
        await using var services = Bouw("Development", new Dictionary<string, string?>());

        await DbSeeder.SeedAsync(services);
        await DbSeeder.SeedAsync(services);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Single(await db.Installers.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await db.Households.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(3, await db.Devices.IgnoreQueryFilters().CountAsync());
        Assert.Equal(3, await db.Users.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Een_al_bestaand_beheeraccount_krijgt_het_ingestelde_wachtwoord()
    {
        // De situatie op de server: de oude seeder maakte de beheerder al aan met het wachtwoord uit
        // de broncode. Alleen de nieuwe regel hierboven haalt dat account niet weg.
        var dbName = Guid.NewGuid().ToString();
        await using (var oud = Bouw("Development", new Dictionary<string, string?>(), dbName))
        {
            await DbSeeder.SeedAsync(oud);
        }

        await using var services = Bouw("Production", new Dictionary<string, string?>
        {
            ["Seed:AdminPassword"] = "nu-een-echt-geheim",
        }, dbName);

        await DbSeeder.SeedAsync(services);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<PasswordHasher>();
        var admin = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == DbSeeder.AdminEmail);

        Assert.True(hasher.Verify("nu-een-echt-geheim", admin.PasswordHash));
        Assert.False(hasher.Verify("Admin123!", admin.PasswordHash));
    }

    [Fact]
    public async Task Het_adres_van_de_beheerder_mag_je_zelf_kiezen()
    {
        await using var services = Bouw("Production", new Dictionary<string, string?>
        {
            ["Seed:AdminAccount"] = "Ibo@Wattzicht.NL",
            ["Seed:AdminPassword"] = "een-eigen-wachtwoord",
        });

        await DbSeeder.SeedAsync(services);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = await db.Users.IgnoreQueryFilters().ToListAsync();

        // Adressen worden kleingeschreven bewaard, net als bij inloggen en registreren.
        Assert.Contains(users, u => u.Email == "ibo@wattzicht.nl" && u.Role == UserRole.Admin);
        Assert.DoesNotContain(users, u => u.Email == DbSeeder.AdminEmail);
    }

    [Fact]
    public async Task Een_wachtwoord_uit_onze_eigen_broncode_wordt_geweigerd()
    {
        // Admin123! als instelling invullen voelt als iets dichtzetten, maar dat wachtwoord staat in de
        // git-geschiedenis van deze repo. Dan is het geen wachtwoord meer.
        await using var services = Bouw("Production", new Dictionary<string, string?>
        {
            ["Seed:AdminPassword"] = "Admin123!",
            ["Seed:InstallerPassword"] = "Installateur123!",
        });

        await DbSeeder.SeedAsync(services);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.Users.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Een_geweigerd_wachtwoord_overschrijft_ook_geen_bestaand_account()
    {
        var dbName = Guid.NewGuid().ToString();
        await using (var eerst = Bouw("Production", new Dictionary<string, string?>
        {
            ["Seed:AdminPassword"] = "het-goede-wachtwoord",
        }, dbName))
        {
            await DbSeeder.SeedAsync(eerst);
        }

        await using var services = Bouw("Production", new Dictionary<string, string?>
        {
            ["Seed:AdminPassword"] = "Admin123!",
        }, dbName);

        await DbSeeder.SeedAsync(services);

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<PasswordHasher>();
        var admin = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == DbSeeder.AdminEmail);

        Assert.True(hasher.Verify("het-goede-wachtwoord", admin.PasswordHash));
        Assert.False(hasher.Verify("Admin123!", admin.PasswordHash));
    }

    private static ServiceProvider Bouw(string environment, Dictionary<string, string?> settings, string? dbName = null)
    {
        dbName ??= Guid.NewGuid().ToString();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddSingleton<IHostEnvironment>(new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = environment });
        services.AddSingleton<PasswordHasher>();
        services.AddSingleton<CredentialProtector>();
        services.AddSingleton(TenantContext.System);
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(dbName));

        return services.BuildServiceProvider();
    }
}
