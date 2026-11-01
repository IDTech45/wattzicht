using System.Text.Json;
using Wattzicht.Api.Connectors;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests;

/// <summary>
/// De P1-meter op de slimme meter.
///
/// Waarom dit er is: bij micro-omvormers en gewone string-omvormers weten we alleen wat het dak
/// maakt en niet wat het huis van het net haalt. Zonder dat tweede valt er geen besparing uit te
/// rekenen, en daar is de app voor. Onze server kan zo'n kastje niet bereiken, dus de app leest uit
/// en stuurt het antwoord door; de omzetting staat hier zodat een oudere app geen eigen uitleg van
/// de velden kan meesturen.
/// </summary>
public sealed class P1MeterTests
{
    private static readonly DateTimeOffset Nu = new(2027, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static DeviceReading Lees(string json) =>
        HomeWizardConnector.LeesP1(JsonDocument.Parse(json).RootElement, Nu);

    [Fact]
    public void Afname_is_positief_en_teruglevering_negatief()
    {
        // Dezelfde afspraak als bij de omvormers, dus het teken mag blijven zoals het binnenkomt.
        Assert.Equal(1234, Lees("""{ "active_power_w": 1234 }""").GridPowerW);
        Assert.Equal(-2100, Lees("""{ "active_power_w": -2100 }""").GridPowerW);
    }

    [Fact]
    public void De_tellers_komen_in_wattuur_binnen()
    {
        var meting = Lees("""
        { "active_power_w": 0, "total_power_import_kwh": 8123.456, "total_power_export_kwh": 4200.0 }
        """);

        Assert.Equal(8_123_456, meting.ImportEnergyWh);
        Assert.Equal(4_200_000, meting.ExportEnergyWh);
    }

    [Fact]
    public void Zonder_totaalteller_worden_dag_en_nachttarief_opgeteld()
    {
        // Oudere firmware geeft alleen t1 en t2. Wie dan de eerste pakt die hij vindt, laat de
        // nachtstroom van de rekening vallen; bij veel huizen is dat de helft.
        var meting = Lees("""
        {
          "total_power_import_t1_kwh": 3000.5,
          "total_power_import_t2_kwh": 2000.5,
          "total_power_export_t1_kwh": 1000.0,
          "total_power_export_t2_kwh": 500.0
        }
        """);

        Assert.Equal(5_001_000, meting.ImportEnergyWh);
        Assert.Equal(1_500_000, meting.ExportEnergyWh);
    }

    [Fact]
    public void Een_meter_zonder_tellers_geeft_leeg_in_plaats_van_nul()
    {
        var meting = Lees("""{ "active_power_w": 500 }""");

        Assert.Null(meting.ImportEnergyWh);
        Assert.Null(meting.ExportEnergyWh);
    }

    [Fact]
    public void Koppelen_doet_geen_netwerkverkeer_en_onthoudt_het_serienummer()
    {
        var connector = new HomeWizardConnector();

        var uitkomst = connector.ConnectAsync(new ConnectRequest(
            DeviceBrand.HomeWizard, DeviceType.P1Meter,
            new Dictionary<string, string> { ["host"] = "192.168.1.50", ["serial"] = "3c39e7", ["product"] = "P1 meter" }),
            CancellationToken.None).Result;

        Assert.True(uitkomst.Success);
        var apparaat = Assert.Single(uitkomst.Devices);
        Assert.Equal("3c39e7", apparaat.ExternalId);
        Assert.Equal(DeviceType.P1Meter, apparaat.Type);
    }

    [Fact]
    public void Zonder_adres_koppelt_hij_niet()
    {
        var connector = new HomeWizardConnector();

        var uitkomst = connector.ConnectAsync(new ConnectRequest(
            DeviceBrand.HomeWizard, DeviceType.P1Meter, new Dictionary<string, string>()),
            CancellationToken.None).Result;

        Assert.False(uitkomst.Success);
        Assert.Contains("IP-adres", uitkomst.Error);
    }

    [Fact]
    public void De_server_probeert_zo_n_meter_niet_zelf_uit_te_lezen()
    {
        // Dit is de kern: 192.168.1.50 bestaat voor onze server niet. Zou de poller het toch proberen,
        // dan stond er bij elke ronde een storing bij een meter die prima werkt.
        var connector = new HomeWizardConnector();

        Assert.True(connector.IsLokaal);
    }
}
