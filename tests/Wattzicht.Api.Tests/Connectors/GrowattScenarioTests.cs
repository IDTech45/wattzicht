using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Wattzicht.Api.Connectors;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests.Connectors;

/// <summary>
/// Growatt Open API v1: koppelen, uitlezen en alle foutpaden, tegen een nep-Growatt.
/// Antwoorden volgen de vorm uit de Growatt OpenAPI-documentatie (error_code/error_msg/data).
/// </summary>
public sealed class GrowattScenarioTests
{
    private static readonly Dictionary<string, string> Creds = new() { ["token"] = "test-token-growatt" };

    private const string Plants = """
        {"error_code":0,"error_msg":"","data":{"count":1,"plants":[{"plant_id":"1234","name":"Thuis","city":"Enschede"}]}}
        """;

    private const string DevicesTlxAndMix = """
        {"error_code":0,"error_msg":"","data":{"count":2,"devices":[
          {"device_sn":"TLX0000001","model":"MIN 3600TL-X","type":"7"},
          {"device_sn":"SPH0000001","model":"SPH 5000","type":"5"}]}}
        """;

    private static (GrowattConnector Connector, FakeVendor Vendor) Create(Action<FakeVendor> setup)
    {
        var vendor = new FakeVendor();
        setup(vendor);
        return (new GrowattConnector(vendor.Factory(GrowattConnector.DefaultBaseUrl), NullLogger<GrowattConnector>.Instance), vendor);
    }

    private static Task<ConnectResult> Connect(GrowattConnector c, Dictionary<string, string>? creds = null) =>
        c.ConnectAsync(new ConnectRequest(DeviceBrand.Growatt, DeviceType.Inverter, creds ?? Creds), default);

    // --- Koppelen -------------------------------------------------------------

    [Fact]
    public async Task Koppelen_vindt_omvormer_en_bij_SPH_ook_de_batterij()
    {
        var (c, vendor) = Create(v => v.Json("plant/list", Plants).Json("device/list", DevicesTlxAndMix));

        var result = await Connect(c);

        Assert.True(result.Success, result.Error);
        Assert.Equal(3, result.Devices.Count);
        var tlx = Assert.Single(result.Devices, d => d.ExternalId == "TLX0000001");
        Assert.Equal(DeviceType.Inverter, tlx.Type);
        Assert.Equal("tlx", tlx.ExtraCredentials!["device_kind"]);
        Assert.Equal("1234", tlx.ExtraCredentials["plant_id"]);
        Assert.Contains(result.Devices, d => d.ExternalId == "SPH0000001" && d.Type == DeviceType.Battery && d.ExtraCredentials!["device_kind"] == "mix");

        // Token gaat in de header 'token', nooit in de URL.
        Assert.All(vendor.Requests, r => Assert.Equal("test-token-growatt", r.Headers["token"]));
        Assert.All(vendor.Requests, r => Assert.DoesNotContain("test-token-growatt", r.Uri.ToString()));
        Assert.Contains(vendor.Requests, r => r.Uri.ToString().Contains("device/list?plant_id=1234"));
    }

