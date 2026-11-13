using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Connectors;

/// <summary>
/// Easee laadpaal via de Easee-cloud (api.easee.com).
///
/// Inloggen met het e-mailadres en wachtwoord van het Easee-account van de bewoner:
/// <c>POST /api/accounts/login</c> geeft een access- en een refresh-token. Daarna
/// <c>GET /api/chargers</c> voor de palen en <c>GET /api/chargers/{id}/state</c> voor wat de paal nu doet.
///
/// Waarom niet dezelfde code als bij LaadCash: die haalt afgeronde laadbeurten op
/// (<c>/api/sessions/charger/...</c>) omdat het daar om kilowatturen voor de inboeking gaat. Hier is
/// een momentopname nodig, elke paar minuten, want dit scherm laat zien wat er nu door het huis loopt.
/// De inlogroute is wel letterlijk dezelfde, en die is in productie bij LaadCash geverifieerd.
///
/// Het access-token gaat ongeveer een dag mee en we bewaren het, met zijn refresh-token, in de
/// credentials van het apparaat. Zo hoeft er niet bij elke uitlezing opnieuw met het wachtwoord te
/// worden ingelogd: dat is onnodig verkeer en Easee ziet het liever niet.
/// </summary>
public sealed class EaseeConnector(IHttpClientFactory httpClientFactory, ILogger<EaseeConnector> logger) : IDeviceConnector
{
    public const string HttpClientName = "easee";
    public const string DefaultBaseUrl = "https://api.easee.com";

    /// <summary>Iets eerder vernieuwen dan nodig; een token dat tijdens de aanroep verloopt kost een hele ronde.</summary>
    private static readonly TimeSpan Marge = TimeSpan.FromMinutes(5);

    public DeviceBrand Brand => DeviceBrand.Easee;

    public async Task<ConnectResult> ConnectAsync(ConnectRequest request, CancellationToken ct)
    {
        var gebruiker = request.Credentials.Get("username");
        var wachtwoord = request.Credentials.Get("password");

        if (string.IsNullOrWhiteSpace(gebruiker) || string.IsNullOrWhiteSpace(wachtwoord))
        {
            return ConnectResult.Fail("Vul het e-mailadres en wachtwoord in waarmee je in de Easee-app inlogt.");
        }

        try
        {
            var sleutels = new DeviceCredentials(new Dictionary<string, string>
            {
                ["username"] = gebruiker,
                ["password"] = wachtwoord,
            });

            var token = await TokenAsync(sleutels, ct);
            using var doc = await HaalAsync(token, "/api/chargers", ct);

            var palen = new List<DiscoveredDevice>();
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var paal in doc.RootElement.EnumerateArray())
                {
                    var id = paal.GetStringOrNull("id", "Id");
                    if (id is null) continue;

                    var naam = paal.GetStringOrNull("name", "Name") ?? id;
                    palen.Add(new DiscoveredDevice(id, naam, DeviceType.Charger, new Dictionary<string, string>
                    {
                        // Het verse token meteen mee, zodat de eerste uitlezing niet opnieuw hoeft in te loggen.
                        ["access_token"] = token.Token,
                        ["refresh_token"] = token.Refresh ?? string.Empty,
                        ["token_verloopt"] = token.Verloopt.ToString("O", CultureInfo.InvariantCulture),
                    }));
                }
            }

