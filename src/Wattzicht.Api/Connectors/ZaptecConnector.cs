using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Connectors;

/// <summary>
/// Zaptec laadpaal via de Zaptec-cloud (api.zaptec.com).
///
/// Inloggen gaat met het e-mailadres en wachtwoord van het Zaptec-account van de bewoner, precies
/// zoals in zijn eigen app: <c>POST /oauth/token</c> met een password grant. Daarna <c>/api/chargers</c>
/// voor de palen en <c>/api/chargers/{id}/state</c> voor wat hij nu doet.
///
/// Waarom niet dezelfde code als bij LaadCash: die haalt laadbeurten op nadat ze klaar zijn
/// (<c>/api/chargehistory</c>), want daar gaat het om kilowatturen voor de inboeking. Hier is een
/// momentopname nodig, elke paar minuten, want dit scherm laat zien wat er nu door het huis loopt.
/// De inlog is wel letterlijk dezelfde route, inclusief de les die daar geleerd is: Zaptec knijpt af
/// op het IP-adres van de aanvrager en niet op het account, en al onze klanten lopen over één server.
/// Vandaar hetzelfde slot met een vaste tussenpoos tussen twee inlogpogingen.
/// </summary>
public sealed class ZaptecConnector(IHttpClientFactory httpClientFactory, ILogger<ZaptecConnector> logger) : IDeviceConnector
{
    public const string HttpClientName = "zaptec";
    public const string DefaultBaseUrl = "https://api.zaptec.com";

    /// <summary>
    /// Zaptec gaf in productie bij LaadCash een 503 van hun nginx op de derde inlog kort achter
    /// elkaar vanaf hetzelfde adres. Inloggen gebeurt daarom één voor één, met een paar tellen ertussen.
    /// </summary>
    private static readonly SemaphoreSlim Inlogslot = new(1, 1);
    private static readonly TimeSpan Tussenpoos = TimeSpan.FromSeconds(5);
    private static DateTimeOffset _laatsteInlog = DateTimeOffset.MinValue;

    /// <summary>
    /// De waarden die wij uit <c>/state</c> gebruiken. Zaptec levert daar een lijst
    /// <c>{ StateId, ValueAsString }</c>; de nummers zijn hun vaste observatie-ids.
    /// </summary>
    private const int TotalChargePower = 513;          // watt, wat er nu door de paal gaat
    private const int TotalChargePowerSession = 553;   // kWh van de lopende laadbeurt
    private const int SignedMeterValueKwh = 554;       // meterstand van de paal in kWh, loopt door

    public DeviceBrand Brand => DeviceBrand.Zaptec;

