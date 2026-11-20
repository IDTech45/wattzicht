using System.Text.Json;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Connectors;

/// <summary>
/// SolarEdge Monitoring API.
///
/// De bewoner maakt zelf een sleutel aan in het monitoringportaal onder Beheer, tabblad Sitetoegang,
/// en geeft die aan ons door; het site-id zoeken wij er daarna zelf bij. Dat laatste is met opzet:
/// het id staat in de URL van zijn eigen dashboard en dat is precies het soort ding waar iemand op
/// afhaakt. Lukt het ophalen niet, dan vragen we het alsnog.
///
/// Let op bij het koppelen: een sleutel kan alleen aangemaakt worden door wie eigenaarsrechten op de
/// site heeft. Installateurs leveren een installatie vaak op zonder die rechten over te dragen, en
/// dan ziet de bewoner dat tabblad niet eens. De foutmelding hieronder zegt dat er dus bij.
///
/// Twee eindpunten per uitlezing:
/// - <c>/site/{id}/currentPowerFlow</c> geeft zon, huis, net en batterij in één keer, maar alleen bij
///   een installatie met een verbruiksmeter. Zonder zo'n meter geeft SolarEdge hier niets bruikbaars.
/// - <c>/site/{id}/overview</c> geeft het huidige zonvermogen en de tellers (vandaag en totaal), en
///   werkt altijd. Die gebruiken we als basis en, bij een site zonder meter, als enige bron.
///
/// Limiet: 300 aanroepen per dag per sleutel. Twee aanroepen per uitlezing betekent dat we hooguit
/// elke twintig minuten mogen kijken; zie <c>PollingWorker.KortsteInterval</c>, waar dat is
/// vastgelegd. Een opbrengstgrafiek wordt daar niet slechter van, een geblokkeerde sleutel wel.
///
/// Tekens: SolarEdge geeft alles positief en zegt met de richting in <c>connections</c> wat er
/// gebeurt. Wij rekenen om naar de Wattzicht-conventie, waarbij het net positief is bij afname en de
/// batterij positief bij ontladen.
/// </summary>
public sealed class SolarEdgeConnector(IHttpClientFactory httpClientFactory, ILogger<SolarEdgeConnector> logger)
    : IDeviceConnector
{
    public const string HttpClientName = "solaredge";
    public const string DefaultBaseUrl = "https://monitoringapi.solaredge.com/";

    public DeviceBrand Brand => DeviceBrand.SolarEdge;

    public async Task<ConnectResult> ConnectAsync(ConnectRequest request, CancellationToken ct)
    {
        var key = request.Credentials.Get("api_key") ?? request.Credentials.Get("token");
        if (key is null)
        {
            return ConnectResult.Fail(
                "Vul je SolarEdge API-sleutel in. Die maak je aan in het monitoringportaal onder Beheer, "
                + "tabblad Sitetoegang. Zie je dat tabblad niet, dan heeft je installateur je nog geen "
                + "eigenaarsrechten op de installatie gegeven; vraag hem daarom.");
        }

        try
        {
            var siteId = request.Credentials.Get("site_id");
            if (string.IsNullOrWhiteSpace(siteId))
            {
                siteId = await ZoekSiteAsync(key, ct);
            }

            if (siteId is null)
            {
                return ConnectResult.Fail(
                    "Met deze sleutel vinden we geen installatie. Controleer of je hem uit het juiste "
                    + "account hebt gekopieerd, of vul het site-id erbij in; dat staat in de URL van je dashboard.");
            }

            using var overzicht = await HaalAsync(key, $"site/{siteId}/overview", ct);
            var o = overzicht.RootElement.GetProperty("overview");

            var extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["api_key"] = key,
                ["site_id"] = siteId,
            };

            var naam = await ZoekNaamAsync(key, siteId, ct);
            var apparaten = new List<DiscoveredDevice>
            {
                new(ExternalId: siteId, Name: naam ?? $"SolarEdge omvormer ({siteId})",
                    Type: DeviceType.Inverter, ExtraCredentials: extra),
            };

            // Een thuisbatterij melden we alleen als SolarEdge er ook echt een laat zien. Een lege
            // batterijrij in het scherm is erger dan geen rij: dan denkt iemand dat hij er een heeft.
            if (await HeeftBatterijAsync(key, siteId, ct))
            {
                apparaten.Add(new DiscoveredDevice(siteId, "SolarEdge thuisbatterij", DeviceType.Battery, extra));
            }

            _ = o; // het overzicht is hier alleen gebruikt om de sleutel te toetsen
            return ConnectResult.Ok(apparaten);
        }
        catch (ConnectorException ex)
        {
            return ConnectResult.Fail(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "SolarEdge niet bereikbaar bij koppelen");
            return ConnectResult.Fail("SolarEdge is op dit moment niet bereikbaar. Probeer het later opnieuw.");
        }
    }

    public async Task<DeviceReading> ReadAsync(Device device, DeviceCredentials credentials, CancellationToken ct)
    {
        var key = credentials.Get("api_key") ?? credentials.Get("token")
            ?? throw new ConnectorException("SolarEdge API-sleutel ontbreekt.");
        var siteId = credentials.Get("site_id") ?? device.ExternalId;

        try
        {
            JsonElement? stroom = null;
            try
            {
                using var flow = await HaalAsync(key, $"site/{siteId}/currentPowerFlow", ct);
                if (flow.RootElement.TryGetPropertyIgnoreCase("siteCurrentPowerFlow", out var f))
                {
                    stroom = f.Clone();
                }
            }
            catch (ConnectorException)
            {
                // Geen verbruiksmeter op deze installatie. Dat is geen storing: we vallen terug op het
                // overzicht, dat alleen de zon kent. Zo krijgt ook wie geen meter heeft zijn opbrengst.
            }

            if (device.Type == DeviceType.Battery)
            {
                return LeesBatterij(stroom, DateTimeOffset.UtcNow);
            }

            using var overzicht = await HaalAsync(key, $"site/{siteId}/overview", ct);
            return Lees(overzicht.RootElement, stroom, DateTimeOffset.UtcNow);
        }
        catch (HttpRequestException ex)
        {
            throw new ConnectorException("SolarEdge is niet bereikbaar.", ex);
        }
        catch (KeyNotFoundException ex)
        {
            throw new ConnectorException("Onverwacht antwoord van SolarEdge.", ex);
        }
    }

    /// <summary>
    /// Zet het overzicht en, als die er is, de vermogensstroom om naar één meting. Publiek voor tests.
    ///
    /// Het overzicht is leidend voor de tellers; de vermogensstroom voor wat er nu gebeurt. Zijn beide
    /// er, dan komt het zonvermogen uit de stroom, want die is actueler dan het overzicht.
    /// </summary>
    public static DeviceReading Lees(JsonElement overzicht, JsonElement? stroom, DateTimeOffset nu)
    {
        var o = overzicht.TryGetPropertyIgnoreCase("overview", out var ov) ? ov : overzicht;

        var zon = o.TryGetPropertyIgnoreCase("currentPower", out var cp)
            ? cp.GetDoubleOrNull("power") ?? 0
            : 0;
        var vandaagWh = KwhOfWh(o, "lastDayData");
        var totaalWh = KwhOfWh(o, "lifeTimeData");

        double huis = 0, net = 0;
        if (stroom is { } s)
        {
            var eenheid = s.GetStringOrNull("unit") ?? "kW";
            double Naar(string naam) => NaarWatt(s.TryGetPropertyIgnoreCase(naam, out var e)
                ? e.GetDoubleOrNull("currentPower") ?? 0 : 0, eenheid);

            var zonUitStroom = Naar("PV");
            if (zonUitStroom > 0) zon = zonUitStroom;
            huis = Naar("LOAD");

            // SolarEdge geeft het netvermogen altijd positief en zegt met de status of er geleverd of
            // afgenomen wordt. "Active" betekent hier dat er stroom loopt; de richting zit in de
            // verbindingen, waar GRID als bron staat bij afname en als bestemming bij teruglevering.
            var netW = Naar("GRID");
            net = LeverenAanNet(s) ? -netW : netW;
        }

        return new DeviceReading(
            Timestamp: nu,
            PvPowerW: Math.Round(zon),
            LoadPowerW: Math.Round(huis),
            GridPowerW: Math.Round(net),
            PvEnergyWh: totaalWh,
            PvTodayWh: vandaagWh);
    }

    /// <summary>De batterijkant apart, zodat een hybride installatie niet twee keer hetzelfde meldt.</summary>
    public static DeviceReading LeesBatterij(JsonElement? stroom, DateTimeOffset nu)
    {
        if (stroom is not { } s || !s.TryGetPropertyIgnoreCase("STORAGE", out var accu))
        {
            return new DeviceReading(Timestamp: nu);
        }

        var eenheid = s.GetStringOrNull("unit") ?? "kW";
        var vermogen = NaarWatt(accu.GetDoubleOrNull("currentPower") ?? 0, eenheid);
        var status = (accu.GetStringOrNull("status") ?? "").ToLowerInvariant();

        // Laden is bij ons negatief, ontladen positief. SolarEdge zegt het met een woord.
        var ontlaadt = status.Contains("discharg");
        return new DeviceReading(
            Timestamp: nu,
            BatteryPowerW: Math.Round(ontlaadt ? vermogen : -vermogen),
            BatterySoc: accu.GetDoubleOrNull("chargeLevel"));
    }

    /// <summary>Levert dit huis op dit moment aan het net? Dan staat GRID in de verbindingen als bestemming.</summary>
    private static bool LeverenAanNet(JsonElement stroom)
    {
        if (!stroom.TryGetPropertyIgnoreCase("connections", out var verbindingen)
            || verbindingen.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var v in verbindingen.EnumerateArray())
        {
            if (string.Equals(v.GetStringOrNull("to"), "Grid", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static double NaarWatt(double waarde, string eenheid) => eenheid.ToLowerInvariant() switch
    {
        "w" => waarde,
        "mw" => waarde * 1_000_000,
        _ => waarde * 1000,   // kW is wat SolarEdge standaard stuurt
    };

    /// <summary>Tellers komen als Wh met een eenheid ernaast; we rekenen alles naar Wh.</summary>
    private static double? KwhOfWh(JsonElement overzicht, string naam)
    {
        if (!overzicht.TryGetPropertyIgnoreCase(naam, out var blok)) return null;
        var energie = blok.GetDoubleOrNull("energy");
        if (energie is null) return null;
        var eenheid = (blok.GetStringOrNull("unit") ?? "Wh").ToLowerInvariant();
        return eenheid switch
        {
            "kwh" => energie * 1000,
            "mwh" => energie * 1_000_000,
            _ => energie,
        };
    }

    private async Task<string?> ZoekSiteAsync(string key, CancellationToken ct)
    {
        using var doc = await HaalAsync(key, "sites/list", ct);
        if (!doc.RootElement.TryGetPropertyIgnoreCase("sites", out var sites)
            || !sites.TryGetPropertyIgnoreCase("site", out var lijst)
            || lijst.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var site in lijst.EnumerateArray())
        {
            var id = site.GetDoubleOrNull("id");
            if (id is not null) return ((long)id.Value).ToString();
        }

        return null;
    }

    private async Task<string?> ZoekNaamAsync(string key, string siteId, CancellationToken ct)
    {
        try
        {
            using var doc = await HaalAsync(key, $"site/{siteId}/details", ct);
            if (doc.RootElement.TryGetPropertyIgnoreCase("details", out var d))
            {
                var naam = d.GetStringOrNull("name");
                if (!string.IsNullOrWhiteSpace(naam)) return $"SolarEdge {naam}";
            }
        }
        catch (ConnectorException)
        {
            // Een naam is mooi meegenomen, geen reden om het koppelen te laten mislukken.
        }

        return null;
    }

    private async Task<bool> HeeftBatterijAsync(string key, string siteId, CancellationToken ct)
    {
        try
        {
            using var doc = await HaalAsync(key, $"site/{siteId}/currentPowerFlow", ct);
            return doc.RootElement.TryGetPropertyIgnoreCase("siteCurrentPowerFlow", out var f)
                   && f.TryGetPropertyIgnoreCase("STORAGE", out _);
        }
        catch (ConnectorException)
        {
            return false;
        }
    }

    private async Task<JsonDocument> HaalAsync(string key, string pad, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var antwoord = await client.GetAsync($"{pad}?api_key={Uri.EscapeDataString(key)}", ct);

        if (antwoord.StatusCode == System.Net.HttpStatusCode.Forbidden
            || antwoord.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            throw new ConnectorException(
                "SolarEdge accepteert deze sleutel niet (of niet voor deze installatie). Controleer hem in het "
                + "monitoringportaal onder Beheer, tabblad Sitetoegang.");
        }

        if (antwoord.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            throw new ConnectorException(
                "SolarEdge laat maar een beperkt aantal opvragingen per dag toe en die zijn voor vandaag op. "
                + "Morgen gaat het vanzelf weer.");
        }

        if (!antwoord.IsSuccessStatusCode)
        {
            throw new ConnectorException($"SolarEdge gaf een onverwacht antwoord ({(int)antwoord.StatusCode}).");
        }

        var inhoud = await antwoord.Content.ReadAsStringAsync(ct);
        try
        {
            return JsonDocument.Parse(inhoud);
        }
        catch (JsonException ex)
        {
            throw new ConnectorException("Onleesbaar antwoord van SolarEdge.", ex);
        }
    }
}
