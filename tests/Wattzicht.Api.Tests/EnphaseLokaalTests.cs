using System.Text.Json;
using Wattzicht.Api.Connectors;
using Wattzicht.Shared;
using Xunit;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Enphase uitlezen via de gateway in het huis, niet via de cloud.
///
/// De cloudroute hangt op een developer-app van ons. Die sleutels staan leeg, en zelfs ingevuld geeft
/// het gratis plan duizend aanroepen per maand voor álle huizen samen; bij vijfhonderd uitlezingen is
/// het op. Daar komt bij dat de cloud alleen de zonkant geeft. De IQ Gateway in de meterkast kent die
/// limiet niet en levert in één antwoord de opwek, het huisverbruik en wat er over de meter gaat.
///
/// Dat laatste is de reden om het zo te doen: zonder netkant kun je niet zeggen wat iemand bespaart.
///
/// De vorm hieronder is die van /production.json?details=1. Let op de valkuil: beide regels onder
/// "consumption" heten "eim" en verschillen alleen in measurementType.
/// </summary>
public class EnphaseLokaalTests
{
    /// <summary>Een gateway met CT-klemmen: zon, huis en net.</summary>
    private const string MetMeters = """
    {
      "production": [
        { "type": "inverters", "activeCount": 14, "wNow": 2100, "whLifetime": 9000000 },
        { "type": "eim", "measurementType": "production", "wNow": 2143.5, "whLifetime": 9123456.7 }
      ],
      "consumption": [
        { "type": "eim", "measurementType": "total-consumption", "wNow": 850.25, "whLifetime": 7000000 },
        { "type": "eim", "measurementType": "net-consumption", "wNow": -1293.25,
          "whLifetimeImport": 4000000, "whLifetimeExport": 6000000 }
      ]
    }
    """;

    /// <summary>Een gateway zonder CT-klemmen: alleen micro-omvormers, geen consumption-lijst.</summary>
    private const string ZonderMeters = """
    { "production": [ { "type": "inverters", "activeCount": 10, "wNow": 1500, "whLifetime": 4500000 } ] }
    """;

    private static DeviceReading Lees(string json)
    {
        var connector = new EnphaseConnector(null!, null!, null!);
        using var doc = JsonDocument.Parse(json);
        return connector.LeesLokaal(doc.RootElement, DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public void De_meter_wint_van_de_omvormers_als_hij_er_is()
    {
        // Beide regels geven opwek; de eim-regel meet en de inverters-regel telt op wat de
        // micro-omvormers zelf melden. De gemeten waarde is nauwkeuriger.
        Assert.Equal(2143.5, Lees(MetMeters).PvPowerW);
    }

    [Fact]
    public void Zonder_meter_vallen_we_terug_op_de_omvormers()
    {
        // Een installatie zonder CT-klemmen is geen storing; dan is er alleen opwek.
        var lezing = Lees(ZonderMeters);

        Assert.Equal(1500, lezing.PvPowerW);
        Assert.Equal(0, lezing.LoadPowerW);
        Assert.Equal(0, lezing.GridPowerW);
        Assert.Null(lezing.ImportEnergyWh);
    }

    [Fact]
    public void Huisverbruik_en_netkant_komen_uit_de_juiste_regel()
    {
        // De valkuil: allebei heten ze "eim". Pakken we de eerste de beste, dan staat het
        // huisverbruik op de plek van de netkant en klopt elke besparing daarna niet meer.
        var lezing = Lees(MetMeters);

        Assert.Equal(850.25, lezing.LoadPowerW);
        Assert.Equal(-1293.25, lezing.GridPowerW);
    }

    [Fact]
    public void Teruglevering_blijft_negatief()
    {
        // De gateway rekent teruglevering negatief op de netkant, net als onze eigen afspraak. Zou
        // iemand hier een absolute waarde van maken, dan wordt teruglevering stilletjes afname.
        Assert.True(Lees(MetMeters).GridPowerW < 0);
    }

    [Fact]
    public void De_tellers_komen_mee_voor_de_lange_termijn()
    {
        var lezing = Lees(MetMeters);

        Assert.Equal(9123456.7, lezing.PvEnergyWh);
        Assert.Equal(4000000, lezing.ImportEnergyWh);
        Assert.Equal(6000000, lezing.ExportEnergyWh);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""{ "production": "stuk" }""")]
    public void Onzin_levert_een_lege_meting_op_en_geen_uitzondering(string json)
    {
        // De app stuurt door wat de gateway zegt. Een firmware-update die de vorm verandert mag geen
        // halve nacht aan foutmeldingen opleveren; nul is dan het eerlijke antwoord.
        var lezing = Lees(json);

        Assert.Equal(0, lezing.PvPowerW);
        Assert.Equal(0, lezing.GridPowerW);
    }

    [Fact]
    public void Een_gateway_in_huis_laten_we_niet_door_de_server_pollen()
    {
        var connector = new EnphaseConnector(null!, null!, null!);

        Assert.True(connector.KanDoorDeAppGelezenWorden);
        Assert.True(connector.IsLokaalVoor(new Dictionary<string, string> { ["host"] = "192.168.1.30" }));
        Assert.False(connector.IsLokaalVoor(new Dictionary<string, string> { ["host"] = "api.enphaseenergy.com" }));
    }
}
