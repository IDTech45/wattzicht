using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Welke tegels het beginscherm laat zien.
///
/// Een woning met alleen zonnepanelen kreeg toch een batterij "in rust" en een laadpaal "niet aan
/// het laden". Dat komt doordat die velden altijd een getal zijn: nul watt. Nul is geen bewijs dat
/// je iets hebt, en op een scherm leest zo'n tegel als een apparaat dat stilstaat in plaats van een
/// apparaat dat er niet is. Daarom zegt de momentopname nu zelf welke soorten er hangen.
/// </summary>
public sealed class EnergiestroomTests
{
    private static Reading Meting(double pv = 0, double batterij = 0, double laadpaal = 0, double? soc = null) =>
        new()
        {
            DeviceId = Guid.NewGuid(),
            Timestamp = new DateTimeOffset(2026, 11, 13, 18, 13, 0, TimeSpan.Zero),
            PvPowerW = pv,
            BatteryPowerW = batterij,
            BatterySoc = soc,
            ChargerPowerW = laadpaal,
        };

    [Fact]
    public void Alleen_zonnepanelen_geeft_geen_batterij_en_geen_laadpaal()
    {
        var uit = SnapshotBuilder.Build([Meting(pv: 0)], [DeviceType.Inverter]);

        Assert.False(uit.HasBattery);
        Assert.False(uit.HasCharger);
    }

    [Fact]
    public void Een_batterij_die_niets_doet_blijft_een_batterij()
    {
        // Nul watt en geen laadtoestand: toch hoort de tegel er te staan, want het ding hangt er.
        var uit = SnapshotBuilder.Build([Meting(), Meting()], [DeviceType.Inverter, DeviceType.Battery]);

        Assert.True(uit.HasBattery);
        Assert.False(uit.HasCharger);
    }

    [Fact]
    public void Een_laadpaal_telt_mee_zodra_hij_er_hangt()
    {
        var uit = SnapshotBuilder.Build([Meting()], [DeviceType.Charger]);

        Assert.True(uit.HasCharger);
    }

    [Fact]
    public void Zonder_lijst_met_soorten_raden_we_het_aan_de_metingen_af()
    {
        // Oudere aanroepers geven de soorten niet mee. Dan is een batterij die laadtoestand meldt
        // nog steeds een batterij, en een laadpaal die vermogen trekt nog steeds een laadpaal.
        var stil = SnapshotBuilder.Build([Meting(pv: 1200)]);
        Assert.False(stil.HasBattery);
        Assert.False(stil.HasCharger);

        var metBatterij = SnapshotBuilder.Build([Meting(soc: 64)]);
        Assert.True(metBatterij.HasBattery);

        var metLaadpaal = SnapshotBuilder.Build([Meting(laadpaal: 7400)]);
        Assert.True(metLaadpaal.HasCharger);
    }

    [Fact]
    public void Een_woning_zonder_apparaten_heeft_niets()
    {
        Assert.False(SnapshotBuilder.Empty.HasBattery);
        Assert.False(SnapshotBuilder.Empty.HasCharger);
        Assert.False(SnapshotBuilder.Build([], [DeviceType.Battery]).HasBattery);
    }
}
