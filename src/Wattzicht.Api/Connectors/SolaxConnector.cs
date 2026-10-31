using System.Globalization;
using System.Text.Json;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Connectors;

/// <summary>
/// SolaXCloud User Monitoring API (V6.1):
/// <c>GET https://www.solaxcloud.com/proxyApp/proxy/api/getRealtimeInfo.do?tokenId={token}&amp;sn={wifi_sn}</c>.
/// Credentials: <c>token</c> (SolaXCloud → Service → API → TokenID) en <c>sn</c> (registratienummer van de
/// wifi-/LAN-dongle, 10 tekens, staat op de dongle en in SolaXCloud bij het apparaat).
///
/// Tekens bij SolaX: <c>feedinpower</c> positief = teruglevering, negatief = afname;
/// <c>batPower</c> positief = laden, negatief = ontladen. Wij draaien beide om naar de Wattzicht-conventie
/// (grid positief = afname, batterij positief = ontladen).
///
/// Limiet van SolaX: minder dan 10 calls per minuut en 10.000 per dag per token. De poller leest elke 5 minuten;
/// een hybride levert twee apparaten (omvormer + batterij) op hetzelfde serienummer, dus 2 calls per ronde.
///
/// Laadpaal (SolaX Smart EV Charger / X1-X3-HAC): zelfde TokenID, maar het serienummer van de laadpaal en de
/// endpoint <c>api/v2/charger/getInfo</c> (token in header <c>tokenId</c>). Die geeft fasestromen en een
/// cumulatieve energieteller, geen momentaan vermogen; dat schatten we als stroom × 230 V per fase.
/// SolaX Power Nederland zit in Hengelo en levert omvormers, batterijen en laadpalen met MID-meter.
/// </summary>
public sealed class SolaxConnector(IHttpClientFactory httpClientFactory, ILogger<SolaxConnector> logger) : IDeviceConnector
{
    public const string HttpClientName = "solax";
    public const string DefaultBaseUrl = "https://www.solaxcloud.com/";
    private const string RealtimePath = "proxyApp/proxy/api/getRealtimeInfo.do";
    private const string ChargerPath = "api/v2/charger/getInfo";
    private const double PhaseVoltage = 230d;

    public DeviceBrand Brand => DeviceBrand.Solax;

