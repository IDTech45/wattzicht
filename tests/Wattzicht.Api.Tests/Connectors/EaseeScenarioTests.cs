using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Wattzicht.Api.Connectors;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests.Connectors;

/// <summary>
/// Easee-cloud: inloggen, uitlezen, en het hergebruik van het token.
///
/// Dat laatste is waar het hier echt om gaat. Een connector die bij elke ronde opnieuw met
/// wachtwoord inlogt werkt prima in een test en gaat in productie stuk: Easee gaat knijpen zodra
/// honderd huizen elke vijf minuten inloggen vanaf hetzelfde serveradres. Precies dat is bij LaadCash
/// met Zaptec gebeurd. Dus staat hier vast dat een geldig token hergebruikt wordt, dat een verlopen
/// token eerst met het refresh-token wordt vernieuwd, en dat het nieuwe token wordt teruggegeven om
/// te bewaren.
///
/// De velden van <c>/state</c> komen uit de documentatie van Easee en niet uit een meting aan een
/// echte paal, dus leest de connector meer dan één schrijfwijze. Dat staat hier ook als test, anders
/// sneuvelt die soepelheid bij de eerste opruimbeurt.
/// </summary>
public sealed class EaseeScenarioTests
{
    private const string Inlog = """
        {"accessToken":"vers-token","refreshToken":"vers-refresh","expiresIn":86400}
        """;

    private const string Palen = """
        [{"id":"EH123456","name":"Oprit","levelOfAccess":3}]
        """;

    private const string Stand = """
        {"chargerOpMode":3,"totalPower":7.36,"sessionEnergy":4.2,"lifetimeEnergy":1234.5}
        """;

    private static readonly Dictionary<string, string> Creds =
        new() { ["username"] = "ibo@voorbeeld.nl", ["password"] = "geheim" };

    private static EaseeConnector Maak(FakeVendor vendor) =>
        new(vendor.Factory("https://api.easee.com"), NullLogger<EaseeConnector>.Instance);

    private static FakeVendor WerkendeEasee() => new FakeVendor()
        .Json("/api/accounts/login", Inlog)
        .Json("/api/accounts/refresh_token", Inlog)
        .Json("/api/chargers/EH123456/state", Stand)
        .Json("/api/chargers", Palen);

    [Fact]
    public async Task Koppelen_vindt_de_paal_en_geeft_het_token_mee()
    {
        var vendor = WerkendeEasee();

        var result = await Maak(vendor).ConnectAsync(
            new ConnectRequest(DeviceBrand.Easee, DeviceType.Charger, Creds), default);

        Assert.True(result.Success, result.Error);
        var paal = Assert.Single(result.Devices);
        Assert.Equal("EH123456", paal.ExternalId);
        Assert.Equal("Oprit", paal.Name);
        Assert.Equal(DeviceType.Charger, paal.Type);

        // Het verse token gaat mee de koppeling in, zodat de eerste uitlezing niet opnieuw inlogt.
        Assert.Equal("vers-token", paal.ExtraCredentials!["access_token"]);
        Assert.Equal("vers-refresh", paal.ExtraCredentials["refresh_token"]);
    }

    [Fact]
    public async Task Zonder_inloggegevens_vraagt_hij_erom_in_plaats_van_te_proberen()
    {
        var vendor = WerkendeEasee();

        var result = await Maak(vendor).ConnectAsync(
            new ConnectRequest(DeviceBrand.Easee, DeviceType.Charger, new Dictionary<string, string>()), default);

        Assert.False(result.Success);
        Assert.Contains("Easee-app", result.Error);
        Assert.Empty(vendor.Requests);
    }

    [Fact]
    public async Task Een_verkeerd_wachtwoord_levert_een_zin_op_waar_iemand_iets_mee_kan()
    {
        var vendor = new FakeVendor().Status("/api/accounts/login", HttpStatusCode.Unauthorized);

        var result = await Maak(vendor).ConnectAsync(
            new ConnectRequest(DeviceBrand.Easee, DeviceType.Charger, Creds), default);

        Assert.False(result.Success);
        Assert.Contains("wachtwoord", result.Error);
        Assert.DoesNotContain("401", result.Error);
    }

    [Fact]
    public async Task Een_leeg_account_zegt_dat_er_geen_paal_op_staat()
    {
        var vendor = new FakeVendor()
            .Json("/api/accounts/login", Inlog)
            .Json("/api/chargers", "[]");

        var result = await Maak(vendor).ConnectAsync(
            new ConnectRequest(DeviceBrand.Easee, DeviceType.Charger, Creds), default);

        Assert.False(result.Success);
        Assert.Contains("Geen laadpaal", result.Error);
    }

    [Fact]
    public async Task Uitlezen_hergebruikt_een_token_dat_nog_geldig_is()
    {
        var vendor = WerkendeEasee();
        var creds = new DeviceCredentials(new Dictionary<string, string>
        {
            ["username"] = "ibo@voorbeeld.nl",
            ["password"] = "geheim",
            ["access_token"] = "nog-goed",
            ["refresh_token"] = "oud-refresh",
            ["token_verloopt"] = DateTimeOffset.UtcNow.AddHours(5).ToString("O"),
        });

        var meting = await Maak(vendor).ReadAsync(Paal(), creds, default);

        Assert.Equal(7360, meting.ChargerPowerW, 1);
        Assert.False(creds.Changed);

        // Eén aanroep: alleen de stand. Geen inlog, geen refresh.
        var paden = vendor.Requests.Select(r => r.Uri.AbsolutePath).ToList();
        Assert.Equal(["/api/chargers/EH123456/state"], paden);
    }

