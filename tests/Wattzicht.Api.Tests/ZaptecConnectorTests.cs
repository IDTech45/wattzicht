using System.Text.Json;
using Wattzicht.Api.Connectors;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Het antwoord van Zaptec op /api/chargers/{id}/state omzetten naar een meting.
///
/// Zaptec stuurt daar geen keurig object maar een lijst observaties met nummers erbij. Twee daarvan
/// lijken op elkaar en dat is precies de valkuil: 553 is de lopende laadbeurt en springt bij elke
/// nieuwe beurt terug naar nul, 554 is de meterstand van de paal en loopt door. Wie 553 als teller
/// gebruikt krijgt bij het verschil nemen negatieve kilowatturen zodra er een nieuwe beurt begint.
/// </summary>
public sealed class ZaptecConnectorTests
{
    private static readonly DateTimeOffset Nu = new(2027, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static JsonElement Antwoord(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Een_ladende_paal_geeft_vermogen_en_meterstand()
    {
        var meting = ZaptecConnector.LeesStand(Antwoord("""
        [
          { "StateId": 513, "ValueAsString": "7360.0" },
          { "StateId": 553, "ValueAsString": "12.4" },
          { "StateId": 554, "ValueAsString": "1834.56" }
        ]
        """), Nu);

        Assert.Equal(7360, meting.ChargerPowerW);
        Assert.Equal(7360, meting.LoadPowerW);
        Assert.Equal(1_834_560, meting.ChargerEnergyWh);
    }

    [Fact]
    public void De_teller_komt_van_de_meterstand_en_niet_van_de_lopende_beurt()
    {
        // 553 staat hier hoger dan 554 zou zijn als je ze zou verwisselen; dan valt het meteen op.
        var meting = ZaptecConnector.LeesStand(Antwoord("""
        [
          { "StateId": 553, "ValueAsString": "9999" },
          { "StateId": 554, "ValueAsString": "40.0" }
        ]
        """), Nu);

        Assert.Equal(40_000, meting.ChargerEnergyWh);
    }

    [Fact]
    public void Een_paal_die_niets_doet_geeft_nul_watt()
    {
        var meting = ZaptecConnector.LeesStand(Antwoord("""
        [ { "StateId": 554, "ValueAsString": "1834.56" } ]
        """), Nu);

        Assert.Equal(0, meting.ChargerPowerW);
        Assert.Equal(1_834_560, meting.ChargerEnergyWh);
    }

    [Fact]
    public void Een_antwoord_in_een_Data_omhulsel_werkt_ook()
    {
        var meting = ZaptecConnector.LeesStand(Antwoord("""
        { "Data": [ { "StateId": 513, "ValueAsString": "3680" } ] }
        """), Nu);

        Assert.Equal(3680, meting.ChargerPowerW);
    }

    [Fact]
    public void Zonder_meterstand_blijft_de_teller_leeg_in_plaats_van_nul()
    {
        // Nul zou in de grafiek een teller zijn die naar beneden klapt; leeg betekent "niet gemeten".
        var meting = ZaptecConnector.LeesStand(Antwoord("""
        [ { "StateId": 513, "ValueAsString": "1500" } ]
        """), Nu);

        Assert.Null(meting.ChargerEnergyWh);
    }

    [Fact]
    public void Onzin_in_een_waarde_laat_de_rest_staan()
    {
        var meting = ZaptecConnector.LeesStand(Antwoord("""
        [
          { "StateId": 513, "ValueAsString": "n/a" },
          { "StateId": 554, "ValueAsString": "100.5" }
        ]
        """), Nu);

        Assert.Equal(0, meting.ChargerPowerW);
        Assert.Equal(100_500, meting.ChargerEnergyWh);
    }
}
