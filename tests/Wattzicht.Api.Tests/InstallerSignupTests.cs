using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Een installateur kan zich zelf aanmelden, en dat is precies waarom de goedkeuring bestaat.
///
/// Zonder die drempel schrijft iemand zich in als "installateur", krijgt een code, en haalt daarmee
/// bewoners naar zijn eigen portal: hij ziet dan hun opwek, verbruik en adresgegevens. De regel is
/// daarom dat een code pas werkt nadat Wattzicht de aanmelding heeft goedgekeurd. Inloggen en zijn
/// eigen logo invullen mag wel meteen; dat raakt niemand anders.
///
/// Deze tests leggen het gedrag vast op het niveau van de database, dus zonder webserver: de query die
/// de code opzoekt moet een niet-goedgekeurde installateur overslaan.
/// </summary>
public sealed class InstallerSignupTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private static readonly Guid Goedgekeurd = Guid.NewGuid();
    private static readonly Guid Wachtend = Guid.NewGuid();

    public InstallerSignupTests()
    {
        using var db = Maak();
        var now = DateTimeOffset.UtcNow;

        db.Installers.AddRange(
            new Installer
            {
                Id = Goedgekeurd, Name = "Triple Energie", Slug = "triple-energie",
                ApprovedAt = now, CreatedAt = now,
            },
            new Installer
            {
                Id = Wachtend, Name = "Nog Niet BV", Slug = "nog-niet-bv",
                ApprovedAt = null, CreatedAt = now,
            });
        db.SaveChanges();
    }

    [Fact]
    public async Task Een_goedgekeurde_code_is_te_vinden()
    {
        using var db = Maak();
        var installer = await db.Installers.FirstOrDefaultAsync(i => i.Slug == "triple-energie");

        Assert.NotNull(installer);
        Assert.NotNull(installer!.ApprovedAt);
    }

    [Fact]
    public async Task Een_wachtende_aanmelding_mag_geen_bewoners_binnenhalen()
    {
        using var db = Maak();

        // Dit is de voorwaarde die de registratie gebruikt: bestaat hij én is hij goedgekeurd.
        var bruikbaar = await db.Installers
            .FirstOrDefaultAsync(i => i.Slug == "nog-niet-bv" && i.ApprovedAt != null);

        Assert.Null(bruikbaar);
    }

    [Fact]
    public async Task Goedkeuren_zet_de_deur_open_en_intrekken_sluit_hem_weer()
    {
        var moment = DateTimeOffset.UtcNow;

        using (var db = Maak())
        {
            var installer = await db.Installers.SingleAsync(i => i.Id == Wachtend);
            installer.ApprovedAt = moment;
            await db.SaveChangesAsync();
        }

        using (var db = Maak())
        {
            Assert.NotNull(await db.Installers
                .FirstOrDefaultAsync(i => i.Slug == "nog-niet-bv" && i.ApprovedAt != null));

            var installer = await db.Installers.SingleAsync(i => i.Id == Wachtend);
            installer.ApprovedAt = null;
            await db.SaveChangesAsync();
        }

        using (var db = Maak())
        {
            Assert.Null(await db.Installers
                .FirstOrDefaultAsync(i => i.Slug == "nog-niet-bv" && i.ApprovedAt != null));
        }
    }

    [Fact]
    public async Task Een_nieuw_huishouden_staat_standaard_op_het_plan_van_de_installateur()
    {
        using var db = Maak();
        var household = new Household
        {
            Id = Guid.NewGuid(),
            Name = "Thuis",
            InstallerId = Goedgekeurd,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Households.Add(household);
        await db.SaveChangesAsync();

        var bewaard = await db.Households.SingleAsync(h => h.Id == household.Id);
        Assert.Equal(Plans.ViaInstaller, bewaard.Plan);
    }

    private AppDbContext Maak() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options,
            TenantContext.System);
}
