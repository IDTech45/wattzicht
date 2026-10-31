using System.Globalization;
using System.Text.Json;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Connectors;

/// <summary>
/// Growatt Open API v1 (<c>https://openapi.growatt.com/v1/</c>), token in header <c>token</c>.
/// Credentials: <c>token</c>. Bij koppelen slaan we per apparaat ook <c>plant_id</c> en <c>device_kind</c>
/// (<c>tlx</c> voor MIN/TLX, <c>mix</c> voor SPH/MIX) op, zodat uitlezen zonder extra calls kan.
/// Alle JSON-velden worden defensief gelezen: Growatt wisselt veldnamen per firmware en type.
/// </summary>
public sealed class GrowattConnector(IHttpClientFactory httpClientFactory, ILogger<GrowattConnector> logger) : IDeviceConnector
{
    public const string HttpClientName = "growatt";
    public const string DefaultBaseUrl = "https://openapi.growatt.com/v1/";

    private const string KindTlx = "tlx";
    private const string KindMix = "mix";

    public DeviceBrand Brand => DeviceBrand.Growatt;

    public async Task<ConnectResult> ConnectAsync(ConnectRequest request, CancellationToken ct)
    {
        var token = request.Credentials.Get("token");
        if (token is null)
        {
            return ConnectResult.Fail("Vul je Growatt API-token in (ShinePhone → Ik → API Token).");
        }

        var client = CreateClient(token);

        try
        {
            using var plantsDoc = await GetAsync(client, "plant/list", ct);
            var plants = ExtractArray(plantsDoc.RootElement, "data.plants", "data.data", "data");
            if (plants.Count == 0)
            {
                return ConnectResult.Fail("Geen installaties gevonden bij dit Growatt-account.");
            }

            var devices = new List<DiscoveredDevice>();
            foreach (var plant in plants)
            {
                var plantId = plant.GetStringOrNull("plant_id", "plantId", "id");
                if (plantId is null)
                {
                    continue;
                }

                var plantName = plant.GetStringOrNull("name", "plant_name", "plantName") ?? $"Installatie {plantId}";

                using var devicesDoc = await GetAsync(client, $"device/list?plant_id={Uri.EscapeDataString(plantId)}", ct);
                var list = ExtractArray(devicesDoc.RootElement, "data.devices", "data.data", "data");

                foreach (var dev in list)
                {
                    var sn = dev.GetStringOrNull("device_sn", "deviceSn", "sn", "serial_num");
                    if (sn is null)
                    {
                        continue;
                    }

                    var kind = DetermineKind(dev);
                    var model = dev.GetStringOrNull("model", "device_model", "deviceModel") ?? kind.ToUpperInvariant();

                    var extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["plant_id"] = plantId,
                        ["device_kind"] = kind
                    };

                    devices.Add(new DiscoveredDevice(
                        ExternalId: sn,
                        Name: $"Growatt {model} ({plantName})",
                        Type: DeviceType.Inverter,
                        ExtraCredentials: extra));

                    // Hybride SPH/MIX heeft een batterij: lever die als apart apparaat mee onder hetzelfde serienummer.
                    if (kind == KindMix)
                    {
                        devices.Add(new DiscoveredDevice(
                            ExternalId: sn,
                            Name: $"Growatt batterij ({plantName})",
                            Type: DeviceType.Battery,
                            ExtraCredentials: extra));
                    }
                }
            }

            return devices.Count == 0
                ? ConnectResult.Fail("Geen omvormers gevonden bij dit Growatt-account.")
                : ConnectResult.Ok(devices);
        }
        catch (ConnectorException ex)
        {
            return ConnectResult.Fail(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Growatt niet bereikbaar bij koppelen");
            return ConnectResult.Fail("Growatt is op dit moment niet bereikbaar. Probeer het later opnieuw.");
        }
    }

    public async Task<DeviceReading> ReadAsync(Device device, DeviceCredentials credentials, CancellationToken ct)
    {
        var token = credentials.Get("token") ?? throw new ConnectorException("Growatt-token ontbreekt.");
        var kind = credentials.Get("device_kind") ?? KindTlx;
        var sn = Uri.EscapeDataString(device.ExternalId);

        var client = CreateClient(token);
        var path = kind == KindMix
            ? $"device/mix/mix_last_data?mix_sn={sn}"
            : $"device/tlx/tlx_last_data?tlx_sn={sn}";

        try
        {
            using var doc = await GetAsync(client, path, ct);
            var root = doc.RootElement;

            // Het antwoord is meestal { error_code, error_msg, data: { ... } }; soms zit de meting nog een laag dieper.
            if (!root.TryGetPath("data", out var data))
            {
                data = root;
            }

            return kind == KindMix ? ParseMix(device, data) : ParseTlx(device, data);
        }
        catch (HttpRequestException ex)
        {
            throw new ConnectorException("Growatt niet bereikbaar.", ex);
        }
        catch (JsonException ex)
        {
            throw new ConnectorException("Onverwacht antwoord van Growatt.", ex);
        }
    }

