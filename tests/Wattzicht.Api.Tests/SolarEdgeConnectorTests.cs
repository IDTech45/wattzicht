using System.Text.Json;
using Wattzicht.Api.Connectors;
using Xunit;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Het omrekenen van wat SolarEdge stuurt naar wat wij opslaan.
///
/// SolarEdge geeft alle vermogens positief en vertelt met de richting in "connections" wat er
/// gebeurt: staat het net als bestemming, dan levert het huis terug. Bij ons is het net juist
/// positief bij afname en negatief bij teruglevering, en een batterij positief bij ontladen. Dat is
/// precies het soort omrekening waar een teken omklapt en waar je het pas maanden later aan de
/// grafiek ziet, dus daar liggen hier regels voor vast.
///
/// Ook de eenheid is een valkuil: SolarEdge stuurt standaard kilowatt, maar schrijft er de eenheid
/// bij. Die lezen we uit in plaats van hem aan te nemen.
/// </summary>
public class SolarEdgeConnectorTests
{
    private static JsonElement Json(string ruw) => JsonDocument.Parse(ruw).RootElement.Clone();

    private static readonly DateTimeOffset Nu = new(2026, 11, 20, 13, 0, 0, TimeSpan.Zero);

    /// <summary>Het overzicht is wat elke installatie heeft, ook zonder verbruiksmeter.</summary>
    private static JsonElement Overzicht(double vermogenW = 2500, double vandaagWh = 8000, double totaalKwh = 12.5) =>
        Json($$"""
        {
          "overview": {
            "lastUpdateTime": "2026-11-20 14:00:00",
            "lifeTimeData":  { "energy": {{totaalKwh}}, "unit": "kWh" },
            "lastDayData":   { "energy": {{vandaagWh}} },
            "currentPower":  { "power": {{vermogenW}} }
          }
        }
        """);

    private static JsonElement Stroom(string richting = "naarNet") => Json($$"""
        {
          "unit": "kW",
          "GRID":    { "status": "Active", "currentPower": 1.2 },
          "LOAD":    { "status": "Active", "currentPower": 0.8 },
          "PV":      { "status": "Active", "currentPower": 2.0 },
          "STORAGE": { "status": "{{(richting == "ontladen" ? "Discharging" : "Charging")}}", "currentPower": 1.5, "chargeLevel": 64 },
          "connections": [ {{(richting == "naarNet" ? """{ "from": "PV", "to": "Grid" }""" : """{ "from": "Grid", "to": "Load" }""")}} ]
        }
        """);

    [Fact]
    public void Zonder_verbruiksmeter_komt_alleen_de_zon_binnen()
    {
        var r = SolarEdgeConnector.Lees(Overzicht(), null, Nu);

        Assert.Equal(2500, r.PvPowerW);
        Assert.Equal(8000, r.PvTodayWh);
        Assert.Equal(12500, r.PvEnergyWh);   // 12,5 kWh uit het overzicht, bij ons in Wh
        Assert.Equal(0, r.LoadPowerW);
        Assert.Equal(0, r.GridPowerW);
    }

    /// <summary>Teruglevering is bij ons negatief; SolarEdge zegt het met de richting, niet met een min.</summary>
    [Fact]
    public void Teruglevering_wordt_negatief()
    {
        var r = SolarEdgeConnector.Lees(Overzicht(), Stroom("naarNet"), Nu);

        Assert.Equal(-1200, r.GridPowerW);
        Assert.Equal(800, r.LoadPowerW);
    }

    [Fact]
    public void Afname_blijft_positief()
    {
        var r = SolarEdgeConnector.Lees(Overzicht(), Stroom("vanNet"), Nu);

        Assert.Equal(1200, r.GridPowerW);
    }

    /// <summary>
    /// De vermogensstroom is actueler dan het overzicht, dus die wint voor de zon. De tellers komen
    /// altijd uit het overzicht, want die staan niet in de vermogensstroom.
    /// </summary>
    [Fact]
    public void De_vermogensstroom_wint_voor_de_zon_het_overzicht_voor_de_tellers()
    {
        var r = SolarEdgeConnector.Lees(Overzicht(vermogenW: 2500), Stroom(), Nu);

        Assert.Equal(2000, r.PvPowerW);      // 2,0 kW uit de stroom
        Assert.Equal(12500, r.PvEnergyWh);   // teller blijft uit het overzicht
    }

    [Fact]
    public void Een_ladende_batterij_is_negatief_en_een_ontladende_positief()
    {
        var laadt = SolarEdgeConnector.LeesBatterij(Stroom("naarNet"), Nu);
        var ontlaadt = SolarEdgeConnector.LeesBatterij(Stroom("ontladen"), Nu);

        Assert.Equal(-1500, laadt.BatteryPowerW);
        Assert.Equal(1500, ontlaadt.BatteryPowerW);
        Assert.Equal(64, laadt.BatterySoc);
    }

    /// <summary>Zonder batterij in het antwoord melden we er geen, in plaats van nul te verzinnen.</summary>
    [Fact]
    public void Zonder_batterij_blijft_de_lading_leeg()
    {
        var r = SolarEdgeConnector.LeesBatterij(null, Nu);

        Assert.Equal(0, r.BatteryPowerW);
        Assert.Null(r.BatterySoc);
    }

    /// <summary>
    /// De eenheid staat erbij en die lezen we uit. Stond hier een aanname van kilowatt, dan zou een
    /// installatie die in watt rapporteert duizend keer te hoog uitkomen.
    /// </summary>
    [Fact]
    public void De_eenheid_uit_het_antwoord_telt()
    {
        var inWatt = Json("""
        {
          "unit": "W",
          "GRID": { "status": "Active", "currentPower": 1200 },
          "LOAD": { "status": "Active", "currentPower": 800 },
          "PV":   { "status": "Active", "currentPower": 2000 },
          "connections": [ { "from": "Grid", "to": "Load" } ]
        }
        """);

        var r = SolarEdgeConnector.Lees(Overzicht(), inWatt, Nu);

        Assert.Equal(2000, r.PvPowerW);
        Assert.Equal(1200, r.GridPowerW);
    }

    /// <summary>Een teller in kilowattuur en een in wattuur mogen niet door elkaar lopen.</summary>
    [Fact]
    public void Tellers_rekenen_naar_wattuur()
    {
        var overzicht = Json("""
        {
          "overview": {
            "lifeTimeData": { "energy": 9.4, "unit": "MWh" },
            "lastDayData":  { "energy": 7200, "unit": "Wh" },
            "currentPower": { "power": 0 }
          }
        }
        """);

        var r = SolarEdgeConnector.Lees(overzicht, null, Nu);

        Assert.Equal(9_400_000, r.PvEnergyWh);
        Assert.Equal(7200, r.PvTodayWh);
    }
}
