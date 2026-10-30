using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Wattzicht.Api.Connectors;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests;

public class SolaxConnectorTests
{
    private const string HybridJson = """
    {"success":true,"exception":"Query success!","result":{
      "inverterSN":"H34A10I1234567","sn":"SABCDEFGHI","acpower":2500,"yieldtoday":12.3,"yieldtotal":4567.8,
      "feedinpower":800,"feedinenergy":1234.56,"consumeenergy":2345.67,"soc":64,"batPower":-1200,
      "powerdc1":2000,"powerdc2":1500,"inverterType":"5","inverterStatus":"102","uploadTime":"2026-10-30 12:00:00"}}
    """;

    private static SolaxConnector Create(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new StubHandler(json, status);
        var factory = new StubFactory(new HttpClient(handler) { BaseAddress = new Uri(SolaxConnector.DefaultBaseUrl) });
        return new SolaxConnector(factory, NullLogger<SolaxConnector>.Instance);
    }

    private static readonly Dictionary<string, string> Creds = new() { ["token"] = "20201012345678901234567", ["sn"] = "SABCDEFGHI" };

    [Fact]
    public async Task Connect_hybrid_yields_inverter_and_battery()
    {
        var result = await Create(HybridJson).ConnectAsync(new ConnectRequest(DeviceBrand.Solax, DeviceType.Inverter, Creds), default);

        Assert.True(result.Success);
        Assert.Equal(2, result.Devices.Count);
        Assert.Contains(result.Devices, d => d.Type == DeviceType.Battery);
    }

    [Fact]
    public async Task Inverter_reading_flips_grid_sign_and_sums_strings()
    {
        var device = new Device { ExternalId = "SABCDEFGHI", Type = DeviceType.Inverter, Brand = DeviceBrand.Solax };
        var r = await Create(HybridJson).ReadAsync(device, Creds, default);

        Assert.Equal(3500, r.PvPowerW);
        Assert.Equal(-800, r.GridPowerW);          // 800 W teruglevering
        Assert.Equal(1700, r.LoadPowerW);          // 2500 AC - 800 export
        Assert.Equal(4_567_800, r.PvEnergyWh);
        Assert.Equal(0, r.BatteryPowerW);          // batterij staat op eigen rij
    }

    [Fact]
    public async Task Battery_reading_flips_sign()
    {
        var device = new Device { ExternalId = "SABCDEFGHI", Type = DeviceType.Battery, Brand = DeviceBrand.Solax };
        var r = await Create(HybridJson).ReadAsync(device, Creds, default);

        Assert.Equal(1200, r.BatteryPowerW);       // SolaX -1200 (ontladen) => Wattzicht +1200
        Assert.Equal(64, r.BatterySoc);
        Assert.Equal(0, r.PvPowerW);
    }

    [Fact]
    public async Task Bad_token_gives_dutch_error()
    {
        var result = await Create("""{"success":false,"exception":"tokenId is error","result":null}""")
            .ConnectAsync(new ConnectRequest(DeviceBrand.Solax, DeviceType.Inverter, Creds), default);

        Assert.False(result.Success);
        Assert.Contains("TokenID", result.Error);
    }

    private sealed class StubHandler(string json, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }

    private sealed class StubFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}

public class SolaxChargerParseTests
{
    [Fact]
    public void Charging_three_phase_estimates_power_from_currents()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""
            {"deviceState":2,"deviceMode":1,"currentA":16,"currentB":16,"currentC":16,"chargeEnergyTotal":1234.5,"sn":"C3110000001"}
            """);
        var r = Wattzicht.Api.Connectors.SolaxConnector.ParseCharger(doc.RootElement, DateTimeOffset.UtcNow);

        Assert.Equal(11040, r.ChargerPowerW);      // 3 × 16 A × 230 V
        Assert.Equal(1_234_500, r.ChargerEnergyWh);
    }

    [Fact]
    public void Not_charging_gives_zero_power()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""
            {"deviceState":0,"currentA":0.4,"chargeEnergyTotal":10}
            """);
        var r = Wattzicht.Api.Connectors.SolaxConnector.ParseCharger(doc.RootElement, DateTimeOffset.UtcNow);

        Assert.Equal(0, r.ChargerPowerW);
    }
}