    public async Task<ConnectResult> ConnectAsync(ConnectRequest request, CancellationToken ct)
    {
        var gebruiker = request.Credentials.Get("username");
        var wachtwoord = request.Credentials.Get("password");

        if (string.IsNullOrWhiteSpace(gebruiker) || string.IsNullOrWhiteSpace(wachtwoord))
        {
            return ConnectResult.Fail("Vul het e-mailadres en wachtwoord in waarmee je in de Zaptec-app inlogt.");
        }

        try
        {
            var token = await TokenAsync(gebruiker, wachtwoord, ct);
            using var doc = await HaalAsync(token, "/api/chargers?PageSize=100&PageIndex=0", ct);

            var palen = new List<DiscoveredDevice>();
            if (doc.RootElement.TryGetPropertyIgnoreCase("Data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var paal in data.EnumerateArray())
                {
                    var id = paal.GetStringOrNull("Id", "DeviceId");
                    if (id is null) continue;

                    var naam = paal.GetStringOrNull("Name", "DeviceId") ?? "Zaptec";
                    palen.Add(new DiscoveredDevice(id, naam, DeviceType.Charger));
                }
            }

            return palen.Count == 0
                ? ConnectResult.Fail("Geen laadpaal gevonden op dit Zaptec-account. Klopt het account, en staat de paal erin?")
                : ConnectResult.Ok(palen);
        }
        catch (ConnectorException ex)
        {
            return ConnectResult.Fail(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Zaptec niet bereikbaar bij het koppelen");
            return ConnectResult.Fail("Zaptec is nu niet bereikbaar. Probeer het straks nog eens.");
        }
    }

    public async Task<DeviceReading> ReadAsync(Device device, DeviceCredentials credentials, CancellationToken ct)
    {
        var gebruiker = credentials.Get("username");
        var wachtwoord = credentials.Get("password");
        if (string.IsNullOrWhiteSpace(gebruiker) || string.IsNullOrWhiteSpace(wachtwoord))
        {
            throw new ConnectorException("De Zaptec-inloggegevens ontbreken. Koppel de laadpaal opnieuw.");
        }

        var token = await TokenAsync(gebruiker, wachtwoord, ct);
        using var doc = await HaalAsync(token, $"/api/chargers/{Uri.EscapeDataString(device.ExternalId)}/state", ct);

        return LeesStand(doc.RootElement, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Het antwoord van <c>/state</c> omzetten naar een meting. Apart en internal, zodat een test hem
    /// kan voeren met een echt antwoord zonder dat er een Zaptec-account aan te pas komt.
    /// </summary>
    internal static DeviceReading LeesStand(JsonElement root, DateTimeOffset nu)
    {
        var waarden = new Dictionary<int, string>();
        var lijst = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetPropertyIgnoreCase("Data", out var d) ? d : default;

        if (lijst.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in lijst.EnumerateArray())
            {
                var id = item.GetDoubleOrNull("StateId", "stateId");
                var waarde = item.GetStringOrNull("ValueAsString", "valueAsString", "Value");
                if (id is not null && waarde is not null) waarden[(int)id.Value] = waarde;
            }
        }

        // Vermogen in watt. Een laadpaal is altijd verbruik, nooit opwek, dus dit telt op bij de
        // belasting van het huis; het teken blijft positief.
        var watt = Getal(waarden, TotalChargePower) ?? 0;

        // De meterstand loopt door over laadbeurten heen en is dus bruikbaar als teller. De waarde van
        // de lopende beurt niet: die springt bij elke nieuwe beurt terug naar nul, en een teller die
        // terugloopt levert bij het verschil nemen negatieve kilowatturen op.
        var meterKwh = Getal(waarden, SignedMeterValueKwh);

        return new DeviceReading(
            Timestamp: nu,
            LoadPowerW: watt,
            ChargerPowerW: watt,
            ChargerEnergyWh: meterKwh is null ? null : meterKwh.Value * 1000);
    }

    private static double? Getal(IReadOnlyDictionary<int, string> waarden, int stateId) =>
        waarden.TryGetValue(stateId, out var tekst)
        && double.TryParse(tekst, NumberStyles.Float, CultureInfo.InvariantCulture, out var g)
            ? g
            : null;

    private async Task<string> TokenAsync(string gebruiker, string wachtwoord, CancellationToken ct)
    {
        await Inlogslot.WaitAsync(ct);
        try
        {
            var sinds = DateTimeOffset.UtcNow - _laatsteInlog;
            if (sinds < Tussenpoos) await Task.Delay(Tussenpoos - sinds, ct);
            _laatsteInlog = DateTimeOffset.UtcNow;

            var client = httpClientFactory.CreateClient(HttpClientName);
            using var inhoud = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["username"] = gebruiker,
                ["password"] = wachtwoord,
            });

            using var antwoord = await client.PostAsync("/oauth/token", inhoud, ct);
            if (antwoord.StatusCode == System.Net.HttpStatusCode.BadRequest
                || antwoord.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                throw new ConnectorException("Zaptec accepteert dit e-mailadres of wachtwoord niet. Werkt het wel in de Zaptec-app zelf?");
            }

            if (!antwoord.IsSuccessStatusCode)
            {
                throw new ConnectorException("Zaptec geeft nu geen antwoord op het inloggen. Probeer het straks nog eens.");
            }

            using var doc = JsonDocument.Parse(await antwoord.Content.ReadAsStringAsync(ct));
            var token = doc.RootElement.GetStringOrNull("access_token");
            return token ?? throw new ConnectorException("Zaptec gaf geen toegangssleutel terug.");
        }
        finally
        {
            Inlogslot.Release();
        }
    }

    private async Task<JsonDocument> HaalAsync(string token, string pad, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var verzoek = new HttpRequestMessage(HttpMethod.Get, pad);
        verzoek.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var antwoord = await client.SendAsync(verzoek, ct);
        if (!antwoord.IsSuccessStatusCode)
        {
            throw new ConnectorException($"Zaptec antwoordde met {(int)antwoord.StatusCode} op {pad}.");
        }

        return JsonDocument.Parse(await antwoord.Content.ReadAsStringAsync(ct));
    }
}