    // --- Parsers -------------------------------------------------------------

    /// <summary>MIN/TLX: string-omvormer, eventueel met backup-/batterij-velden.</summary>
    private static DeviceReading ParseTlx(Device device, JsonElement data)
    {
        var pac = data.GetDoubleOrZero("pac", "pAc");
        var ppv = data.GetDoubleOrNull("ppv", "pPv") ?? pac;
        var load = data.GetDoubleOrZero("pLocalLoad", "plocalLoad", "pLoad", "pSelf");
        var toGrid = data.GetDoubleOrZero("pactogrid", "pacToGrid", "pacToGridTotal");
        var fromGrid = data.GetDoubleOrZero("pactouser", "pacToUser", "pacToUserTotal");
        var charge = data.GetDoubleOrZero("bdc1ChargePower", "pCharge", "chargePower");
        var discharge = data.GetDoubleOrZero("bdc1DischargePower", "pDischarge", "dischargePower");
        var soc = data.GetDoubleOrNull("bdc1Soc", "SOC", "soc", "capacity");

        var grid = fromGrid - toGrid;
        var battery = discharge - charge;

        // Als de omvormer geen huisverbruik meldt, schatten we het uit de balans.
        if (load <= 0)
        {
            load = Math.Max(0, ppv + grid + battery);
        }

        return new DeviceReading(
            Timestamp: ParseTimestamp(data),
            PvPowerW: ppv,
            LoadPowerW: load,
            GridPowerW: grid,
            BatteryPowerW: device.Type == DeviceType.Battery ? battery : 0,
            BatterySoc: device.Type == DeviceType.Battery ? soc : null,
            PvEnergyWh: ToWh(data.GetDoubleOrNull("epvTotal", "epv_total", "eTotal", "energyTotal")),
            ImportEnergyWh: ToWh(data.GetDoubleOrNull("etoUserTotal", "etouser_total")),
            ExportEnergyWh: ToWh(data.GetDoubleOrNull("etoGridTotal", "etogrid_total")));
    }

    /// <summary>SPH/MIX: hybride omvormer met batterij. Zelfde apparaat levert zowel Inverter- als Battery-meting.</summary>
    private static DeviceReading ParseMix(Device device, JsonElement data)
    {
        var ppv = data.GetDoubleOrNull("ppv", "pPv") ?? data.GetDoubleOrZero("pac");
        var load = data.GetDoubleOrZero("pLocalLoad", "plocalLoad", "pLoad");
        var toGrid = data.GetDoubleOrZero("pactogrid", "pacToGrid", "pacToGridTotal");
        var fromGrid = data.GetDoubleOrZero("pactouser", "pacToUser", "pacToUserTotal");
        var charge = data.GetDoubleOrZero("pCharge1", "pcharge1", "chargePower", "pCharge");
        var discharge = data.GetDoubleOrZero("pDischarge1", "pdisCharge1", "dischargePower", "pDischarge");
        var soc = data.GetDoubleOrNull("SOC", "soc", "capacity");

        var grid = fromGrid - toGrid;
        var battery = discharge - charge;

        if (load <= 0)
        {
            load = Math.Max(0, ppv + grid + battery);
        }

        var isBattery = device.Type == DeviceType.Battery;

        // Voorkom dubbeltelling in het huishoudentotaal: de omvormer-meting draagt zon/net/verbruik,
        // de batterij-meting alleen batterijvermogen en SoC.
        return new DeviceReading(
            Timestamp: ParseTimestamp(data),
            PvPowerW: isBattery ? 0 : ppv,
            LoadPowerW: isBattery ? 0 : load,
            GridPowerW: isBattery ? 0 : grid,
            BatteryPowerW: isBattery ? battery : 0,
            BatterySoc: isBattery ? soc : null,
            PvEnergyWh: isBattery ? null : ToWh(data.GetDoubleOrNull("epvTotal", "epv_total", "eTotal")),
            ImportEnergyWh: isBattery ? null : ToWh(data.GetDoubleOrNull("etoUserTotal", "etouser_total")),
            ExportEnergyWh: isBattery ? null : ToWh(data.GetDoubleOrNull("etoGridTotal", "etogrid_total")),
            BatteryChargeEnergyWh: isBattery ? ToWh(data.GetDoubleOrNull("echargeTotal", "echarge_total", "eChargeTotal")) : null,
            BatteryDischargeEnergyWh: isBattery ? ToWh(data.GetDoubleOrNull("edischargeTotal", "edischarge_total", "eDischargeTotal")) : null);
    }

