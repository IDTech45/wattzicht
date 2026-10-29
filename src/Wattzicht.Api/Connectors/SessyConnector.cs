using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Connectors;

/// <summary>
/// Sessy thuisbatterij via de lokale API: <c>GET http://{host}/api/v1/power/status</c> met basic auth
/// (gebruikersnaam en wachtwoord staan op de dongle). Credentials: <c>host</c>, <c>username</c>, <c>password</c>.
/// In v0.1 pollt de API de batterij; dat werkt alleen als de API de batterij kan bereiken (VPN/poort-forward of lokaal netwerk).
/// </summary>
public sealed class SessyConnector(IHttpClientFactory httpClientFactory, ILogger<SessyConnector> logger) : IDeviceConnector
{
    public const string HttpClientName = "sessy";

    public DeviceBrand Brand => DeviceBrand.Sessy;

    public async Task<ConnectResult> ConnectAsync(ConnectRequest request, CancellationToken ct)
    {
        var host = request.Credentials.Get("host");
        var username = request.Credentials.Get("username");
        var password = request.Credentials.Get("password");

        if (host is null || username is null || password is null)
        {
            return ConnectResult.Fail("Vul het IP-adres, de gebruikersnaam en het wachtwoord van je Sessy in (staan op de dongle).");
        }

        try
        {
            using var doc = await FetchStatusAsync(host, username, password, ct);
            var root = doc.RootElement;

            // Serienummer is niet altijd aanwezig in power/status; val terug op de gebruikersnaam (uniek per Sessy).
            var serial = root.GetStringOrNull("serial", "serial_number", "sn") ?? username;

            var devices = new List<DiscoveredDevice>
            {
                new(serial, "Sessy thuisbatterij", DeviceType.Battery)
            };

            return ConnectResult.Ok(devices);
        }
        catch (ConnectorException ex)
        {
            return ConnectResult.Fail(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Sessy op {Host} niet bereikbaar bij koppelen", host);
            return ConnectResult.Fail($"Sessy op {host} is niet bereikbaar. Controleer het IP-adres en of de batterij aanstaat.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return ConnectResult.Fail($"Sessy op {host} reageert niet (time-out).");
        }
    }

    public async Task<DeviceReading> ReadAsync(Device device, IReadOnlyDictionary<string, string> credentials, CancellationToken ct)
    {
        var host = credentials.Get("host") ?? throw new ConnectorException("Sessy-host ontbreekt.");
        var username = credentials.Get("username") ?? throw new ConnectorException("Sessy-gebruikersnaam ontbreekt.");
        var password = credentials.Get("password") ?? throw new ConnectorException("Sessy-wachtwoord ontbreekt.");

        try
        {
            using var doc = await FetchStatusAsync(host, username, password, ct);
            return Parse(doc.RootElement);
        }
        catch (HttpRequestException ex)
        {
            throw new ConnectorException($"Sessy op {host} niet bereikbaar.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ConnectorException($"Sessy op {host} reageert niet (time-out).", ex);
        }
        catch (JsonException ex)
        {
            throw new ConnectorException("Onverwacht antwoord van Sessy.", ex);
        }
    }

    /// <summary>
    /// Normaliseert het Sessy-antwoord. Sessy: <c>sessy.power</c> positief = ontladen (zelfde als onze conventie).
    /// Zon komt uit <c>renewable_energy_phase1..3.power_w</c>; net uit <c>grid_phase1..3.power_w</c> of <c>sessy.power_setpoint</c>-achtige velden.
    /// </summary>
    internal static DeviceReading Parse(JsonElement root)
    {
        var sessy = root.TryGetPropertyIgnoreCase("sessy", out var s) ? s : root;

        var batteryPower = sessy.GetDoubleOrZero("power");
        var soc = sessy.GetDoubleOrNull("state_of_charge");
        if (soc is not null && soc <= 1.0)
        {
            soc *= 100; // Sessy geeft een fractie 0–1.
        }

        var pv = 0d;
        var grid = 0d;
        var hasGrid = false;

        for (var phase = 1; phase <= 3; phase++)
        {
            if (root.TryGetPropertyIgnoreCase($"renewable_energy_phase{phase}", out var re))
            {
                pv += re.GetDoubleOrZero("power_w", "power");
            }

            if (root.TryGetPropertyIgnoreCase($"grid_phase{phase}", out var gp))
            {
                grid += gp.GetDoubleOrZero("power_w", "power");
                hasGrid = true;
            }
        }

        // Sommige firmwares geven totalen direct.
        if (!hasGrid)
        {
            grid = root.GetDoubleOrZero("grid_power_w", "total_power");
        }

        // Verbruik = zon + ontladen + netafname (negatieve termen zijn teruglevering/laden).
        var load = Math.Max(0, pv + batteryPower + grid);

        // Een eventuele system_state "ERROR" negeren we hier bewust: de meting blijft bruikbaar
        // en de AlertService signaleert problemen op basis van de data zelf.
        return new DeviceReading(
            Timestamp: DateTimeOffset.UtcNow,
            PvPowerW: pv,
            LoadPowerW: load,
            GridPowerW: grid,
            BatteryPowerW: batteryPower,
            BatterySoc: soc);
    }

    private async Task<JsonDocument> FetchStatusAsync(string host, string username, string password, CancellationToken ct)
    {
        var baseUri = host.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                      || host.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? host.TrimEnd('/')
            : $"http://{host}";

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUri}/api/v1/power/status");
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        using var response = await client.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            throw new ConnectorException("Gebruikersnaam of wachtwoord van de Sessy klopt niet.");
        }

        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }
}