    [Fact]
    public async Task Koppelen_zonder_token_vraagt_om_het_token_zonder_netwerkverkeer()
    {
        var (c, vendor) = Create(_ => { });

        var result = await Connect(c, new Dictionary<string, string>());

        Assert.False(result.Success);
        Assert.Contains("API-token", result.Error);
        Assert.Empty(vendor.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Koppelen_met_geweigerd_token_via_http_status(HttpStatusCode status)
    {
        var (c, _) = Create(v => v.Status("plant/list", status));

        var result = await Connect(c);

        Assert.False(result.Success);
        Assert.Equal("Growatt-token is ongeldig of verlopen.", result.Error);
    }

    [Theory]
    [InlineData(10011)]
    [InlineData(10012)]
    public async Task Koppelen_met_ongeldig_token_via_error_code_bij_http_200(int code)
    {
        var (c, _) = Create(v => v.Json("plant/list", $$"""{"error_code":{{code}},"error_msg":"error_permission_denied"}"""));

        var result = await Connect(c);

        Assert.False(result.Success);
        Assert.Equal("Growatt-token is ongeldig of verlopen.", result.Error);
    }

    [Fact]
    public async Task Andere_foutcode_geeft_de_melding_van_Growatt_door()
    {
        var (c, _) = Create(v => v.Json("plant/list", """{"error_code":10001,"error_msg":"system error"}"""));

        var result = await Connect(c);

        Assert.False(result.Success);
        Assert.Equal("Growatt meldt een fout: system error", result.Error);
    }

    [Fact]
    public async Task Account_zonder_installaties()
    {
        var (c, _) = Create(v => v.Json("plant/list", """{"error_code":0,"data":{"count":0,"plants":[]}}"""));

        var result = await Connect(c);

        Assert.False(result.Success);
        Assert.Contains("Geen installaties", result.Error);
    }

    [Fact]
    public async Task Installatie_zonder_omvormers()
    {
        var (c, _) = Create(v => v.Json("plant/list", Plants).Json("device/list", """{"error_code":0,"data":{"devices":[]}}"""));

        var result = await Connect(c);

        Assert.False(result.Success);
        Assert.Contains("Geen omvormers", result.Error);
    }

    [Theory]
    [InlineData("server")]
    [InlineData("json")]
    [InlineData("timeout")]
    [InlineData("offline")]
    public async Task Storing_bij_Growatt_geeft_nette_melding_en_geen_crash(string storing)
    {
        var (c, _) = Create(v =>
        {
            switch (storing)
            {
                case "server": v.Status("plant/list", HttpStatusCode.InternalServerError); break;
                case "json": v.Json("plant/list", "<html>onderhoud</html>"); break;
                case "timeout": v.Timeout("plant/list"); break;
                default: v.Unreachable("plant/list"); break;
            }
        });

        var result = await Connect(c);

        Assert.False(result.Success);
        Assert.Contains("Growatt", result.Error);
    }

    // --- Uitlezen -------------------------------------------------------------

    private static readonly Dictionary<string, string> TlxCreds = new() { ["token"] = "t", ["device_kind"] = "tlx", ["plant_id"] = "1234" };
    private static readonly Dictionary<string, string> MixCreds = new() { ["token"] = "t", ["device_kind"] = "mix", ["plant_id"] = "1234" };

    [Fact]
    public async Task TLX_terugleveren_wordt_negatief_net_en_kWh_wordt_Wh()
    {
        var (c, vendor) = Create(v => v.Json("tlx_last_data", """
            {"error_code":0,"data":{"ppv":3200,"pac":3100,"pactogrid":1800,"pactouser":0,"pLocalLoad":1300,
             "epvTotal":4567.8,"etoGridTotal":2100.5,"etoUserTotal":3300.25,"time":"2026-10-28 12:00:00"}}
            """));
        var device = new Device { ExternalId = "TLX0000001", Type = DeviceType.Inverter, Brand = DeviceBrand.Growatt };

        var r = await c.ReadAsync(device, TlxCreds, default);

        Assert.Contains(vendor.Requests, q => q.Uri.ToString().Contains("device/tlx/tlx_last_data?tlx_sn=TLX0000001"));
        Assert.Equal(3200, r.PvPowerW);
        Assert.Equal(-1800, r.GridPowerW);
        Assert.Equal(1300, r.LoadPowerW);
        Assert.Equal(0, r.BatteryPowerW);
        Assert.Null(r.BatterySoc);
        Assert.Equal(4_567_800, r.PvEnergyWh!.Value, 3);
        Assert.Equal(2_100_500, r.ExportEnergyWh!.Value, 3);
        Assert.Equal(3_300_250, r.ImportEnergyWh!.Value, 3);
        // 12:00 Nederlandse wintertijd = 11:00 UTC.
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 11, 0, 0, TimeSpan.Zero), r.Timestamp);
    }

    [Fact]
    public async Task TLX_zonder_huisverbruik_schat_verbruik_uit_de_balans()
    {
        var (c, _) = Create(v => v.Json("tlx_last_data", """
            {"error_code":0,"data":{"ppv":500,"pactogrid":0,"pactouser":700}}
            """));
        var device = new Device { ExternalId = "TLX0000001", Type = DeviceType.Inverter, Brand = DeviceBrand.Growatt };

        var r = await c.ReadAsync(device, TlxCreds, default);

        Assert.Equal(700, r.GridPowerW);
        Assert.Equal(1200, r.LoadPowerW); // 500 zon + 700 net
    }

    private const string MixData = """
        {"error_code":0,"data":{"ppv":2000,"pactogrid":0,"pactouser":300,"pLocalLoad":1100,
         "pCharge1":0,"pDischarge1":1200,"SOC":55,"echargeTotal":800,"edischargeTotal":750,"epvTotal":3000}}
        """;

