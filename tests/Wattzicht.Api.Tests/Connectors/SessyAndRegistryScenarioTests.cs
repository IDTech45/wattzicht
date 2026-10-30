using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Wattzicht.Api.Connectors;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests.Connectors;

/// <summary>Sessy lokale API: koppelen, uitlezen en foutpaden, tegen een nep-Sessy.</summary>
public sealed class SessyScenarioTests
{
    private static readonly Dictionary<string, string> Creds = new() { ["host"] = "192.168.1.50", ["username"] = "ABCD1234", ["password"] = "geheim" };

    private const string Status = """
        {"status":"ok","sessy":{"state_of_charge":0.8,"power":-900,"system_state":"SYSTEM_STATE_RUNNING_SAFE"},
         "renewable_energy_phase1":{"power_w":1500},"grid_phase1":{"power_w":-200}}
        """;

    private static (SessyConnector Connector, FakeVendor Vendor) Create(Action<FakeVendor> setup)
    {
        var vendor = new FakeVendor();
        setup(vendor);
        return (new SessyConnector(vendor.Factory(), NullLogger<SessyConnector>.Instance), vendor);
    }

    private static Task<ConnectResult> Connect(SessyConnector c, Dictionary<string, string>? creds = null) =>
        c.ConnectAsync(new ConnectRequest(DeviceBrand.Sessy, DeviceType.Battery, creds ?? Creds), default);

    [Fact]
    public async Task Koppelen_gebruikt_basic_auth_op_de_lokale_API()
    {
        var (c, vendor) = Create(v => v.Json("/api/v1/power/status", Status));

        var result = await Connect(c);

        Assert.True(result.Success, result.Error);
        var dev = Assert.Single(result.Devices);
        Assert.Equal(DeviceType.Battery, dev.Type);
        Assert.Equal("ABCD1234", dev.ExternalId); // geen serienummer in het antwoord: gebruikersnaam

        var req = Assert.Single(vendor.Requests);
        Assert.Equal("http://192.168.1.50/api/v1/power/status", req.Uri.ToString());
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("ABCD1234:geheim")), req.Headers["Authorization"]);
    }

    [Fact]
    public async Task Host_met_http_en_slash_wordt_netjes_gebruikt()
    {
        var (c, vendor) = Create(v => v.Json("/api/v1/power/status", Status));

        await Connect(c, new() { ["host"] = "http://sessy.local/", ["username"] = "u", ["password"] = "p" });

        Assert.Equal("http://sessy.local/api/v1/power/status", Assert.Single(vendor.Requests).Uri.ToString());
    }

    [Fact]
    public async Task Ontbrekende_gegevens_geen_netwerkverkeer()
    {
        var (c, vendor) = Create(_ => { });

        var result = await Connect(c, new() { ["host"] = "192.168.1.50" });

        Assert.False(result.Success);
        Assert.Contains("IP-adres", result.Error);
        Assert.Empty(vendor.Requests);
    }

    [Theory]
    [InlineData("401", "klopt niet")]
    [InlineData("500", "niet bereikbaar")]
    [InlineData("offline", "niet bereikbaar")]
    [InlineData("timeout", "time-out")]
    [InlineData("json", "anders dan een Sessy")]
    public async Task Storing_bij_koppelen_geeft_nette_melding(string storing, string expected)
    {
        var (c, _) = Create(v => Storing(v, storing));

        var result = await Connect(c);

        Assert.False(result.Success);
        Assert.Contains(expected, result.Error);
    }

    [Theory]
    [InlineData("401")]
    [InlineData("500")]
    [InlineData("offline")]
    [InlineData("timeout")]
    [InlineData("json")]
    public async Task Storing_bij_uitlezen_is_altijd_een_ConnectorException(string storing)
    {
        var (c, _) = Create(v => Storing(v, storing));
        var device = new Device { ExternalId = "ABCD1234", Type = DeviceType.Battery, Brand = DeviceBrand.Sessy };

        await Assert.ThrowsAsync<ConnectorException>(() => c.ReadAsync(device, Creds, default));
    }

    [Fact]
    public async Task Uitlezen_laden_is_negatief_en_soc_in_procenten()
    {
        var (c, _) = Create(v => v.Json("/api/v1/power/status", Status));
        var device = new Device { ExternalId = "ABCD1234", Type = DeviceType.Battery, Brand = DeviceBrand.Sessy };

        var r = await c.ReadAsync(device, Creds, default);

        Assert.Equal(-900, r.BatteryPowerW);
        Assert.Equal(80d, r.BatterySoc!.Value, 6);
        Assert.Equal(1500, r.PvPowerW);
        Assert.Equal(-200, r.GridPowerW);
        Assert.Equal(400, r.LoadPowerW); // 1500 − 900 − 200
    }

    private static void Storing(FakeVendor v, string storing)
    {
        const string path = "/api/v1/power/status";
        switch (storing)
        {
            case "401": v.Status(path, HttpStatusCode.Unauthorized); break;
            case "500": v.Status(path, HttpStatusCode.InternalServerError); break;
            case "offline": v.Unreachable(path); break;
            case "timeout": v.Timeout(path); break;
            default: v.Json(path, "<html>router login</html>"); break;
        }
    }
}