    public async Task<ConnectResult> ConnectAsync(ConnectRequest request, CancellationToken ct)
    {
        var token = request.Credentials.Get("token");
        var sn = request.Credentials.Get("sn");
        if (token is null)
        {
            return ConnectResult.Fail("Vul je SolaXCloud TokenID in (SolaXCloud → Service → API).");
        }

        if (request.Type == DeviceType.Charger)
        {
            return await ConnectChargerAsync(token, request.Credentials.Get("charger_sn") ?? sn, ct);
        }

        if (sn is null)
        {
            return ConnectResult.Fail("Vul het registratienummer van de dongle in (10 tekens, staat op de wifi-stick).");
        }

        try
        {
            using var doc = await GetRealtimeAsync(token, sn, ct);
            var result = doc.RootElement.GetProperty("result");

            var inverterSn = result.GetStringOrNull("inverterSN", "inverterSn") ?? sn;
            var hasBattery = result.GetDoubleOrNull("soc") is > 0
                             || Math.Abs(result.GetDoubleOrZero("batPower")) > 0
                             || IsHybrid(result.GetStringOrNull("inverterType"));

            var extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["sn"] = sn,
                ["inverter_sn"] = inverterSn
            };

            var devices = new List<DiscoveredDevice>
            {
                new(ExternalId: sn, Name: $"SolaX omvormer ({inverterSn})", Type: DeviceType.Inverter, ExtraCredentials: extra)
            };

            if (hasBattery)
            {
                devices.Add(new DiscoveredDevice(ExternalId: sn, Name: "SolaX batterij", Type: DeviceType.Battery, ExtraCredentials: extra));
            }

            return ConnectResult.Ok(devices);
        }
        catch (ConnectorException ex)
        {
            return ConnectResult.Fail(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "SolaXCloud niet bereikbaar bij koppelen");
            return ConnectResult.Fail("SolaXCloud is op dit moment niet bereikbaar. Probeer het later opnieuw.");
        }
    }

    public async Task<DeviceReading> ReadAsync(Device device, DeviceCredentials credentials, CancellationToken ct)
    {
        var token = credentials.Get("token") ?? throw new ConnectorException("SolaXCloud-token ontbreekt.");
        if (device.Type == DeviceType.Charger)
        {
            return await ReadChargerAsync(token, credentials.Get("charger_sn") ?? device.ExternalId, ct);
        }

        var sn = credentials.Get("sn") ?? device.ExternalId;

        try
        {
            using var doc = await GetRealtimeAsync(token, sn, ct);
            var r = doc.RootElement.GetProperty("result");
            var ts = ParseUploadTime(r.GetStringOrNull("uploadTime")) ?? DateTimeOffset.UtcNow;

            if (device.Type == DeviceType.Battery)
            {
                // Alleen batterijvelden: voorkomt dubbeltelling met de omvormer-rij op hetzelfde serienummer.
                return new DeviceReading(
                    Timestamp: ts,
                    BatteryPowerW: -r.GetDoubleOrZero("batPower"),
                    BatterySoc: r.GetDoubleOrNull("soc"));
            }

            var pv = r.GetDoubleOrZero("powerdc1") + r.GetDoubleOrZero("powerdc2")
                     + r.GetDoubleOrZero("powerdc3") + r.GetDoubleOrZero("powerdc4");
            var ac = r.GetDoubleOrZero("acpower");
            var feedIn = r.GetDoubleOrZero("feedinpower");      // + = teruglevering
            var grid = -feedIn;                                  // Wattzicht: + = afname
            var load = Math.Max(0, ac + grid);                   // huisverbruik = omvormer-AC + netafname

            // Sommige firmware geeft 0 op de DC-strings maar wel AC-vermogen (bijv. stringomvormer zonder batterij).
            if (pv <= 0 && ac > 0 && r.GetDoubleOrNull("soc") is null)
            {
                pv = ac;
            }

            return new DeviceReading(
                Timestamp: ts,
                PvPowerW: Math.Round(pv),
                LoadPowerW: Math.Round(load),
                GridPowerW: Math.Round(grid),
                PvEnergyWh: KwhToWh(r.GetDoubleOrNull("yieldtotal")),
                ImportEnergyWh: KwhToWh(r.GetDoubleOrNull("consumeenergy")),
                ExportEnergyWh: KwhToWh(r.GetDoubleOrNull("feedinenergy")));
        }
        catch (HttpRequestException ex)
        {
            throw new ConnectorException("SolaXCloud is niet bereikbaar.", ex);
        }
        catch (KeyNotFoundException ex)
        {
            throw new ConnectorException("Onverwacht antwoord van SolaXCloud.", ex);
        }
    }

    private async Task<ConnectResult> ConnectChargerAsync(string token, string? chargerSn, CancellationToken ct)
    {
        if (chargerSn is null)
        {
            return ConnectResult.Fail("Vul het serienummer van de laadpaal in (staat op de zijkant en in SolaXCloud).");
        }

        try
        {
            using var doc = await GetChargerAsync(token, chargerSn, ct);
            var extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["charger_sn"] = chargerSn };
            return ConnectResult.Ok([new DiscoveredDevice(chargerSn, $"SolaX laadpaal ({chargerSn})", DeviceType.Charger, extra)]);
        }
        catch (ConnectorException ex)
        {
            return ConnectResult.Fail(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "SolaXCloud niet bereikbaar bij koppelen laadpaal");
            return ConnectResult.Fail("SolaXCloud is op dit moment niet bereikbaar. Probeer het later opnieuw.");
        }
    }

    private async Task<DeviceReading> ReadChargerAsync(string token, string chargerSn, CancellationToken ct)
    {
        try
        {
            using var doc = await GetChargerAsync(token, chargerSn, ct);
            var r = doc.RootElement.GetProperty("result");
            return ParseCharger(r, DateTimeOffset.UtcNow);
        }
        catch (HttpRequestException ex)
        {
            throw new ConnectorException("SolaXCloud is niet bereikbaar.", ex);
        }
    }

    /// <summary>Normaliseert het charger/getInfo-resultaat. Publiek voor tests.</summary>
    public static DeviceReading ParseCharger(JsonElement r, DateTimeOffset timestamp)
    {
        // Status 2 = Charging. Alleen dan rekenen we vermogen; anders 0 (stroomwaarden kunnen blijven hangen).
        var state = (int)(r.GetDoubleOrNull("deviceState") ?? -1);
        double power = 0;
        if (state == 2)
        {
            power = r.GetDoubleOrNull("chargePower", "power", "chargingPower")
                    ?? PhaseVoltage * (r.GetDoubleOrZero("currentA") + r.GetDoubleOrZero("currentB") + r.GetDoubleOrZero("currentC"));
            if (power <= 0)
            {
                power = PhaseVoltage * r.GetDoubleOrZero("current");
            }
        }

        // chargeEnergyTotal is cumulatief, in kWh.
        var totalKwh = r.GetDoubleOrNull("chargeEnergyTotal", "totalChargeEnergy");
        return new DeviceReading(
            Timestamp: timestamp,
            ChargerPowerW: Math.Round(Math.Max(0, power)),
            ChargerEnergyWh: KwhToWh(totalKwh));
    }

    private async Task<JsonDocument> GetChargerAsync(string token, string chargerSn, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);

        // De documentatie noemt GET maar toont een JSON-body; we proberen POST en vallen terug op GET.
        using var post = new HttpRequestMessage(HttpMethod.Post, ChargerPath)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { sn = chargerSn }), System.Text.Encoding.UTF8, "application/json")
        };
        post.Headers.Add("tokenId", token);
        using var response = await SendGuardedAsync(() => client.SendAsync(post, ct), ct);

        HttpResponseMessage final = response;
        HttpResponseMessage? fallback = null;
        if (response.StatusCode is System.Net.HttpStatusCode.MethodNotAllowed or System.Net.HttpStatusCode.NotFound)
        {
            var get = new HttpRequestMessage(HttpMethod.Get, $"{ChargerPath}?sn={Uri.EscapeDataString(chargerSn)}");
            get.Headers.Add("tokenId", token);
            fallback = await SendGuardedAsync(() => client.SendAsync(get, ct), ct);
            final = fallback;
        }

        try
        {
            if (!final.IsSuccessStatusCode)
            {
                throw new ConnectorException($"SolaXCloud gaf HTTP {(int)final.StatusCode} voor de laadpaal.");
            }

            var doc = await ParseJsonAsync(final, ct);
            var root = doc.RootElement;
            var code = (int)(root.GetDoubleOrNull("code") ?? 0);
            var success = root.TryGetPropertyIgnoreCase("success", out var s) && s.ValueKind == JsonValueKind.True;
            if (!success || code != 0 || !root.TryGetPropertyIgnoreCase("result", out var result) || result.ValueKind != JsonValueKind.Object)
            {
                doc.Dispose();
                throw new ConnectorException(code switch
                {
                    1001 => "Dit SolaXCloud-account heeft geen toegang tot de laadpaal-API. Vraag de installateur om API-rechten.",
                    1002 => "SolaXCloud keurt het serienummer van de laadpaal af. Controleer het nummer.",
                    1003 => "Deze laadpaal hoort niet bij dit SolaXCloud-account.",
                    2002 => "SolaXCloud vindt geen laadpaal met dit serienummer.",
                    _ => TranslateError(root.GetStringOrNull("exception") ?? "onbekende fout")
                });
            }

            return doc;
        }
        finally
        {
            fallback?.Dispose();
        }
    }

    private async Task<JsonDocument> GetRealtimeAsync(string token, string sn, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        var url = $"{RealtimePath}?tokenId={Uri.EscapeDataString(token)}&sn={Uri.EscapeDataString(sn)}";

        using var response = await SendGuardedAsync(() => client.GetAsync(url, ct), ct);
        if ((int)response.StatusCode == 429)
        {
            throw new ConnectorException("SolaXCloud-limiet bereikt (max. 10 keer per minuut). Probeer het zo opnieuw.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new ConnectorException($"SolaXCloud gaf HTTP {(int)response.StatusCode}.");
        }

        var doc = await ParseJsonAsync(response, ct);
        var root = doc.RootElement;

        var success = root.TryGetPropertyIgnoreCase("success", out var s) && s.ValueKind == JsonValueKind.True;
        if (!success || !root.TryGetPropertyIgnoreCase("result", out var result) || result.ValueKind != JsonValueKind.Object)
        {
            var message = root.GetStringOrNull("exception") ?? "onbekende fout";
            doc.Dispose();
            throw new ConnectorException(success
                ? "SolaXCloud gaf geen meetgegevens terug. Staat de omvormer aan en is de dongle online?"
                : TranslateError(message));
        }

        return doc;
    }

    /// <summary>Time-out en netwerkfout worden een nette <see cref="ConnectorException"/>; annuleren door de aanroeper niet.</summary>
    private static async Task<HttpResponseMessage> SendGuardedAsync(Func<Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        try
        {
            return await send();
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ConnectorException("SolaXCloud reageert niet (time-out). Probeer het later opnieuw.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ConnectorException("SolaXCloud is op dit moment niet bereikbaar. Probeer het later opnieuw.", ex);
        }
    }

    private static async Task<JsonDocument> ParseJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
        catch (JsonException ex)
        {
            throw new ConnectorException("Onverwacht antwoord van SolaXCloud (geen geldige JSON).", ex);
        }
    }

    private static string TranslateError(string message)
    {
        var m = message.ToLowerInvariant();
        if (m.Contains("token"))
        {
            return "SolaXCloud weigert het TokenID. Controleer het token in SolaXCloud → Service → API.";
        }

        if (m.Contains("frequen") || m.Contains("limit") || m.Contains("threshold") || m.Contains("too many"))
        {
            return "SolaXCloud-limiet bereikt (max. 10 keer per minuut). Probeer het zo opnieuw.";
        }

        if (m.Contains("sn") || m.Contains("serial") || m.Contains("not exist") || m.Contains("permission"))
        {
            return "SolaXCloud kent dit registratienummer niet bij dit account. Controleer de 10 tekens op de dongle.";
        }

        return $"SolaXCloud: {message}";
    }

    /// <summary>Hybride typecodes uit tabel 4 van de API-documentatie (X1/X3-Hybrid, X1-Fit, X3-Fit, X1/X3-IES).</summary>
    private static bool IsHybrid(string? inverterType) => inverterType is "3" or "5" or "6" or "7" or "11" or "13" or "14" or "15" or "16";

    private static double? KwhToWh(double? kwh) => kwh is null ? null : Math.Round(kwh.Value * 1000);

    /// <summary>SolaX geeft <c>yyyy-MM-dd HH:mm:ss</c> in de tijdzone van de installatie; voor NL nemen we Europe/Amsterdam.</summary>
    private static DateTimeOffset? ParseUploadTime(string? value)
    {
        if (value is null || !DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return null;
        }

        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
            return new DateTimeOffset(local, tz.GetUtcOffset(local));
        }
        catch (TimeZoneNotFoundException)
        {
            return new DateTimeOffset(local, TimeSpan.Zero);
        }
    }
}
