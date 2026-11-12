using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Wattzicht.Api.Connectors;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests.Connectors;

/// <summary>
/// Peblar lokale API: koppelen, uitlezen en de foutpaden, tegen een nep-Peblar.
///
/// Wat hier bewust getest wordt, is niet "leest hij een getal". Het zijn de drie dingen die bij een
/// lokale laadpaal echt misgaan: een token dat ongeldig is geworden doordat de eigenaar een nieuw
/// token aanmaakte, firmware zonder lokale API, en een uitvoering zonder ingebouwde meter. Alle drie
/// horen een zin op te leveren waar iemand zelf iets mee kan, niet "401" of "niet gevonden".
///
/// De veldnamen komen uit Peblars ontwikkelaarspagina en niet uit een meting aan een echt apparaat,
/// dus leest de connector meerdere schrijfwijzen. Dat staat hier ook als test, want anders sneuvelt
/// die soepelheid bij de eerste opruimbeurt en merkt niemand het tot er een klant stilvalt.
/// </summary>
public sealed class PeblarScenarioTests
{
    private static readonly Dictionary<string, string> Creds =
        new() { ["host"] = "192.168.1.42", ["token"] = "geheim-token" };

    private const string Systeem = """
        {"ProductSerialNumber":"PBLR-0069938","ProductPartNumber":"1P-22-B","ProductModelName":"Business","FirmwareVersion":"1.8.2"}
        """;

    private const string Meter = """
        {"EnergyTotal":1234500,"EnergySession":4200,"PowerTotal":7360,"CurrentPhase1":16}
        """;

    private static PeblarConnector Maak(FakeVendor vendor) =>
        new(vendor.Factory(), NullLogger<PeblarConnector>.Instance);

    private static Task<ConnectResult> Koppel(PeblarConnector c, Dictionary<string, string>? creds = null) =>
        c.ConnectAsync(new ConnectRequest(DeviceBrand.Peblar, DeviceType.Charger, creds ?? Creds), default);

    [Fact]
    public async Task Koppelen_vindt_de_paal_op_serienummer_en_model()
    {
        var vendor = new FakeVendor().Json("/api/v1/system", Systeem).Json("/api/v1/meter", Meter);

        var result = await Koppel(Maak(vendor));

        Assert.True(result.Success, result.Error);
        var paal = Assert.Single(result.Devices);
        Assert.Equal("PBLR-0069938", paal.ExternalId);
        Assert.Equal(DeviceType.Charger, paal.Type);
        Assert.Contains("Business", paal.Name);
    }

    [Fact]
    public async Task Het_token_gaat_kaal_mee_zonder_Bearer()
    {
        // Peblar wil het token zonder schema. Met "Bearer " ervoor weigert de paal, en dat kost een
        // klant een avond zoeken naar een token dat gewoon klopt.
        var vendor = new FakeVendor().Json("/api/v1/system", Systeem).Json("/api/v1/meter", Meter);

        await Koppel(Maak(vendor));

        var verzoek = vendor.Requests[0];
        Assert.Equal("http://192.168.1.42/api/v1/system", verzoek.Uri.ToString());
        Assert.Equal("geheim-token", verzoek.Headers["Authorization"]);
    }

