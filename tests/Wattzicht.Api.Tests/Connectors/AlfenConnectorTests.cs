using System.Text.Json;
using Wattzicht.Api.Connectors;
using Wattzicht.Shared;
using Xunit;

namespace Wattzicht.Api.Tests.Connectors;

/// <summary>
/// De Alfen-laadpaal over het thuisnetwerk.
///
/// Wat hier getest wordt is het omrekenen van wat de paal teruggeeft, want daar zit de fout die je
/// niet ziet: de paal geeft zijn meterstand in kWh en wij bewaren tellers in Wh. Factor duizend mis
/// en de besparing klopt een jaar lang niet, zonder dat er ooit een foutmelding komt.
///
/// En het onderscheid lokaal/niet-lokaal, want daar hangt aan vast of onze server het zelf probeert.
/// Een paal op 192.168.x.x is vanaf de server onbereikbaar; dat elke vijf minuten proberen levert
/// alleen mislukte pogingen op.
/// </summary>
public class AlfenConnectorTests
{
    private static JsonElement Antwoord(params (string Id, object Waarde)[] eigenschappen)
    {
        var dict = eigenschappen.ToDictionary(x => x.Id, x => x.Waarde);
        return JsonSerializer.SerializeToDocument(dict).RootElement.Clone();
    }

    [Fact]
    public void Meterstand_in_kwh_wordt_wh()
    {
        var meting = AlfenConnector.Parse(Antwoord(("2221_22", 1704.69)));

        Assert.NotNull(meting.ChargerEnergyWh);
        Assert.Equal(1_704_690, meting.ChargerEnergyWh!.Value, 0);
    }

    [Fact]
    public void Twee_sockets_worden_bij_elkaar_opgeteld()
    {
        // Een paal met twee aansluitingen geeft twee standen. Voor de besparing telt alles wat er uit
        // die paal is gegaan, dus allebei.
        var meting = AlfenConnector.Parse(Antwoord(("2221_22", 1000.0), ("3221_22", 500.5)));

        Assert.Equal(1_500_500, meting.ChargerEnergyWh!.Value, 0);
    }

    [Fact]
    public void Het_actieve_vermogen_komt_in_watt_binnen()
    {
        var meting = AlfenConnector.Parse(Antwoord(("2221_22", 10.0), ("2221_16", 7360.0)));

        Assert.Equal(7360, meting.ChargerPowerW, 0);
    }

    [Fact]
    public void Een_stand_als_tekst_wordt_ook_gelezen()
    {
        // Sommige firmwares zetten getallen tussen aanhalingstekens. Dat mag geen nul opleveren,
        // want een teller die terugvalt naar nul leest als "alles is weg".
        var meting = AlfenConnector.Parse(Antwoord(("2221_22", "1704.69")));

        Assert.Equal(1_704_690, meting.ChargerEnergyWh!.Value, 0);
    }

    [Fact]
    public void Zonder_meterstand_blijft_de_teller_leeg_en_wordt_het_geen_nul()
    {
        // Nul zou betekenen "er is nog nooit geladen" en dat is iets anders dan "we weten het niet".
        var meting = AlfenConnector.Parse(Antwoord(("2221_16", 0.0)));

        Assert.Null(meting.ChargerEnergyWh);
    }

    [Theory]
    [InlineData("192.168.1.42", true)]
    [InlineData("10.0.0.15", true)]
    [InlineData("172.16.3.9", true)]
    [InlineData("alfen.local", true)]
    [InlineData("laadpaal.example.com", false)]
    [InlineData("82.192.69.90", false)]
    public void Een_paal_in_huis_leest_de_app_uit_en_niet_de_server(string host, bool lokaal)
    {
        var connector = new AlfenConnector(new FakeVendor().Factory(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AlfenConnector>.Instance);

        Assert.Equal(lokaal, connector.IsLokaalVoor(new Dictionary<string, string> { ["host"] = host }));
    }

    [Fact]
    public void De_app_mag_een_alfen_altijd_aanleveren()
    {
        var connector = new AlfenConnector(new FakeVendor().Factory(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AlfenConnector>.Instance);

        Assert.True(connector.KanDoorDeAppGelezenWorden);
        Assert.Equal(DeviceBrand.Alfen, connector.Brand);
    }

    [Fact]
    public void De_meting_van_de_app_houdt_het_tijdstip_van_de_app()
    {
        // De app meet in huis en stuurt het later door. Zouden we hier de serverklok pakken, dan
        // schuift elke meting op naar het moment van doorsturen.
        var connector = new AlfenConnector(new FakeVendor().Factory(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AlfenConnector>.Instance);
        var gemeten = new DateTimeOffset(2026, 11, 6, 7, 30, 0, TimeSpan.Zero);

        var meting = connector.LeesLokaal(Antwoord(("2221_22", 12.5)), gemeten);

        Assert.Equal(gemeten, meting.Timestamp);
        Assert.Equal(12_500, meting.ChargerEnergyWh!.Value, 0);
    }
}
