using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Wattzicht.Api.Connectors;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests.Connectors;

/// <summary>
/// Enphase koppelen en uitlezen, met nadruk op de valkuil die deze koppeling kapot zou maken.
///
/// Enphase geeft bij elke token-vernieuwing een nieuw refresh-token en maakt het oude ongeldig.
/// Bewaren we dat niet, dan werkt de koppeling precies één keer en daarna nooit meer, zonder dat er
/// iets opvallends gebeurt: je ziet alleen "verbind opnieuw" terug bij een klant die niets fout deed.
///
/// Het tweede dat hier vastligt: deze connector vult alleen de zonkant. Afname van en teruglevering
/// aan het net zitten bij Enphase in de meter-telemetrie en die hoort bij een betaald plan. Nul
/// invullen is eerlijker dan iets verzinnen; wie de besparing wil weten, heeft een P1-meter nodig.
/// </summary>
public sealed class EnphaseScenarioTests
{
    private const string SystemenJson = """
    {"systems":[{"system_id":1234567,"name":"Dogan","status":"normal"}],"total":1}
    """;

    private const string SamenvattingJson = """
    {"system_id":1234567,"current_power":2350,"energy_today":8450,"energy_lifetime":9876543,
     "last_report_at":1762034400,"size_w":4200,"status":"normal"}
    """;

    private static EnphaseConnector Maak(FakeVendor vendor, bool ingesteld = true)
    {
        var options = ingesteld
            ? new EnphaseOptions("client-id", "client-secret", "api-sleutel")
            : new EnphaseOptions(null, null, null);

        return new EnphaseConnector(vendor.Factory(EnphaseConnector.DefaultBaseUrl), options, NullLogger<EnphaseConnector>.Instance);
    }

    private static FakeVendor Vendor(string refreshTokenTerug = "refresh-2") => new FakeVendor()
        .Json("/oauth/token", $"{{\"access_token\":\"access-1\",\"refresh_token\":\"{refreshTokenTerug}\",\"expires_in\":86400}}")
        .Json("/summary", SamenvattingJson)
        .Json("/api/v4/systems", SystemenJson);

    private static Device Apparaat() => new()
    {
        Id = Guid.NewGuid(),
        ExternalId = "1234567",
        Type = DeviceType.Inverter,
        Brand = DeviceBrand.Enphase,
    };

    [Fact]
    public async Task Koppelen_met_een_toestemmingscode_levert_het_systeem_en_het_refresh_token()
    {
        var vendor = Vendor();
        var creds = new Dictionary<string, string>
        {
            ["code"] = "toestemmingscode",
            ["redirect_uri"] = "https://wattzicht.nl/oauth/enphase",
        };

        var result = await Maak(vendor).ConnectAsync(new ConnectRequest(DeviceBrand.Enphase, DeviceType.Inverter, creds), default);

        Assert.True(result.Success);
        var device = Assert.Single(result.Devices);
        Assert.Equal("1234567", device.ExternalId);
        Assert.Equal(DeviceType.Inverter, device.Type);
        Assert.Equal("refresh-2", device.ExtraCredentials!["refresh_token"]);
        Assert.Equal("1234567", device.ExtraCredentials["system_id"]);

        // De code moet met grant_type=authorization_code en de terugkeerpagina mee de deur uit.
        var tokenCall = vendor.Requests.First(r => r.Uri.ToString().Contains("/oauth/token"));
        Assert.Equal(HttpMethod.Post, tokenCall.Method);
        Assert.Contains("grant_type=authorization_code", tokenCall.Uri.ToString());
        Assert.Contains("redirect_uri=https%3A%2F%2Fwattzicht.nl%2Foauth%2Fenphase", tokenCall.Uri.ToString());
        Assert.True(tokenCall.Headers.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task Koppelen_zonder_terugkeerpagina_zegt_wat_er_mist()
    {
        var result = await Maak(Vendor()).ConnectAsync(
            new ConnectRequest(DeviceBrand.Enphase, DeviceType.Inverter, new Dictionary<string, string> { ["code"] = "abc" }), default);

        Assert.False(result.Success);
        Assert.Contains("redirect_uri", result.Error);
    }

    [Fact]
    public async Task Zonder_serverinstellingen_koppelt_Enphase_niet()
    {
        var result = await Maak(Vendor(), ingesteld: false).ConnectAsync(
            new ConnectRequest(DeviceBrand.Enphase, DeviceType.Inverter, new Dictionary<string, string> { ["refresh_token"] = "r" }), default);

        Assert.False(result.Success);
        Assert.Contains("nog niet ingesteld", result.Error);
    }

    [Fact]
    public async Task Uitlezen_geeft_vermogen_en_opwekteller_van_de_panelen()
    {
        var creds = new DeviceCredentials(new Dictionary<string, string>
        {
            ["refresh_token"] = "refresh-1",
            ["system_id"] = "1234567",
        });

        var reading = await Maak(Vendor()).ReadAsync(Apparaat(), creds, default);

        Assert.Equal(2350, reading.PvPowerW);
        Assert.Equal(9_876_543, reading.PvEnergyWh);

        // Het tijdstip komt van Enphase zelf (last_report_at), niet van onze klok.
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1762034400), reading.Timestamp);
    }