    // --- Hulpfuncties ---------------------------------------------------------

    private HttpClient CreateClient(string token)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress ??= new Uri(DefaultBaseUrl);
        client.DefaultRequestHeaders.Remove("token");
        client.DefaultRequestHeaders.Add("token", token);
        return client;
    }

    private static async Task<JsonDocument> GetAsync(HttpClient client, string path, CancellationToken ct)
    {
        HttpResponseMessage raw;
        try
        {
            raw = await client.GetAsync(path, ct);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ConnectorException("Growatt reageert niet (time-out). Probeer het later opnieuw.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ConnectorException("Growatt is op dit moment niet bereikbaar. Probeer het later opnieuw.", ex);
        }

        using var response = raw;
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized || response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            throw new ConnectorException("Growatt-token is ongeldig of verlopen.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new ConnectorException($"Growatt gaf HTTP {(int)response.StatusCode}.");
        }

        JsonDocument doc;
        try
        {
            var stream = await response.Content.ReadAsStreamAsync(ct);
            doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
        catch (JsonException ex)
        {
            throw new ConnectorException("Onverwacht antwoord van Growatt (geen geldige JSON).", ex);
        }

        // Growatt geeft HTTP 200 met error_code != 0 bij fouten.
        var errorCode = doc.RootElement.GetDoubleOrNull("error_code", "errorCode");
        if (errorCode is not null && errorCode != 0)
        {
            var msg = doc.RootElement.GetStringOrNull("error_msg", "errorMsg") ?? $"foutcode {errorCode}";
            doc.Dispose();
            throw new ConnectorException((int)errorCode.Value is 10011 or 10012
                ? "Growatt-token is ongeldig of verlopen."
                : $"Growatt meldt een fout: {msg}");
        }

        return doc;
    }

    /// <summary>Haalt een array op uit het eerste pad dat bestaat; accepteert ook een object met de items als waarden.</summary>
    private static List<JsonElement> ExtractArray(JsonElement root, params string[] paths)
    {
        foreach (var path in paths)
        {
            if (!root.TryGetPath(path, out var el))
            {
                continue;
            }

            if (el.ValueKind == JsonValueKind.Array)
            {
                return el.EnumerateArray().ToList();
            }
        }

        return [];
    }

    /// <summary>MIN/TLX en SPH/MIX kennen we aan het type- of modelveld; onbekend valt terug op tlx.</summary>
    private static string DetermineKind(JsonElement device)
    {
        var typeField = device.GetStringOrNull("type", "device_type", "deviceType") ?? string.Empty;
        var model = device.GetStringOrNull("model", "device_model", "deviceModel") ?? string.Empty;
        var haystack = (typeField + " " + model).ToUpperInvariant();

        if (haystack.Contains("MIX") || haystack.Contains("SPH") || haystack.Contains("SPA"))
        {
            return KindMix;
        }

        // Growatt type-codes: 1 = inverter, 5 = mix, 7 = tlx/min (ervaringswaarden).
        if (typeField == "5")
        {
            return KindMix;
        }

        return KindTlx;
    }

    /// <summary>
    /// Growatt geeft lokale tijd zonder zone (<c>yyyy-MM-dd HH:mm:ss</c>). Voor Nederlandse installaties is dat Europe/Amsterdam.
    /// Een tijd in de toekomst (klokafwijking van de datalogger) wordt "nu". Publiek voor tests.
    /// </summary>
    internal static DateTimeOffset ParseTimestamp(JsonElement data, DateTimeOffset? now = null)
    {
        var utcNow = now ?? DateTimeOffset.UtcNow;
        var raw = data.GetStringOrNull("time", "lastUpdateTime", "last_update_time", "dataLogTime");
        if (raw is not null
            && DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            var local = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
            var ts = new DateTimeOffset(local, Services.DutchTime.Zone.GetUtcOffset(local));
            return ts > utcNow ? utcNow : ts;
        }

        return utcNow;
    }

    /// <summary>Growatt-tellers zijn in kWh; wij slaan Wh op.</summary>
    private static double? ToWh(double? kwh) => kwh is null ? null : kwh * 1000d;
}
