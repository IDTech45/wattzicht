using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Xunit;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Met welk tarief een dag wordt gerekend die ouder is dan het oudste tarief van de bewoner.
///
/// Dat is geen randgeval: wie zijn jaren uit het portaal van de fabrikant inleest heeft drie jaar aan
/// opwek staan en een tarief van vorige week. Tot nu toe vielen al die oude dagen terug op een
/// platformbrede standaard van 28 cent, terwijl op zijn contract 22 cent staat. Over 11.747 kWh
/// scheelt dat zevenhonderd euro, en dat bedrag stond gewoon op zijn scherm als "bespaard".
///
/// Een euro op het scherm hoort van hem te zijn. Dus rekenen we oude dagen met zijn eigen oudste
/// tarief, en geldt de standaard alleen nog voor wie helemaal niets heeft ingevuld.
/// </summary>
public sealed class TariefVoorOudeDagenTests
{
    private readonly string _dbNaam = Guid.NewGuid().ToString();
    private static readonly Guid Woning = Guid.NewGuid();

    private AppDbContext Db() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbNaam).Options, TenantContext.System);

    private static SummaryService Dienst(AppDbContext db) => new(db, new SavingsCalculator(), TimeProvider.System);

    /// <summary>De woning zelf moet bestaan: het huurdersfilter op tarieven kijkt naar zijn installateur.</summary>
    private static async Task WoningAsync(AppDbContext db)
    {
        db.Households.Add(new Household
        {
            Id = Woning, Name = "Thuis", InstallerId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static Tariff Tarief(DateOnly vanaf, decimal import) => new()
    {
        Id = Guid.NewGuid(), HouseholdId = Woning, ValidFrom = vanaf,
        ImportPricePerKwh = import, ExportPricePerKwh = 0.06m,
    };

    [Fact]
    public async Task Een_dag_van_voor_het_eerste_tarief_rekent_met_dat_eerste_tarief()
    {
        await using var db = Db();
        await WoningAsync(db);
        db.Tariffs.Add(Tarief(new DateOnly(2026, 11, 13), 0.22m));
        await db.SaveChangesAsync();

        var rates = await Dienst(db).GetRatesAsync(Woning, new DateOnly(2023, 10, 8), default);

        Assert.Equal(0.22m, rates.ImportPricePerKwh);
    }

    /// <summary>Is er een tarief dat op die dag gold, dan verandert er niets.</summary>
    [Fact]
    public async Task Een_dag_binnen_een_tarief_houdt_dat_tarief()
    {
        await using var db = Db();
        await WoningAsync(db);
        db.Tariffs.AddRange(Tarief(new DateOnly(2026, 1, 1), 0.30m), Tarief(new DateOnly(2026, 11, 13), 0.22m));
        await db.SaveChangesAsync();

        var rates = await Dienst(db).GetRatesAsync(Woning, new DateOnly(2026, 6, 1), default);

        Assert.Equal(0.30m, rates.ImportPricePerKwh);
    }

    /// <summary>En het oudste tarief is het oudste, ook als er meerdere zijn.</summary>
    [Fact]
    public async Task Van_meerdere_tarieven_telt_het_oudste_voor_oude_dagen()
    {
        await using var db = Db();
        await WoningAsync(db);
        db.Tariffs.AddRange(Tarief(new DateOnly(2026, 1, 1), 0.30m), Tarief(new DateOnly(2026, 11, 13), 0.22m));
        await db.SaveChangesAsync();

        var rates = await Dienst(db).GetRatesAsync(Woning, new DateOnly(2024, 5, 5), default);

        Assert.Equal(0.30m, rates.ImportPricePerKwh);
    }

    /// <summary>Wie helemaal geen tarief heeft ingevuld, houdt de standaard.</summary>
    [Fact]
    public async Task Zonder_enig_tarief_geldt_de_standaard()
    {
        await using var db = Db();

        var rates = await Dienst(db).GetRatesAsync(Woning, new DateOnly(2026, 6, 1), default);

        Assert.Equal(TariffRates.Default.ImportPricePerKwh, rates.ImportPricePerKwh);
    }
}