    [Fact]
    public async Task Net_en_verbruik_blijven_leeg_want_die_zitten_achter_een_betaald_plan()
    {
        var creds = new DeviceCredentials(new Dictionary<string, string> { ["refresh_token"] = "refresh-1" });

        var reading = await Maak(Vendor()).ReadAsync(Apparaat(), creds, default);

        Assert.Equal(0, reading.GridPowerW);
        Assert.Equal(0, reading.LoadPowerW);
        Assert.Null(reading.ImportEnergyWh);
        Assert.Null(reading.ExportEnergyWh);
    }

    [Fact]
    public async Task Het_vernieuwde_refresh_token_wordt_teruggezet_zodat_morgen_ook_werkt()
    {
        var creds = new DeviceCredentials(new Dictionary<string, string> { ["refresh_token"] = "refresh-1" });

        await Maak(Vendor("refresh-nieuw")).ReadAsync(Apparaat(), creds, default);

        Assert.True(creds.Changed);
        Assert.Equal("refresh-nieuw", creds["refresh_token"]);
    }

    [Fact]
    public async Task Hetzelfde_token_terug_betekent_niets_opnieuw_wegschrijven()
    {
        var creds = new DeviceCredentials(new Dictionary<string, string> { ["refresh_token"] = "refresh-1" });

        await Maak(Vendor("refresh-1")).ReadAsync(Apparaat(), creds, default);

        Assert.False(creds.Changed);
    }

    [Fact]
    public async Task Een_ingetrokken_koppeling_zegt_verbind_opnieuw()
    {
        var vendor = new FakeVendor().Status("/oauth/token", HttpStatusCode.BadRequest);
        var creds = new DeviceCredentials(new Dictionary<string, string> { ["refresh_token"] = "verlopen" });

        var fout = await Assert.ThrowsAsync<ConnectorException>(() => Maak(vendor).ReadAsync(Apparaat(), creds, default));

        Assert.Contains("Verbind opnieuw", fout.Message);
    }

    [Fact]
    public async Task De_maandlimiet_van_Enphase_krijgt_een_eigen_melding()
    {
        var vendor = new FakeVendor()
            .Json("/oauth/token", "{\"access_token\":\"a\",\"refresh_token\":\"r\"}")
            .Status("/summary", HttpStatusCode.TooManyRequests);
        var creds = new DeviceCredentials(new Dictionary<string, string> { ["refresh_token"] = "refresh-1" });

        var fout = await Assert.ThrowsAsync<ConnectorException>(() => Maak(vendor).ReadAsync(Apparaat(), creds, default));

        Assert.Contains("maandlimiet", fout.Message);
    }

    [Fact]
    public async Task Zonder_koppeling_in_de_credentials_lezen_we_niet_uit()
    {
        var creds = new DeviceCredentials(new Dictionary<string, string>());

        var fout = await Assert.ThrowsAsync<ConnectorException>(() => Maak(Vendor()).ReadAsync(Apparaat(), creds, default));

        Assert.Contains("Verbind opnieuw", fout.Message);
    }

    [Fact]
    public async Task Een_systeemnummer_als_getal_in_de_json_werkt_ook()
    {
        // Enphase geeft system_id soms als getal en soms als string terug.
        var vendor = new FakeVendor()
            .Json("/oauth/token", "{\"access_token\":\"a\",\"refresh_token\":\"r\"}")
            .Json("/api/v4/systems", "{\"systems\":[{\"system_id\":\"7654321\",\"name\":\"Tekst-id\"}]}");

        var result = await Maak(vendor).ConnectAsync(new ConnectRequest(DeviceBrand.Enphase, DeviceType.Inverter,
            new Dictionary<string, string> { ["refresh_token"] = "refresh-1" }), default);

        Assert.True(result.Success);
        Assert.Equal("7654321", Assert.Single(result.Devices).ExternalId);
    }

    [Fact]
    public async Task Een_account_zonder_systeem_geeft_een_begrijpelijke_melding()
    {
        var vendor = new FakeVendor()
            .Json("/oauth/token", "{\"access_token\":\"a\",\"refresh_token\":\"r\"}")
            .Json("/api/v4/systems", "{\"systems\":[],\"total\":0}");

        var result = await Maak(vendor).ConnectAsync(new ConnectRequest(DeviceBrand.Enphase, DeviceType.Inverter,
            new Dictionary<string, string> { ["refresh_token"] = "refresh-1" }), default);

        Assert.False(result.Success);
        Assert.Contains("Geen Enphase-systeem", result.Error);
    }

    [Fact]
    public async Task De_api_sleutel_gaat_mee_in_elke_aanroep()
    {
        var vendor = Vendor();
        var creds = new DeviceCredentials(new Dictionary<string, string> { ["refresh_token"] = "refresh-1" });

        await Maak(vendor).ReadAsync(Apparaat(), creds, default);

        var summary = vendor.Requests.Last();
        Assert.Contains("key=api-sleutel", summary.Uri.ToString());
        Assert.Equal("Bearer access-1", summary.Headers["Authorization"]);
    }

    [Fact]
    public void Enphase_staat_nu_in_de_lijst_met_merken_die_gekoppeld_kunnen_worden()
    {
        var registry = new ConnectorRegistry([Maak(Vendor())]);

        Assert.True(registry.IsSupported(DeviceBrand.Enphase));
    }
}
