using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Meten we het huisverbruik en de netkant van deze woning werkelijk?
///
/// Veel woningen hebben alleen een omvormer: geen CT-klemmen, geen P1-meter. Die omvormer vertelt
/// wat de panelen maken en verder niets, dus stonden het huis en het net op het beginscherm altijd
/// op 0,0 kW. Dat leest als een huis waar niemand thuis is en een net waar niets over gaat, en het
/// betekent alleen dat er geen meter tussen zit.
///
/// Dat verschil kun je niet aan de laatste meting zien: nul watt op het net kan "precies in balans"
/// betekenen. Daarom kijken we naar een week metingen, en daarom staat hier wat er gebeurt in de drie
/// gevallen die ertoe doen: geen meter, wel een meter die nu stil is, en een verse koppeling waarvan
/// we nog niets weten.
/// </summary>
public sealed class MeetBereikTests
{
    private readonly string _dbNaam = Guid.NewGuid().ToString();
    private static readonly Guid Woning = Guid.NewGuid();
    private static readonly Guid Apparaat = Guid.NewGuid();
    private static readonly Guid Installateur = Guid.NewGuid();
    private static readonly DateTimeOffset Nu = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private AppDbContext Db() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbNaam).Options, TenantContext.System);

    private static HouseholdQueries Vragen(AppDbContext db) =>
        new(db, new FakeTimeProvider(Nu));

    /// <summary>
    /// Een woning met één omvormer en de meegegeven metingen.
    ///
    /// De installateur en het huishouden moeten er echt staan. Het queryfilter op metingen loopt via
    /// het apparaat naar de woning naar de installateur, en zonder die rijen valt de meting uit de
    /// join en lijkt de database leeg.
    /// </summary>
    private async Task<AppDbContext> MetMetingenAsync(params Reading[] metingen)
    {
        var db = Db();
        db.Installers.Add(new Installer
        {
            Id = Installateur, Name = "Triple Energie", Slug = "triple-energie",
            ApprovedAt = Nu, CreatedAt = Nu,
        });
        db.Households.Add(new Household
        {
            Id = Woning, Name = "Thuis", InstallerId = Installateur, CreatedAt = Nu,
        });
        db.Devices.Add(new Device
        {
            Id = Apparaat,
            HouseholdId = Woning,
            Type = DeviceType.Inverter,
            Brand = DeviceBrand.Enphase,
            Name = "Zonnepanelen",
            ExternalId = Apparaat.ToString(),
        });
        db.Readings.AddRange(metingen);
        await db.SaveChangesAsync();
        return db;
    }

    private static Reading Meting(int urenTerug, double huis = 0, double net = 0, double pv = 0) => new()
    {
        DeviceId = Apparaat,
        Timestamp = Nu.AddHours(-urenTerug),
        PvPowerW = pv,
        LoadPowerW = huis,
        GridPowerW = net,
    };

    [Fact]
    public async Task Alleen_een_omvormer_betekent_geen_huis_en_geen_net()
    {
        await using var db = await MetMetingenAsync(
            Meting(48, pv: 2200), Meting(24, pv: 3100), Meting(1, pv: 400));

        var bereik = await Vragen(db).MeetBereikAsync(Woning, default);

        Assert.False(bereik.Huis);
        Assert.False(bereik.Net);
    }

    [Fact]
    public async Task Een_meter_die_nu_op_nul_staat_blijft_een_meter()
    {
        // Dit is het geval waarvoor we naar een week kijken en niet naar de laatste meting: een huis
        // dat precies in balans is meet nul op het net, en dat is wél een meting.
        await using var db = await MetMetingenAsync(
            Meting(30, huis: 540, net: -1200), Meting(2, huis: 0, net: 0));

        var bereik = await Vragen(db).MeetBereikAsync(Woning, default);

        Assert.True(bereik.Huis);
        Assert.True(bereik.Net);
    }

    [Fact]
    public async Task Teruglevering_telt_ook_als_meting_van_het_net()
    {
        await using var db = await MetMetingenAsync(Meting(5, net: -2400));

        var bereik = await Vragen(db).MeetBereikAsync(Woning, default);

        Assert.True(bereik.Net);
        Assert.False(bereik.Huis);
    }

    [Fact]
    public async Task Zonder_metingen_beweren_we_niets()
    {
        // Een verse koppeling. Dan is "we meten je huis niet" een conclusie over een apparaat dat
        // nog niets heeft kunnen zeggen.
        await using var db = await MetMetingenAsync();

        var bereik = await Vragen(db).MeetBereikAsync(Woning, default);

        Assert.True(bereik.Huis);
        Assert.True(bereik.Net);
    }

    [Fact]
    public async Task Metingen_van_vorige_maand_tellen_niet_mee()
    {
        // Een P1-meter die eruit is gehaald: drie weken terug kwam er nog verbruik binnen, sindsdien
        // alleen nog de omvormer. Dan hoort het huis uit het beeld te verdwijnen in plaats van jaren
        // "gemeten" te blijven op een meter die er niet meer hangt.
        await using var db = await MetMetingenAsync(
            Meting(24 * 20, huis: 700, net: 500), Meting(3, pv: 1800));

        var bereik = await Vragen(db).MeetBereikAsync(Woning, default);

        Assert.False(bereik.Huis);
        Assert.False(bereik.Net);
    }

    [Fact]
    public void De_momentopname_geeft_het_meetbereik_door()
    {
        var metingen = new[] { Meting(0, pv: 1500) };

        var zonderMeter = SnapshotBuilder.Build(
            metingen, [DeviceType.Inverter], new SnapshotBuilder.MeetBereik(false, false));
        Assert.False(zonderMeter.HasLoadData);
        Assert.False(zonderMeter.HasGridData);

        // Weet de beller het niet, dan beweren we niets. Oudere aanroepers (het installateursportal,
        // de pollronde) geven dit niet mee en horen daar niet anders uit te komen dan vroeger.
        var onbekend = SnapshotBuilder.Build(metingen, [DeviceType.Inverter]);
        Assert.True(onbekend.HasLoadData);
        Assert.True(onbekend.HasGridData);
    }
}
