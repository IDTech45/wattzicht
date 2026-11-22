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
        var omvormer = DemoConnector.Simulate(DeviceType.Inverter, ZomerMiddag);
        var meter = DemoConnector.Simulate(DeviceType.P1Meter, ZomerMiddag);

        Assert.InRange(omvormer.PvPowerW, 2500, 4300);
        Assert.True(meter.LoadPowerW > 0);
        // Bij zoveel zon levert de demo-woning terug (netstand negatief).
        Assert.True(meter.GridPowerW < 0, $"Verwacht teruglevering, kreeg {meter.GridPowerW} W");
    }

    /// <summary>
    /// Een omvormer meet de zon en niets anders.
    ///
    /// Hij gaf hier ook verbruik en netstand terug, en dat liep mis zodra er een demo-slimme-meter naast
    /// stond: allebei stuurden ze een netstand, de momentopname telt per veld op, en dan stond er 15 kW
    /// afname waar 8 kW hoorde. Dezelfde regel als bij de echte connectors: elk apparaat vult alleen zijn
    /// eigen velden.
    /// </summary>
    [Fact]
    public void De_omvormer_meet_alleen_de_zon()
    {
        var omvormer = DemoConnector.Simulate(DeviceType.Inverter, ZomerMiddag);

        Assert.Equal(0, omvormer.LoadPowerW);
        Assert.Equal(0, omvormer.GridPowerW);
        Assert.Equal(0, omvormer.BatteryPowerW);
        Assert.Equal(0, omvormer.ChargerPowerW);
    }

    [Fact]
    public void S_nachts_is_er_geen_zonopwek()
    {
        var omvormer = DemoConnector.Simulate(DeviceType.Inverter, ZomerNacht);
        var meter = DemoConnector.Simulate(DeviceType.P1Meter, ZomerNacht);

        Assert.Equal(0, omvormer.PvPowerW);
        Assert.True(meter.GridPowerW > 0, "Zonder zon wordt van het net afgenomen");
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

    /// <summary>
    /// De auto laadt 's nachts, maar niet elke nacht en niet de hele nacht.
    ///
    /// Hij stond zes uur lang op 7,4 kW, elke nacht: 44 kWh per nacht en 267 kWh per week. Drie keer het
    /// verbruik van een huishouden, en een auto die nooit vol raakt. In het besparingsscherm overstemde
    /// die ene post al het andere en stond elke dag op precies hetzelfde bedrag.
    /// </summary>
    [Fact]
    public void Laadpaal_laadt_alleen_s_nachts()
    {
        var laadnacht = EersteLaadnacht();
        var nacht = new DateTimeOffset(laadnacht.ToDateTime(new TimeOnly(23, 30)), TimeSpan.FromHours(2));
        var middag = new DateTimeOffset(laadnacht.ToDateTime(new TimeOnly(13, 0)), TimeSpan.FromHours(2));

        Assert.True(DemoConnector.Simulate(DeviceType.Charger, nacht).ChargerPowerW > 0);
        Assert.Equal(0, DemoConnector.Simulate(DeviceType.Charger, middag).ChargerPowerW);
    }

    /// <summary>Niet elke nacht: over twee weken hoort er minstens één nacht zonder laadbeurt te zitten.</summary>
    [Fact]
    public void De_auto_laadt_niet_elke_nacht()
    {
        var start = new DateOnly(2026, 6, 1);
        var nachten = Enumerable.Range(0, 14).Select(d => DemoConnector.LaadtDieNacht(start.AddDays(d))).ToList();

        Assert.Contains(true, nachten);
        Assert.Contains(false, nachten);
    }

    /// <summary>
    /// Een sessie loopt door over middernacht. Zou de keuze per kalenderdag gaan, dan stopte hij om
    /// twaalf uur 's nachts en begon er soms een halve beurt.
    /// </summary>
    [Fact]
    public void Een_laadbeurt_valt_niet_stil_om_middernacht()
    {
        var nacht = EersteLaadnacht();
        var voorMiddernacht = new DateTimeOffset(nacht.ToDateTime(new TimeOnly(23, 30)), TimeSpan.FromHours(2));
        var naMiddernacht = new DateTimeOffset(nacht.AddDays(1).ToDateTime(new TimeOnly(1, 0)), TimeSpan.FromHours(2));

        Assert.True(DemoConnector.Simulate(DeviceType.Charger, voorMiddernacht).ChargerPowerW > 0);
        Assert.True(DemoConnector.Simulate(DeviceType.Charger, naMiddernacht).ChargerPowerW > 0);
    }

    private static DateOnly EersteLaadnacht()
    {
        var dag = new DateOnly(2026, 6, 1);
        while (!DemoConnector.LaadtDieNacht(dag)) dag = dag.AddDays(1);
        return dag;
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
            // Verbruik en netstand komen van de slimme meter; de omvormer meet alleen de zon.
            var meter = DemoConnector.Simulate(DeviceType.P1Meter, t);

            var supply = inv.PvPowerW + bat.BatteryPowerW + meter.GridPowerW;
            var demand = meter.LoadPowerW + chg.ChargerPowerW;

            Assert.InRange(supply - demand, -3, 3);
        }
    }
}
