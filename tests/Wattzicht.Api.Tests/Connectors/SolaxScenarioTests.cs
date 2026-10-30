using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Wattzicht.Api.Connectors;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests.Connectors;

/// <summary>
/// SolaXCloud: omvormer, batterij en laadpaal, koppelen en uitlezen en alle foutpaden, tegen een nep-SolaXCloud.
/// Antwoordvorm volgens de SolaXCloud User Monitoring API (success/exception/result) en de charger-API (code/result).
/// </summary>
public sealed class SolaxScenarioTests
{
    private const string Token = "20201012345678901234567";
    private static readonly Dictionary<string, string> Creds = new() { ["token"] = Token, ["sn"] = "SABCDEFGHI" };

    private static (SolaxConnector Connector, FakeVendor Vendor) Create(Action<FakeVendor> setup)
    {
        var vendor = new FakeVendor();
        setup(vendor);
        return (new SolaxConnector(vendor.Factory(SolaxConnector.DefaultBaseUrl), NullLogger<SolaxConnector>.Instance), vendor);
    }

    private static string Realtime(string result) => "{\"success\":true,\"exception\":\"Query success!\",\"result\":{" + result + "}}";

    private static Task<ConnectResult> Connect(SolaxConnector c, DeviceType type = DeviceType.Inverter, Dictionary<string, string>? creds = null) =>
        c.ConnectAsync(new ConnectRequest(DeviceBrand.Solax, type, creds ?? Creds), default);

    private static Device Dev(DeviceType type, string sn = "SABCDEFGHI") => new() { ExternalId = sn, Type = type, Brand = DeviceBrand.Solax };

    // --- Omvormer koppelen ------------------------------------------------------

    [Fact]
    public async Task Koppelen_stuurt_token_en_dongle_naar_de_realtime_API()
    {
        var (c, vendor) = Create(v => v.Json("getRealtimeInfo.do", Realtime("""
            "inverterSN":"X1BOOST0001","sn":"SABCDEFGHI","acpower":1500,"inverterType":"4","uploadTime":"2026-10-28 12:00:00"
            """)));

        var result = await Connect(c);

        var req = Assert.Single(vendor.Requests);
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.Equal("https://www.solaxcloud.com/proxyApp/proxy/api/getRealtimeInfo.do?tokenId=" + Token + "&sn=SABCDEFGHI", req.Uri.ToString());

        // Stringomvormer zonder batterij: alleen de omvormer.
        Assert.True(result.Success, result.Error);
        var dev = Assert.Single(result.Devices);
        Assert.Equal(DeviceType.Inverter, dev.Type);
        Assert.Equal("X1BOOST0001", dev.ExtraCredentials!["inverter_sn"]);
    }

    [Fact]
    public async Task Koppelen_zonder_token_of_dongle_vraagt_erom_zonder_netwerkverkeer()
    {
        var (c, vendor) = Create(_ => { });

        var noToken = await Connect(c, creds: new() { ["sn"] = "SABCDEFGHI" });
        var noSn = await Connect(c, creds: new() { ["token"] = Token });

        Assert.Contains("TokenID", noToken.Error);
        Assert.Contains("registratienummer", noSn.Error);
        Assert.Empty(vendor.Requests);
    }

    [Theory]
    [InlineData("tokenId is error", "TokenID")]
    [InlineData("Query failed: sn not exist", "registratienummer")]
    [InlineData("Request calls within the current minute > threshold", "limiet")]
    [InlineData("exceed the maximum call frequency", "limiet")]
    [InlineData("iets nieuws", "SolaXCloud: iets nieuws")]
    public async Task Fout_van_SolaX_wordt_een_Nederlandse_melding(string exception, string expected)
    {
        var (c, _) = Create(v => v.Json("getRealtimeInfo.do", $$"""{"success":false,"exception":"{{exception}}","result":null}"""));

        var result = await Connect(c);

        Assert.False(result.Success);
        Assert.Contains(expected, result.Error);
    }