            return palen.Count == 0
                ? ConnectResult.Fail("Geen laadpaal gevonden op dit Easee-account. Klopt het account, en staat de paal erin?")
                : ConnectResult.Ok(palen);
        }
        catch (ConnectorException ex)
        {
            return ConnectResult.Fail(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Easee niet bereikbaar bij het koppelen");
            return ConnectResult.Fail("Easee is nu niet bereikbaar. Probeer het straks nog eens.");
        }
    }

    public async Task<DeviceReading> ReadAsync(Device device, DeviceCredentials credentials, CancellationToken ct)
    {
        var token = await TokenAsync(credentials, ct);
        using var doc = await HaalAsync(token, $"/api/chargers/{Uri.EscapeDataString(device.ExternalId)}/state", ct);

        return LeesStand(doc.RootElement, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Het antwoord van <c>/state</c> omzetten naar een meting. Apart en internal, zodat een test hem
    /// kan voeren met een echt antwoord zonder dat er een Easee-account aan te pas komt.
    ///
    /// Easee schrijft zijn velden in camelCase, maar niet elk model levert alles. We lezen daarom per
    /// waarde een paar spellingen, en een teller die er niet is blijft null in plaats van nul: nul is
    /// een meting, null is "dit merk zegt het niet", en in een grafiek is dat verschil zichtbaar.
    /// </summary>
    internal static DeviceReading LeesStand(JsonElement root, DateTimeOffset nu)
    {
        // totalPower staat bij Easee in kilowatt, anders dan bij de meeste merken.
        var kw = root.GetDoubleOrNull("totalPower", "TotalPower") ?? 0;
        var watt = kw * 1000;

        // lifetimeEnergy is de meterstand van de paal in kWh en loopt door over laadbeurten heen.
        var meterKwh = root.GetDoubleOrNull("lifetimeEnergy", "LifetimeEnergy");

        return new DeviceReading(
            Timestamp: nu,
            ChargerPowerW: watt,
            LoadPowerW: watt,
            ChargerEnergyWh: meterKwh is { } m ? m * 1000 : null);
    }

    private sealed record Toegang(string Token, string? Refresh, DateTimeOffset Verloopt);

    /// <summary>
    /// Geeft een geldig access-token. Hergebruikt het bewaarde token zolang dat nog even meegaat,
    /// vernieuwt het anders met het refresh-token, en logt alleen als laatste redmiddel opnieuw in met
    /// het wachtwoord.
    /// </summary>
    private async Task<Toegang> TokenAsync(DeviceCredentials credentials, CancellationToken ct)
    {
        var bewaard = credentials.Get("access_token");
        var refresh = credentials.Get("refresh_token");
        var verloopt = Lees(credentials.Get("token_verloopt"));

        if (!string.IsNullOrWhiteSpace(bewaard) && verloopt - Marge > DateTimeOffset.UtcNow)
        {
            return new Toegang(bewaard, refresh, verloopt);
        }

        Toegang? vers = null;

        if (!string.IsNullOrWhiteSpace(bewaard) && !string.IsNullOrWhiteSpace(refresh))
        {
            vers = await VernieuwAsync(bewaard, refresh, ct);
        }

        vers ??= await InlogAsync(credentials, ct);

        // Terugschrijven, zodat de poller het versleuteld bewaart en de volgende ronde niet opnieuw inlogt.
        credentials.Set("access_token", vers.Token);
        credentials.Set("refresh_token", vers.Refresh);
        credentials.Set("token_verloopt", vers.Verloopt.ToString("O", CultureInfo.InvariantCulture));

        return vers;
    }

    private async Task<Toegang?> VernieuwAsync(string token, string refresh, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);

        try
        {
            using var antwoord = await client.PostAsJsonAsync(
                "/api/accounts/refresh_token", new { accessToken = token, refreshToken = refresh }, ct);

            // Een afgewezen refresh is geen fout: het token was gewoon te oud. Dan volgt de gewone inlog.
            return antwoord.IsSuccessStatusCode ? await LeesTokenAsync(antwoord, ct) : null;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Easee: vernieuwen van het token mislukt, we loggen opnieuw in");
            return null;
        }
    }

    private async Task<Toegang> InlogAsync(DeviceCredentials credentials, CancellationToken ct)
    {
        var gebruiker = credentials.Get("username");
        var wachtwoord = credentials.Get("password");

        if (string.IsNullOrWhiteSpace(gebruiker) || string.IsNullOrWhiteSpace(wachtwoord))
        {
            throw new ConnectorException("De Easee-inloggegevens ontbreken. Koppel de laadpaal opnieuw.");
        }

        var client = httpClientFactory.CreateClient(HttpClientName);

        HttpResponseMessage antwoord;
        try
        {
            antwoord = await client.PostAsJsonAsync(
                "/api/accounts/login", new { userName = gebruiker, password = wachtwoord }, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ConnectorException("Easee is nu niet bereikbaar.", ex);
        }

        using (antwoord)
        {
            if (antwoord.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new ConnectorException(
                    "Easee accepteerde het e-mailadres of wachtwoord niet. Werken ze wel in de Easee-app zelf?");
            }

            if (antwoord.StatusCode is HttpStatusCode.TooManyRequests)
            {
                throw new ConnectorException("Easee laat even geen inlogpogingen toe. Probeer het over een kwartier nog eens.");
            }

            if (!antwoord.IsSuccessStatusCode)
            {
                throw new ConnectorException($"Easee gaf een onverwacht antwoord bij het inloggen ({(int)antwoord.StatusCode}).");
            }

            return await LeesTokenAsync(antwoord, ct);
        }
    }

    private static async Task<Toegang> LeesTokenAsync(HttpResponseMessage antwoord, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await antwoord.Content.ReadAsStringAsync(ct));
        var token = doc.RootElement.GetStringOrNull("accessToken", "access_token");

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ConnectorException("Easee gaf geen inlogtoken terug.");
        }

        // expiresIn staat in seconden; valt hij weg, dan houden we het op een uur en vernieuwen we eerder.
        var seconden = doc.RootElement.GetDoubleOrNull("expiresIn", "expires_in") ?? 3600;
        var refresh = doc.RootElement.GetStringOrNull("refreshToken", "refresh_token");

        return new Toegang(token, refresh, DateTimeOffset.UtcNow.AddSeconds(seconden));
    }

    private async Task<JsonDocument> HaalAsync(Toegang toegang, string pad, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);

        using var verzoek = new HttpRequestMessage(HttpMethod.Get, pad);
        verzoek.Headers.Authorization = new AuthenticationHeaderValue("Bearer", toegang.Token);

        using var antwoord = await client.SendAsync(verzoek, ct);

        if (antwoord.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new ConnectorException("Easee wees de koppeling af. Koppel de laadpaal opnieuw.");
        }

        if (!antwoord.IsSuccessStatusCode)
        {
            throw new ConnectorException($"Easee gaf een onverwacht antwoord ({(int)antwoord.StatusCode}).");
        }

        return JsonDocument.Parse(await antwoord.Content.ReadAsStringAsync(ct));
    }

    private static DateTimeOffset Lees(string? waarde) =>
        DateTimeOffset.TryParse(waarde, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)
            ? d
            : DateTimeOffset.MinValue;
}
