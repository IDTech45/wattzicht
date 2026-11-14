using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests;

/// <summary>
/// De dagen die nooit zijn ingevuld alsnog invullen.
///
/// Dit ging de eerste keer mis op een aanname die precies verkeerd om stond: de ronde zocht naar
/// dagen die op nul stonden, terwijl er bij deze woningen helemaal geen dagen in de database
/// stonden. Dat was immers de hele fout. Een ronde die bestaande nullen invult vindt dan niets, en
/// op het scherm bleef alles leeg.
///
/// Daarom begint het nu bij de metingen. Deze test legt dat vast, en ook de grens eromheen: een dag
/// waar al een getal staat blijft zoals hij is.
/// </summary>
public sealed class OpwekHerstelTests
{
    private readonly string _dbNaam = Guid.NewGuid().ToString();
    private static readonly Guid Woning = Guid.NewGuid();
    private static readonly Guid Apparaat = Guid.NewGuid();

    private AppDbContext Db() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbNaam).Options, TenantContext.System);

    private OpwekHerstelWorker Worker()
    {
        var diensten = new ServiceCollection();
        diensten.AddScoped(_ => Db());

        return new OpwekHerstelWorker(
            diensten.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 8, 0, 0, TimeSpan.Zero)),
            NullLogger<OpwekHerstelWorker>.Instance);
    }

    private static DateTimeOffset Op(int dag, int uur) => new(2026, 11, dag, uur, 0, 0, TimeSpan.FromHours(1));

    private async Task VulAsync(params (int Dag, int Uur, double TellerWh)[] metingen)
    {
        await using var db = Db();
        db.Households.Add(new Household { Id = Woning, Name = "Thuis", Plan = "free", CreatedAt = Op(10, 0) });
        db.Devices.Add(new Device
        {
            Id = Apparaat, HouseholdId = Woning, Type = DeviceType.Inverter, Brand = DeviceBrand.Enphase,
            Name = "Zonnepanelen", ExternalId = "x", CreatedAt = Op(10, 0),
        });

        foreach (var m in metingen)
        {
            db.Readings.Add(new Reading
            {
                DeviceId = Apparaat, Timestamp = Op(m.Dag, m.Uur), PvPowerW = 0, PvEnergyWh = m.TellerWh,
            });
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Dagen_die_er_nooit_waren_worden_aangemaakt()
    {
        // Dit is het geval van de bewoner die maandenlang nul zag: er staat niets in de dagtabel,
        // alleen metingen met een tellerstand.
        await VulAsync(
            (11, 12, 1_000_000), (11, 18, 1_006_000),
            (12, 10, 1_010_000), (12, 17, 1_018_000));

        var ingevuld = await Worker().HerstelAsync(default);

        Assert.True(ingevuld >= 2);

        await using var db = Db();
        var elf = await db.DailySummaries.IgnoreQueryFilters()
            .FirstAsync(s => s.HouseholdId == Woning && s.Date == new DateOnly(2026, 11, 11));
        var twaalf = await db.DailySummaries.IgnoreQueryFilters()
            .FirstAsync(s => s.HouseholdId == Woning && s.Date == new DateOnly(2026, 11, 12));

        Assert.Equal(6, elf.PvKwh);
        Assert.Equal(12, twaalf.PvKwh);
    }

    [Fact]
    public async Task Een_dag_waar_al_een_getal_staat_blijft_zoals_hij_is()
    {
        await VulAsync((11, 12, 1_000_000), (11, 18, 1_006_000));

        await using (var db = Db())
        {
            db.DailySummaries.Add(new DailySummary
            {
                Id = Guid.NewGuid(), HouseholdId = Woning, Date = new DateOnly(2026, 11, 11),
                PvKwh = 9.5, UpdatedAt = Op(11, 23),
            });
            await db.SaveChangesAsync();
        }

        await Worker().HerstelAsync(default);

        await using var na = Db();
        var dag = await na.DailySummaries.IgnoreQueryFilters()
            .FirstAsync(s => s.HouseholdId == Woning && s.Date == new DateOnly(2026, 11, 11));

        Assert.Equal(9.5, dag.PvKwh);
    }

    [Fact]
    public async Task Zonder_tellerstanden_gebeurt_er_niets()
    {
        await using (var db = Db())
        {
            db.Households.Add(new Household { Id = Woning, Name = "Thuis", Plan = "free", CreatedAt = Op(10, 0) });
            await db.SaveChangesAsync();
        }

        Assert.Equal(0, await Worker().HerstelAsync(default));
    }
}