    [Theory]
    [InlineData("429")]
    [InlineData("500")]
    [InlineData("json")]
    [InlineData("timeout")]
    [InlineData("offline")]
    [InlineData("result-leeg")]
    public async Task Storing_bij_koppelen_geeft_nette_melding(string storing)
    {
        var (c, _) = Create(v => Storing(v, "getRealtimeInfo.do", storing));

        var result = await Connect(c);

        Assert.False(result.Success);
        Assert.Contains("SolaXCloud", result.Error);
    }

    [Theory]
    [InlineData("429")]
    [InlineData("500")]
    [InlineData("json")]
    [InlineData("timeout")]
    [InlineData("offline")]
    [InlineData("result-leeg")]
    public async Task Storing_bij_uitlezen_is_altijd_een_ConnectorException(string storing)
    {
        var (c, _) = Create(v => Storing(v, "getRealtimeInfo.do", storing));

        var ex = await Assert.ThrowsAsync<ConnectorException>(() => c.ReadAsync(Dev(DeviceType.Inverter), Creds, default));
        Assert.Contains("SolaXCloud", ex.Message);
    }

    private static void Storing(FakeVendor v, string path, string storing)
    {
        switch (storing)
        {
            case "429": v.Status(path, (HttpStatusCode)429); break;
            case "500": v.Status(path, HttpStatusCode.InternalServerError); break;
            case "json": v.Json(path, "<html>Bad gateway</html>"); break;
            case "timeout": v.Timeout(path); break;
            case "offline": v.Unreachable(path); break;
            default: v.Json(path, """{"success":true,"exception":"Query success!","result":null}"""); break;
        }
    }

    // --- Omvormer uitlezen ----------------------------------------------------

    [Fact]
    public async Task Stringomvormer_zonder_DC_waarden_neemt_AC_als_zonvermogen()
    {
        var (c, _) = Create(v => v.Json("getRealtimeInfo.do", Realtime("\"acpower\":2000,\"feedinpower\":1200,\"powerdc1\":0,\"powerdc2\":0")));

        var r = await c.ReadAsync(Dev(DeviceType.Inverter), Creds, default);

        Assert.Equal(2000, r.PvPowerW);
        Assert.Equal(-1200, r.GridPowerW);
        Assert.Equal(800, r.LoadPowerW);
    }

    [Fact]
    public async Task S_avonds_afname_van_het_net_is_positief()
    {
        var (c, _) = Create(v => v.Json("getRealtimeInfo.do", Realtime("\"acpower\":0,\"feedinpower\":-650,\"soc\":20,\"batPower\":0")));

        var r = await c.ReadAsync(Dev(DeviceType.Inverter), Creds, default);

        Assert.Equal(0, r.PvPowerW);
        Assert.Equal(650, r.GridPowerW);
        Assert.Equal(650, r.LoadPowerW);
    }

    [Fact]
    public async Task Batterij_laden_is_negatief_en_heeft_geen_zon_of_net()
    {
        var (c, _) = Create(v => v.Json("getRealtimeInfo.do", Realtime("\"acpower\":3000,\"feedinpower\":500,\"soc\":81,\"batPower\":1800,\"powerdc1\":5300")));

        var r = await c.ReadAsync(Dev(DeviceType.Battery), Creds, default);

        Assert.Equal(-1800, r.BatteryPowerW);
        Assert.Equal(81, r.BatterySoc);
        Assert.Equal(0, r.PvPowerW);
        Assert.Equal(0, r.GridPowerW);
        Assert.Equal(0, r.LoadPowerW);
    }

    [Theory]
    [InlineData("2026-07-01 14:00:00", 12)] // zomertijd
    [InlineData("2026-01-15 14:00:00", 13)] // wintertijd
    public async Task UploadTime_is_Nederlandse_tijd(string uploadTime, int utcHour)
    {
        var (c, _) = Create(v => v.Json("getRealtimeInfo.do", Realtime($"\"acpower\":100,\"uploadTime\":\"{uploadTime}\"")));

        var r = await c.ReadAsync(Dev(DeviceType.Inverter), Creds, default);

        Assert.Equal(utcHour, r.Timestamp.UtcDateTime.Hour);
    }

    // --- Laadpaal -------------------------------------------------------------