/// <summary>
/// Contract voor álle koppelingen samen, via de echte DI-registratie. Faalt als er een merk bijkomt
/// zonder dat het hier (en dus in de scenario-tests) wordt meegenomen.
/// </summary>
public sealed class ConnectorContractTests
{
    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddDeviceConnectors(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Precies_deze_merken_zijn_koppelbaar()
    {
        using var sp = Services();
        var registry = sp.GetRequiredService<ConnectorRegistry>();

        Assert.Equal(
            new[] { DeviceBrand.Growatt, DeviceBrand.Sessy, DeviceBrand.Solax, DeviceBrand.Demo }.OrderBy(b => b),
            registry.SupportedBrands.OrderBy(b => b));
    }

    [Fact]
    public void Niet_ondersteund_merk_geeft_een_duidelijke_fout()
    {
        using var sp = Services();
        var registry = sp.GetRequiredService<ConnectorRegistry>();

        Assert.Null(registry.Get(DeviceBrand.Enphase));
        var ex = Assert.Throws<ConnectorException>(() => registry.GetRequired(DeviceBrand.Enphase));
        Assert.Contains("nog niet ondersteund", ex.Message);
    }

    public static TheoryData<DeviceBrand> RealBrands => new() { DeviceBrand.Growatt, DeviceBrand.Solax, DeviceBrand.Sessy };

    [Theory]
    [MemberData(nameof(RealBrands))]
    public async Task Koppelen_zonder_gegevens_faalt_netjes_en_nooit_met_een_crash(DeviceBrand brand)
    {
        using var sp = Services();
        var connector = sp.GetRequiredService<ConnectorRegistry>().GetRequired(brand);

        foreach (var type in new[] { DeviceType.Inverter, DeviceType.Battery, DeviceType.Charger })
        {
            var result = await connector.ConnectAsync(new ConnectRequest(brand, type, new Dictionary<string, string>()), default);
            Assert.False(result.Success);
            Assert.False(string.IsNullOrWhiteSpace(result.Error));
            Assert.Empty(result.Devices);
        }
    }

    [Theory]
    [MemberData(nameof(RealBrands))]
    public async Task Uitlezen_zonder_gegevens_is_een_ConnectorException(DeviceBrand brand)
    {
        using var sp = Services();
        var connector = sp.GetRequiredService<ConnectorRegistry>().GetRequired(brand);
        var device = new Device { ExternalId = "X", Type = DeviceType.Inverter, Brand = brand };

        await Assert.ThrowsAsync<ConnectorException>(() => connector.ReadAsync(device, new Dictionary<string, string>(), default));
    }

    [Fact]
    public async Task Demo_koppelt_elk_type_en_levert_metingen()
    {
        using var sp = Services();
        var connector = sp.GetRequiredService<ConnectorRegistry>().GetRequired(DeviceBrand.Demo);

        foreach (var type in new[] { DeviceType.Inverter, DeviceType.Battery, DeviceType.Charger })
        {
            var result = await connector.ConnectAsync(new ConnectRequest(DeviceBrand.Demo, type, new Dictionary<string, string>()), default);
            var dev = Assert.Single(result.Devices);
            var reading = await connector.ReadAsync(new Device { ExternalId = dev.ExternalId, Type = type, Brand = DeviceBrand.Demo }, new Dictionary<string, string>(), default);
            Assert.True(reading.PvPowerW >= 0 && reading.ChargerPowerW >= 0);
        }
    }
}