    [Fact]
    public async Task Een_verlopen_token_wordt_vernieuwd_met_het_refresh_token_en_bewaard()
    {
        var vendor = WerkendeEasee();
        var creds = new DeviceCredentials(new Dictionary<string, string>
        {
            ["username"] = "ibo@voorbeeld.nl",
            ["password"] = "geheim",
            ["access_token"] = "verlopen",
            ["refresh_token"] = "oud-refresh",
            ["token_verloopt"] = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"),
        });

        await Maak(vendor).ReadAsync(Paal(), creds, default);

        Assert.Contains("/api/accounts/refresh_token", vendor.Requests.Select(r => r.Uri.AbsolutePath));
        Assert.DoesNotContain("/api/accounts/login", vendor.Requests.Select(r => r.Uri.AbsolutePath));

        // De poller bewaart alleen als Changed aan staat; zonder dit werkt de koppeling één keer.
        Assert.True(creds.Changed);
        Assert.Equal("vers-token", creds["access_token"]);
        Assert.Equal("vers-refresh", creds["refresh_token"]);
    }

    [Fact]
    public async Task Is_het_refresh_token_ook_te_oud_dan_logt_hij_gewoon_opnieuw_in()
    {
        var vendor = new FakeVendor()
            .Status("/api/accounts/refresh_token", HttpStatusCode.BadRequest)
            .Json("/api/accounts/login", Inlog)
            .Json("/api/chargers/EH123456/state", Stand);

        var creds = new DeviceCredentials(new Dictionary<string, string>
        {
            ["username"] = "ibo@voorbeeld.nl",
            ["password"] = "geheim",
            ["access_token"] = "verlopen",
            ["refresh_token"] = "te-oud",
            ["token_verloopt"] = DateTimeOffset.UtcNow.AddDays(-2).ToString("O"),
        });

        var meting = await Maak(vendor).ReadAsync(Paal(), creds, default);

        Assert.Equal(7360, meting.ChargerPowerW, 1);
        Assert.Contains("/api/accounts/login", vendor.Requests.Select(r => r.Uri.AbsolutePath));
    }

    [Fact]
    public async Task Een_koppeling_die_Easee_afwijst_vraagt_om_opnieuw_koppelen()
    {
        var vendor = new FakeVendor()
            .Json("/api/accounts/login", Inlog)
            .Status("/api/chargers/EH123456/state", HttpStatusCode.Forbidden);

        var fout = await Assert.ThrowsAsync<ConnectorException>(() =>
            Maak(vendor).ReadAsync(Paal(), new DeviceCredentials(Creds), default));

        Assert.Contains("opnieuw", fout.Message);
    }

    [Fact]
    public void Kilowatt_wordt_watt_en_de_meterstand_wordt_wattuur()
    {
        using var doc = JsonDocument.Parse(Stand);

        var meting = EaseeConnector.LeesStand(doc.RootElement, DateTimeOffset.UnixEpoch);

        Assert.Equal(7360, meting.ChargerPowerW, 1);
        Assert.Equal(1_234_500, meting.ChargerEnergyWh!.Value, 1);

        // Wat door de paal gaat, gaat ook door het huis; anders mist de verbruikskant een laadbeurt.
        Assert.Equal(meting.ChargerPowerW, meting.LoadPowerW);
    }

    [Fact]
    public void Een_paal_zonder_meterstand_geeft_null_en_niet_nul()
    {
        // Nul is een meting ("de teller staat op nul"), null is "dit merk zegt het niet". In een
        // grafiek is dat verschil zichtbaar, dus mag het hier niet door elkaar lopen.
        using var doc = JsonDocument.Parse("""{"totalPower":0}""");

        var meting = EaseeConnector.LeesStand(doc.RootElement, DateTimeOffset.UnixEpoch);

        Assert.Equal(0, meting.ChargerPowerW);
        Assert.Null(meting.ChargerEnergyWh);
    }

    [Fact]
    public void Ook_met_hoofdletters_leest_hij_de_stand()
    {
        using var doc = JsonDocument.Parse("""{"TotalPower":3.68,"LifetimeEnergy":500}""");

        var meting = EaseeConnector.LeesStand(doc.RootElement, DateTimeOffset.UnixEpoch);

        Assert.Equal(3680, meting.ChargerPowerW, 1);
        Assert.Equal(500_000, meting.ChargerEnergyWh!.Value, 1);
    }

    private static Device Paal() => new()
    {
        Id = Guid.NewGuid(),
        HouseholdId = Guid.NewGuid(),
        Brand = DeviceBrand.Easee,
        Type = DeviceType.Charger,
        Name = "Oprit",
        ExternalId = "EH123456",
        EncryptedCredentials = "x",
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