    [Fact]
    public async Task SPH_omvormerrij_draagt_zon_en_net_batterijrij_alleen_de_batterij()
    {
        var (c, vendor) = Create(v => v.Json("mix_last_data", MixData));
        var inverter = new Device { ExternalId = "SPH0000001", Type = DeviceType.Inverter, Brand = DeviceBrand.Growatt };
        var battery = new Device { ExternalId = "SPH0000001", Type = DeviceType.Battery, Brand = DeviceBrand.Growatt };

        var inv = await c.ReadAsync(inverter, MixCreds, default);
        var bat = await c.ReadAsync(battery, MixCreds, default);

        Assert.Contains(vendor.Requests, q => q.Uri.ToString().Contains("device/mix/mix_last_data?mix_sn=SPH0000001"));

        Assert.Equal(2000, inv.PvPowerW);
        Assert.Equal(300, inv.GridPowerW);
        Assert.Equal(0, inv.BatteryPowerW);
        Assert.Null(inv.BatterySoc);

        // Geen dubbeltelling: de batterijrij heeft geen zon, net of verbruik.
        Assert.Equal(0, bat.PvPowerW);
        Assert.Equal(0, bat.GridPowerW);
        Assert.Equal(0, bat.LoadPowerW);
        Assert.Equal(1200, bat.BatteryPowerW);   // ontladen = positief
        Assert.Equal(55, bat.BatterySoc);
        Assert.Equal(800_000, bat.BatteryChargeEnergyWh!.Value, 3);
        Assert.Equal(750_000, bat.BatteryDischargeEnergyWh!.Value, 3);
    }

    [Fact]
    public async Task SPH_laden_is_negatief_batterijvermogen()
    {
        var (c, _) = Create(v => v.Json("mix_last_data", """{"error_code":0,"data":{"ppv":4000,"pCharge1":2500,"pDischarge1":0,"SOC":30}}"""));
        var battery = new Device { ExternalId = "SPH0000001", Type = DeviceType.Battery, Brand = DeviceBrand.Growatt };

        var r = await c.ReadAsync(battery, MixCreds, default);

        Assert.Equal(-2500, r.BatteryPowerW);
    }

    [Theory]
    [InlineData("server")]
    [InlineData("json")]
    [InlineData("timeout")]
    [InlineData("offline")]
    [InlineData("token")]
    public async Task Uitlezen_bij_storing_gooit_altijd_een_ConnectorException(string storing)
    {
        var (c, _) = Create(v =>
        {
            switch (storing)
            {
                case "server": v.Status("tlx_last_data", HttpStatusCode.BadGateway); break;
                case "json": v.Json("tlx_last_data", "{kapot"); break;
                case "timeout": v.Timeout("tlx_last_data"); break;
                case "offline": v.Unreachable("tlx_last_data"); break;
                default: v.Json("tlx_last_data", """{"error_code":10012,"error_msg":"token expired"}"""); break;
            }
        });
        var device = new Device { ExternalId = "TLX0000001", Type = DeviceType.Inverter, Brand = DeviceBrand.Growatt };

        var ex = await Assert.ThrowsAsync<ConnectorException>(() => c.ReadAsync(device, TlxCreds, default));
        Assert.Contains("Growatt", ex.Message);
    }

    [Fact]
    public async Task Uitlezen_zonder_token_gooit_meteen()
    {
        var (c, vendor) = Create(_ => { });
        var device = new Device { ExternalId = "TLX0000001", Type = DeviceType.Inverter, Brand = DeviceBrand.Growatt };

        await Assert.ThrowsAsync<ConnectorException>(() => c.ReadAsync(device, new Dictionary<string, string>(), default));
        Assert.Empty(vendor.Requests);
    }

    // --- Tijd -----------------------------------------------------------------

    [Theory]
    [InlineData("2026-07-01 14:00:00", 12)] // zomertijd: UTC+2
    [InlineData("2026-12-01 14:00:00", 13)] // wintertijd: UTC+1
    public void Growatt_tijd_is_Nederlandse_tijd(string raw, int expectedUtcHour)
    {
        using var doc = JsonDocument.Parse($$"""{"time":"{{raw}}"}""");
        var now = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var ts = GrowattConnector.ParseTimestamp(doc.RootElement, now);

        Assert.Equal(expectedUtcHour, ts.UtcDateTime.Hour);
    }

    [Fact]
    public void Tijd_in_de_toekomst_wordt_nu()
    {
        using var doc = JsonDocument.Parse("""{"time":"2030-01-01 00:00:00"}""");
        var now = new DateTimeOffset(2026, 10, 30, 11, 0, 0, TimeSpan.Zero);

        Assert.Equal(now, GrowattConnector.ParseTimestamp(doc.RootElement, now));
    }
}
