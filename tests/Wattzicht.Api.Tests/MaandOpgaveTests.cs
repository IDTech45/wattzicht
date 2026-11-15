using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Xunit;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Maandopbrengsten die de bewoner zelf invult.
///
/// Dit bestaat omdat de weg eromheen dicht zat. De dagreeks van Enphase zit op het gratis plan
/// achter slot ("Not authorized to access this resource") en in hun eigen app staat nergens een
/// export; wat er wél staat is een jaargrafiek met twaalf balkjes. Die overtypen is lelijk, maar een
/// leeg jaar terwijl een dak vier megawattuur heeft gemaakt is erger.
///
/// Twee regels moeten kloppen. Een opgave vult alleen gaten: waar wij zelf gemeten hebben, wint de
/// meting. En een opgegeven maand hoort in de maand- en jaargrafiek terecht te komen, anders is het
/// invullen voor niets geweest.
/// </summary>
public class MaandOpgaveTests
{
    private static readonly Guid Woning = Guid.NewGuid();

    private static AppDbContext Db(string naam) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(naam).Options, TenantContext.System);

    private static async Task<AppDbContext> MetWoningAsync()
    {
        var db = Db(Guid.NewGuid().ToString());
        db.Households.Add(new Household { Id = Woning, Name = "Thuis" });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Een_opgegeven_maand_komt_in_de_grafiek()
    {
        await using var db = await MetWoningAsync();
        db.MonthlyHistory.Add(new MonthlyHistory
        {
            Id = Guid.NewGuid(), HouseholdId = Woning, Year = 2026, Month = 6,
            PvKwh = 535, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var queries = new HouseholdQueries(db, TimeProvider.System);
        var rijen = await queries.SummariesAsync(Woning, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), default);

        var juni = Assert.Single(rijen.Where(r => r.Date.Month == 6));
        Assert.Equal(535, juni.PvKwh);
    }

    [Fact]
    public async Task Waar_wij_gemeten_hebben_telt_de_opgave_niet_mee()
    {
        // Anders staat een maand er twee keer in: één keer gemeten en één keer overgetypt.
        await using var db = await MetWoningAsync();
        db.DailySummaries.Add(new DailySummary
        {
            Id = Guid.NewGuid(), HouseholdId = Woning, Date = new DateOnly(2026, 6, 10),
            PvKwh = 20, UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.MonthlyHistory.Add(new MonthlyHistory
        {
            Id = Guid.NewGuid(), HouseholdId = Woning, Year = 2026, Month = 6,
            PvKwh = 535, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var queries = new HouseholdQueries(db, TimeProvider.System);
        var rijen = await queries.SummariesAsync(Woning, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), default);

        var juni = Assert.Single(rijen);
        Assert.Equal(20, juni.PvKwh);
    }

    [Fact]
    public async Task Buiten_het_gevraagde_bereik_blijft_hij_weg()
    {
        await using var db = await MetWoningAsync();
        db.MonthlyHistory.Add(new MonthlyHistory
        {
            Id = Guid.NewGuid(), HouseholdId = Woning, Year = 2025, Month = 6,
            PvKwh = 500, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var queries = new HouseholdQueries(db, TimeProvider.System);
        var rijen = await queries.SummariesAsync(Woning, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), default);

        Assert.Empty(rijen);
    }

    [Fact]
    public async Task Het_overzicht_zegt_welke_maanden_al_gemeten_zijn()
    {
        // Zo weet het invulscherm welke velden het op slot moet zetten.
        await using var db = await MetWoningAsync();
        db.DailySummaries.Add(new DailySummary
        {
            Id = Guid.NewGuid(), HouseholdId = Woning, Date = new DateOnly(2026, 11, 14),
            PvKwh = 2.45, UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.MonthlyHistory.Add(new MonthlyHistory
        {
            Id = Guid.NewGuid(), HouseholdId = Woning, Year = 2026, Month = 3,
            PvKwh = 445, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var jaar = await new HouseholdQueries(db, TimeProvider.System).MaandOpgaveAsync(Woning, 2026, default);

        Assert.Equal(12, jaar.Maanden.Count);
        Assert.True(jaar.Maanden.Single(m => m.Month == 11).Gemeten);
        Assert.False(jaar.Maanden.Single(m => m.Month == 3).Gemeten);
        Assert.Equal(445, jaar.Maanden.Single(m => m.Month == 3).PvKwh);
    }
}
