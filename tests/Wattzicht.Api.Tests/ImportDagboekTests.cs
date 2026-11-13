using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests;

/// <summary>
/// De grenzen rond documenten waar het inlezen niets uit haalde.
///
/// Dit is post van een bewoner die op onze server komt te liggen omdat wij zijn formaat nog niet
/// kennen. Dat mag, maar dan moeten de grenzen kloppen en moeten ze ook echt draaien: afgekapt,
/// dertig dagen, en daarna weg. Een bewaartermijn die alleen in een toelichting staat is er geen.
/// </summary>
public sealed class ImportDagboekTests
{
    private readonly string _dbNaam = Guid.NewGuid().ToString();

    private AppDbContext Db() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbNaam).Options, TenantContext.System);

    [Fact]
    public void Een_lange_nota_wordt_afgekapt()
    {
        var lang = new string('x', ImportDagboek.MaximumTekens + 5_000);

        Assert.Equal(ImportDagboek.MaximumTekens, ImportDagboek.Afkappen(lang).Length);
        Assert.Equal("kort", ImportDagboek.Afkappen("kort"));
    }

    [Fact]
    public void Een_bestand_zonder_naam_krijgt_er_een()
    {
        Assert.Equal("zonder naam", ImportDagboek.KorteNaam(null));
        Assert.Equal("zonder naam", ImportDagboek.KorteNaam("   "));
        Assert.Equal("nota.pdf", ImportDagboek.KorteNaam("nota.pdf"));
        Assert.Equal(200, ImportDagboek.KorteNaam(new string('a', 400)).Length);
    }

    [Fact]
    public void De_bewaartermijn_is_dertig_dagen()
    {
        Assert.Equal(30, ImportDagboek.Bewaartermijn.TotalDays);
    }

    [Fact]
    public async Task Wat_over_de_termijn_heen_is_gaat_weg_en_de_rest_blijft()
    {
        var nu = new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.Zero);
        var woning = Guid.NewGuid();

        await using (var db = Db())
        {
            db.Households.Add(new Household { Id = woning, Name = "Thuis", Plan = "free", CreatedAt = nu });
            db.FailedImports.Add(new FailedImport
            {
                Id = Guid.NewGuid(), HouseholdId = woning, FileName = "oud.pdf", Text = "a",
                CreatedAt = nu.AddDays(-31), ExpiresAt = nu.AddDays(-1)
            });
            db.FailedImports.Add(new FailedImport
            {
                Id = Guid.NewGuid(), HouseholdId = woning, FileName = "vers.pdf", Text = "b",
                CreatedAt = nu.AddDays(-2), ExpiresAt = nu.AddDays(28)
            });
            await db.SaveChangesAsync();
        }

        await using (var db = Db())
        {
            Assert.Equal(1, await ImportDagboek.OpruimenAsync(db, nu));
        }

        await using var na = Db();
        var over = Assert.Single(na.FailedImports.IgnoreQueryFilters());
        Assert.Equal("vers.pdf", over.FileName);
    }

    [Fact]
    public async Task Opruimen_zonder_iets_te_doen_is_geen_fout()
    {
        await using var db = Db();
        Assert.Equal(0, await ImportDagboek.OpruimenAsync(db, DateTimeOffset.UtcNow));
    }
}
