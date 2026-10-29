using Microsoft.Extensions.Time.Testing;
using Wattzicht.Api.Connectors;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests;

/// <summary>De demo-dagcurve moet geloofwaardig en reproduceerbaar zijn.</summary>
public sealed class DemoConnectorTests
{
    // 21 juni 2026, 13:00 Nederlandse zomertijd = 11:00 UTC
    private static readonly DateTimeOffset ZomerMiddag = new(2026, 6, 21, 11, 0, 0, TimeSpan.Zero);

    // 21 juni 2026, 02:00 Nederlandse zomertijd = 00:00 UTC
    private static readonly DateTimeOffset ZomerNacht = new(2026, 6, 21, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Overdag_in_de_zomer_is_er_flinke_zonopwek()
    {
        var reading = DemoConnector.Simulate(DeviceType.Inverter, ZomerMiddag);

        Assert.InRange(reading.PvPowerW, 2500, 4300);
        Assert.True(reading.LoadPowerW > 0);
        // Bij zoveel zon levert de demo-woning terug (grid negatief).
        Assert.True(reading.GridPowerW < 0, $"Verwacht teruglevering, kreeg {reading.GridPowerW} W");
    }

    [Fact]
    public void S_nachts_is_er_geen_zonopwek()
    {
        var reading = DemoConnector.Simulate(DeviceType.Inverter, ZomerNacht);

        Assert.Equal(0, reading.PvPowerW);
        Assert.True(reading.GridPowerW > 0, "Zonder zon wordt van het net afgenomen");
    }

    [Fact]
    public void Winter_piek_is_lager_dan_zomer_piek()
    {
        var winterMiddag = new DateTimeOffset(2026, 12, 21, 12, 0, 0, TimeSpan.Zero); // 13:00 lokaal
        var winter = DemoConnector.Simulate(DeviceType.Inverter, winterMiddag);
        var zomer = DemoConnector.Simulate(DeviceType.Inverter, ZomerMiddag);

        Assert.True(winter.PvPowerW > 0);
        Assert.True(winter.PvPowerW < zomer.PvPowerW);
    }

    [Fact]
    public void Batterij_laadt_overdag_en_heeft_geldige_soc()
    {
        var reading = DemoConnector.Simulate(DeviceType.Battery, ZomerMiddag);

        Assert.True(reading.BatteryPowerW < 0, "Overdag bij overschot moet de batterij laden (negatief)");
        Assert.NotNull(reading.BatterySoc);
        Assert.InRange(reading.BatterySoc!.Value, 0, 100);
        // Batterij-meting draagt geen zon/net bij, anders telt het huishouden dubbel.
        Assert.Equal(0, reading.PvPowerW);
        Assert.Equal(0, reading.GridPowerW);
    }

    [Fact]
    public void Laadpaal_laadt_alleen_s_nachts()
    {
        var nacht = DemoConnector.Simulate(DeviceType.Charger, ZomerNacht);
        var middag = DemoConnector.Simulate(DeviceType.Charger, ZomerMiddag);

        Assert.True(nacht.ChargerPowerW > 0);
        Assert.Equal(0, middag.ChargerPowerW);
    }

    [Fact]
    public void Simulatie_is_deterministisch()
    {
        var a = DemoConnector.Simulate(DeviceType.Inverter, ZomerMiddag, "seed-x");
        var b = DemoConnector.Simulate(DeviceType.Inverter, ZomerMiddag, "seed-x");

        Assert.Equal(a, b);
    }

    [Fact]
    public async Task ReadAsync_gebruikt_de_klok_en_het_apparaattype()
    {
        var clock = new FakeTimeProvider(ZomerMiddag);
        var connector = new DemoConnector(clock);
        var device = new Device { Id = Guid.NewGuid(), Type = DeviceType.Inverter, Brand = DeviceBrand.Demo, ExternalId = "DEMO-INV-001", Name = "Demo" };

        var reading = await connector.ReadAsync(device, new Dictionary<string, string>(), CancellationToken.None);

        Assert.Equal(ZomerMiddag, reading.Timestamp);
        Assert.True(reading.PvPowerW > 0);
    }

    [Fact]
    public async Task ConnectAsync_geeft_een_demo_apparaat_per_type()
    {
        var connector = new DemoConnector(new FakeTimeProvider(ZomerMiddag));

        var result = await connector.ConnectAsync(
            new ConnectRequest(DeviceBrand.Demo, DeviceType.Battery, new Dictionary<string, string>()),
            CancellationToken.None);

        Assert.True(result.Success);
        var device = Assert.Single(result.Devices);
        Assert.Equal(DeviceType.Battery, device.Type);
        Assert.Equal("DEMO-BAT-001", device.ExternalId);
    }

    [Fact]
    public void Energiebalans_klopt_over_een_hele_dag()
    {
        // Voor elk kwartier: zon + ontladen + net = verbruik + laadpaal (binnen afrondingsmarge).
        for (var minute = 0; minute < 24 * 60; minute += 15)
        {
            var t = ZomerNacht.AddMinutes(minute);
            var inv = DemoConnector.Simulate(DeviceType.Inverter, t);
            var bat = DemoConnector.Simulate(DeviceType.Battery, t);
            var chg = DemoConnector.Simulate(DeviceType.Charger, t);

            var supply = inv.PvPowerW + bat.BatteryPowerW + inv.GridPowerW;
            var demand = inv.LoadPowerW + chg.ChargerPowerW;

            Assert.InRange(supply - demand, -3, 3);
        }
    }
}
