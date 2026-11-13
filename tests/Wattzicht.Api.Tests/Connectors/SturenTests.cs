using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Wattzicht.Api.Connectors;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests.Connectors;

/// <summary>
/// Een laadpaal starten en pauzeren.
///
/// Dit is het enige in de app dat niet kijkt maar iets verandert in het huis van iemand anders.
/// Daarom staat hier vast welke opdracht er precies de deur uit gaat: pauzeren en niet stoppen,
/// want stoppen beëindigt de sessie en dan moet er bij sommige instellingen opnieuw met een pas
/// worden geautoriseerd. Dat is niet wat iemand bedoelt als hij zegt "nu even niet".
///
/// En wat er gebeurt als het misgaat: een zin waar de bewoner iets mee kan, geen statuscode.
/// </summary>
public sealed class SturenTests
{
    private const string Inlog = """
        {"accessToken":"vers-token","refreshToken":"vers-refresh","expiresIn":86400}
        """;

    private static readonly Dictionary<string, string> Creds =
        new() { ["username"] = "ibo@voorbeeld.nl", ["password"] = "geheim" };

    private static Device Paal(DeviceBrand merk, string id) => new()
    {
        Id = Guid.NewGuid(),
        HouseholdId = Guid.NewGuid(),
        Brand = merk,
        Type = DeviceType.Charger,
        Name = "Oprit",
        ExternalId = id,
        EncryptedCredentials = "x",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    // ---- Easee ----

    private static EaseeConnector Easee(FakeVendor vendor) =>
        new(vendor.Factory("https://api.easee.com"), NullLogger<EaseeConnector>.Instance);

    [Fact]
    public async Task Easee_pauzeert_met_pause_charging()
    {
        var vendor = new FakeVendor()
            .Json("/api/accounts/login", Inlog)
            .Json("/commands/pause_charging", "{}");

        await Easee(vendor).StuurAsync(Paal(DeviceBrand.Easee, "EH123456"), new DeviceCredentials(Creds), Laadopdracht.Pauze, default);

        Assert.Contains("/api/chargers/EH123456/commands/pause_charging",
            vendor.Requests.Select(r => r.Uri.AbsolutePath));
    }

    [Fact]
    public async Task Easee_gaat_verder_met_resume_charging()
    {
        // Resume en niet start: de auto hangt er al aan, en resume hoeft niet opnieuw geautoriseerd
        // te worden.
        var vendor = new FakeVendor()
            .Json("/api/accounts/login", Inlog)
            .Json("/commands/resume_charging", "{}");

        await Easee(vendor).StuurAsync(Paal(DeviceBrand.Easee, "EH123456"), new DeviceCredentials(Creds), Laadopdracht.Start, default);

        Assert.Contains("/api/chargers/EH123456/commands/resume_charging",
            vendor.Requests.Select(r => r.Uri.AbsolutePath));
    }

    [Fact]
    public async Task Een_afgewezen_opdracht_bij_Easee_vraagt_om_opnieuw_koppelen()
    {
        var vendor = new FakeVendor()
            .Json("/api/accounts/login", Inlog)
            .Status("/commands/", HttpStatusCode.Forbidden);

        var fout = await Assert.ThrowsAsync<ConnectorException>(() =>
            Easee(vendor).StuurAsync(Paal(DeviceBrand.Easee, "EH1"), new DeviceCredentials(Creds), Laadopdracht.Start, default));

        Assert.Contains("opnieuw", fout.Message);
    }

    // ---- Zaptec ----

    private static ZaptecConnector Zaptec(FakeVendor vendor) =>
        new(vendor.Factory("https://api.zaptec.com"), NullLogger<ZaptecConnector>.Instance);

    [Fact]
    public async Task Zaptec_pauzeert_met_506_en_gaat_verder_met_507()
    {
        var vendor = new FakeVendor()
            .Json("/oauth/token", """{"access_token":"t","expires_in":3600}""")
            .Json("/sendCommand/", "{}");

        var paal = Paal(DeviceBrand.Zaptec, "ZAP-1");

        await Zaptec(vendor).StuurAsync(paal, new DeviceCredentials(Creds), Laadopdracht.Pauze, default);
        await Zaptec(vendor).StuurAsync(paal, new DeviceCredentials(Creds), Laadopdracht.Start, default);

        var paden = vendor.Requests.Select(r => r.Uri.AbsolutePath).ToList();
        Assert.Contains("/api/chargers/ZAP-1/sendCommand/506", paden);
        Assert.Contains("/api/chargers/ZAP-1/sendCommand/507", paden);
    }

    [Fact]
    public async Task Zonder_inloggegevens_stuurt_Zaptec_niets()
    {
        var vendor = new FakeVendor().Json("/sendCommand/", "{}");

        var fout = await Assert.ThrowsAsync<ConnectorException>(() =>
            Zaptec(vendor).StuurAsync(
                Paal(DeviceBrand.Zaptec, "ZAP-1"),
                new DeviceCredentials(new Dictionary<string, string>()),
                Laadopdracht.Start,
                default));

        Assert.Contains("ontbreken", fout.Message);
        Assert.Empty(vendor.Requests);
    }

    // ---- wie kan het wel en wie niet ----

    [Fact]
    public void Alleen_de_merken_waarvan_we_het_hebben_nagekeken()
    {
        var vendor = new FakeVendor();

        Assert.True(Easee(vendor).KanSturen);
        Assert.True(Zaptec(vendor).KanSturen);

        // De rest leest alleen uit, en zegt dat ook in plaats van stil te blijven.
        // Via de interface, want zo gebruikt de rest van de code het ook: de standaardimplementatie
        // zit op IDeviceConnector en niet op elke klasse apart.
        IDeviceConnector growatt = new GrowattConnector(vendor.Factory("https://x"), NullLogger<GrowattConnector>.Instance);
        Assert.False(growatt.KanSturen);
    }

    [Fact]
    public async Task Een_merk_dat_niet_kan_sturen_gooit_een_duidelijke_fout()
    {
        var vendor = new FakeVendor();
        IDeviceConnector growatt = new GrowattConnector(vendor.Factory("https://x"), NullLogger<GrowattConnector>.Instance);

        var fout = await Assert.ThrowsAsync<ConnectorException>(() =>
            growatt.StuurAsync(Paal(DeviceBrand.Growatt, "X"), new DeviceCredentials(Creds), Laadopdracht.Start, default));

        Assert.Contains("niet worden aangestuurd", fout.Message);
    }
}