    [Fact]
    public async Task Zonder_adres_of_token_vraagt_hij_erom_in_plaats_van_te_proberen()
    {
        var vendor = new FakeVendor();

        var result = await Koppel(Maak(vendor), new Dictionary<string, string> { ["host"] = "192.168.1.42" });

        Assert.False(result.Success);
        Assert.Contains("token", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(vendor.Requests);
    }

    [Fact]
    public async Task Een_ongeldig_token_legt_uit_dat_een_nieuw_token_het_oude_ongeldig_maakt()
    {
        var vendor = new FakeVendor().Status("/api/v1/system", HttpStatusCode.Unauthorized);

        var result = await Koppel(Maak(vendor));

        Assert.False(result.Success);
        Assert.Contains("nieuw API-token", result.Error);
    }

    [Fact]
    public async Task Firmware_zonder_lokale_API_wijst_naar_bijwerken()
    {
        var vendor = new FakeVendor().Status("/api/v1/system", HttpStatusCode.NotFound);

        var result = await Koppel(Maak(vendor));

        Assert.False(result.Success);
        Assert.Contains("firmware", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Een_paal_zonder_ingebouwde_meter_strandt_bij_het_koppelen()
    {
        // Niet pas over een maand, als iemand zich afvraagt waarom zijn grafiek leeg blijft.
        var vendor = new FakeVendor()
            .Json("/api/v1/system", Systeem)
            .Json("/api/v1/meter", """{"PowerTotal":0,"CurrentPhase1":0}""");

        var result = await Koppel(Maak(vendor));

        Assert.False(result.Success);
        Assert.Contains("meterstand", result.Error);
    }

    [Fact]
    public async Task Een_paal_die_niet_opneemt_zegt_wat_je_kunt_nakijken()
    {
        var vendor = new FakeVendor().Unreachable("/api/v1/");

        var result = await Koppel(Maak(vendor));

        Assert.False(result.Success);
        Assert.Contains("lokale API", result.Error);
    }

    [Fact]
    public async Task Uitlezen_geeft_stand_in_Wh_en_vermogen_in_watt()
    {
        var vendor = new FakeVendor().Json("/api/v1/meter", Meter);
        var apparaat = new Device { Id = Guid.NewGuid(), Brand = DeviceBrand.Peblar, Type = DeviceType.Charger };

        var meting = await Maak(vendor).ReadAsync(apparaat, new Dictionary<string, string>(Creds), default);

        Assert.Equal(1234500, meting.ChargerEnergyWh);
        Assert.Equal(7360, meting.ChargerPowerW);
    }

    [Fact]
    public void Een_stand_in_kWh_wordt_omgerekend_naar_Wh()
    {
        var rauw = JsonDocument.Parse("""{"EnergyTotalKwh":1234.5,"PowerTotal":7360}""").RootElement;

        var meting = PeblarConnector.Parse(rauw);

        Assert.Equal(1234500, meting.ChargerEnergyWh);
    }

    [Theory]
    [InlineData("""{"energy_total":1234500}""")]
    [InlineData("""{"energyTotal":1234500}""")]
    [InlineData("""{"TotalEnergy":1234500}""")]
    public void De_stand_wordt_onder_meerdere_schrijfwijzen_herkend(string json)
    {
        // Zolang er geen opgenomen antwoord van een echt apparaat ligt, is dit de verzekering: een
        // firmware die het veld anders spelt laat de koppeling niet stilvallen.
        var meting = PeblarConnector.Parse(JsonDocument.Parse(json).RootElement);

        Assert.Equal(1234500, meting.ChargerEnergyWh);
    }

    [Fact]
    public void Een_stand_van_nul_telt_niet_als_meting()
    {
        // Nul is hier geen stand maar een paal die nog niets geleverd heeft. Zouden we die wel
        // bewaren, dan leest de sprong naar de eerste echte stand later als verbruik dat er nooit was.
        var meting = PeblarConnector.Parse(JsonDocument.Parse("""{"EnergyTotal":0,"PowerTotal":0}""").RootElement);

        Assert.Null(meting.ChargerEnergyWh);
    }

    [Fact]
    public void Een_paal_in_huis_wordt_door_de_app_gelezen_en_niet_door_de_server()
    {
        var connector = Maak(new FakeVendor());

        Assert.True(connector.IsLokaalVoor(Creds));
        Assert.True(connector.KanDoorDeAppGelezenWorden);
        Assert.False(connector.IsLokaalVoor(new Dictionary<string, string> { ["host"] = "peblar.ergens.nl" }));
    }

    [Fact]
    public void De_app_mag_een_rauw_antwoord_aanleveren_met_zijn_eigen_tijdstip()
    {
        var gemeten = new DateTimeOffset(2026, 11, 12, 9, 30, 0, TimeSpan.Zero);

        var meting = Maak(new FakeVendor()).LeesLokaal(JsonDocument.Parse(Meter).RootElement, gemeten);

        Assert.Equal(gemeten, meting.Timestamp);
        Assert.Equal(1234500, meting.ChargerEnergyWh);
    }
}