    private static readonly Dictionary<string, string> ChargerCreds = new() { ["token"] = Token, ["charger_sn"] = "C3110000001" };

    private const string ChargerCharging = """
        {"success":true,"code":0,"exception":"Query success!","result":{"sn":"C3110000001","deviceState":2,"chargePower":7400,"chargeEnergyTotal":321.5}}
        """;

    [Fact]
    public async Task Laadpaal_koppelen_stuurt_POST_met_tokenId_header_en_serienummer()
    {
        var (c, vendor) = Create(v => v.Json("api/v2/charger/getInfo", ChargerCharging, method: HttpMethod.Post));

        var result = await Connect(c, DeviceType.Charger, ChargerCreds);

        Assert.True(result.Success, result.Error);
        var dev = Assert.Single(result.Devices);
        Assert.Equal(DeviceType.Charger, dev.Type);
        Assert.Equal("C3110000001", dev.ExternalId);

        var req = Assert.Single(vendor.Requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal(Token, req.Headers["tokenId"]);
        Assert.Contains("\"sn\":\"C3110000001\"", req.Body);
        Assert.DoesNotContain(Token, req.Uri.ToString());
    }

    [Fact]
    public async Task Laadpaal_valt_terug_op_GET_als_POST_niet_mag()
    {
        var (c, vendor) = Create(v => v
            .Status("api/v2/charger/getInfo", HttpStatusCode.MethodNotAllowed, HttpMethod.Post)
            .Json("api/v2/charger/getInfo?sn=C3110000001", ChargerCharging, method: HttpMethod.Get));

        var r = await c.ReadAsync(Dev(DeviceType.Charger, "C3110000001"), ChargerCreds, default);

        Assert.Equal(2, vendor.Requests.Count);
        Assert.Equal(Token, vendor.Requests[1].Headers["tokenId"]);
        Assert.Equal(7400, r.ChargerPowerW);
        Assert.Equal(321_500, r.ChargerEnergyWh);
    }

    [Theory]
    [InlineData(1001, "API-rechten")]
    [InlineData(1002, "serienummer")]
    [InlineData(1003, "hoort niet bij")]
    [InlineData(2002, "geen laadpaal")]
    public async Task Laadpaal_foutcodes_worden_uitgelegd(int code, string expected)
    {
        var (c, _) = Create(v => v.Json("api/v2/charger/getInfo", $$"""{"success":false,"code":{{code}},"exception":"x","result":null}"""));

        var result = await Connect(c, DeviceType.Charger, ChargerCreds);

        Assert.False(result.Success);
        Assert.Contains(expected, result.Error);
    }

    [Fact]
    public async Task Laadpaal_zonder_serienummer_vraagt_erom()
    {
        var (c, vendor) = Create(_ => { });

        var result = await Connect(c, DeviceType.Charger, new() { ["token"] = Token });

        Assert.Contains("serienummer van de laadpaal", result.Error);
        Assert.Empty(vendor.Requests);
    }

    [Theory]
    [InlineData("500")]
    [InlineData("json")]
    [InlineData("timeout")]
    [InlineData("offline")]
    public async Task Laadpaal_storing_bij_uitlezen_is_een_ConnectorException(string storing)
    {
        var (c, _) = Create(v => Storing(v, "api/v2/charger/getInfo", storing));

        await Assert.ThrowsAsync<ConnectorException>(() => c.ReadAsync(Dev(DeviceType.Charger, "C3110000001"), ChargerCreds, default));
    }

    [Fact]
    public async Task Laadpaal_die_niet_laadt_meldt_nul_ook_als_er_nog_stroom_blijft_hangen()
    {
        var (c, _) = Create(v => v.Json("api/v2/charger/getInfo", """
            {"success":true,"code":0,"result":{"deviceState":0,"chargePower":120,"currentA":0.5,"chargeEnergyTotal":10}}
            """));

        var r = await c.ReadAsync(Dev(DeviceType.Charger, "C3110000001"), ChargerCreds, default);

        Assert.Equal(0, r.ChargerPowerW);
        Assert.Equal(10_000, r.ChargerEnergyWh);
    }
}
