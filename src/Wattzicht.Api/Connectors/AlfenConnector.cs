using System.Text.Json;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Connectors;

/// <summary>
/// Een Alfen-laadpaal uitlezen over het thuisnetwerk.
///
/// Alfen heeft geen cloud waar wij bij mogen. Wie zijn paal via 50five, E-Flux of een
/// leasemaatschappij heeft, ziet zijn data alleen in hún portaal. Maar de paal zelf heeft een lokale
/// interface, dezelfde die de monteur gebruikt: <c>https://{ip}/api/login</c> met gebruiker
/// <c>admin</c> en het wachtwoord van de paal, daarna <c>GET /api/prop?id=2221_22</c> voor de stand
/// van de ingebouwde meter in kWh, en netjes <c>/api/logout</c>.
///
/// Drie dingen om te weten voordat je dit verwacht te laten werken:
/// 1. De paal laat maar één sessie tegelijk toe en koppelt die aan het IP-adres van wie inlogt.
///    Daarom loggen we per uitlezing in en meteen weer uit. Staat de ACE Service Installer-app van
///    de monteur open, dan lukt het niet.
/// 2. Het certificaat van de paal is zelfondertekend en staat op een IP-adres. Daar is geen enkele
///    controle op mogelijk, dus die zetten we voor dit ene adres uit.
/// 3. Zit de paal met een simkaart aan de backoffice en hangt hij verder niet aan de wifi of een
///    netwerkkabel thuis, dan heeft hij op het thuisnetwerk geen adres en is er niets te bereiken.
///    Dan is dit geen bug maar een feit, en moet de paal eerst aan het thuisnetwerk.
///
/// Dit kan alleen vanuit het huis zelf, dus de app doet het uitlezen, net als bij Sessy.
/// Modbus TCP op poort 502 kan ook, maar dat vereist de licentie Active Load Balancing én dat een
/// monteur het aanzet, en het bijt met load balancing via de P1. Deze route vraagt alleen het
/// wachtwoord dat de eigenaar van zijn installateur hoort te krijgen.
/// </summary>
public sealed class AlfenConnector(IHttpClientFactory httpClientFactory, ILogger<AlfenConnector> logger) : IDeviceConnector
{
    /// <summary>Aparte client: deze accepteert het zelfondertekende certificaat van een laadpaal.</summary>
    public const string HttpClientName = "alfen-lokaal";

    /// <summary>Stand van de ingebouwde meter van socket 1, in kWh. Socket 2 is 3221_22.</summary>
    internal const string MeterstandSocket1 = "2221_22";
    internal const string MeterstandSocket2 = "3221_22";

    /// <summary>Actief vermogen totaal, in watt.</summary>
    internal const string VermogenTotaal = "2221_16";

    /// <summary>Naam van de paal, zoals de eigenaar hem in de app ziet.</summary>
    internal const string PaalNaam = "2062_0";

    public DeviceBrand Brand => DeviceBrand.Alfen;

    /// <summary>
    /// Een laadpaal thuis hangt op een adres als 192.168.1.42. Onze server kan daar niet bij en moet
    /// het ook niet proberen: dat levert alleen elke vijf minuten een mislukte poging op.
    /// </summary>
    public bool IsLokaalVoor(IReadOnlyDictionary<string, string> credentials) =>
        ThuisAdres.IsPrive(credentials.Get("host"));

    /// <summary>De app mag een Alfen altijd aanleveren.</summary>
    public bool KanDoorDeAppGelezenWorden => true;

    public DeviceReading LeesLokaal(JsonElement rauw, DateTimeOffset gemeten) =>
        Parse(rauw) with { Timestamp = gemeten };

    public async Task<ConnectResult> ConnectAsync(ConnectRequest request, CancellationToken ct)
    {
        var host = request.Credentials.Get("host");
        var wachtwoord = request.Credentials.Get("password");
        var gebruiker = request.Credentials.Get("username") ?? "admin";

        if (host is null || wachtwoord is null)
        {
            return ConnectResult.Fail(
                "Vul het IP-adres van je Alfen en het wachtwoord in. Het wachtwoord krijg je van je installateur; "
                + "het is hetzelfde wachtwoord waarmee de monteur met de ACE Service Installer op de paal inlogt.");
        }

        try
        {
            using var doc = await LeesEigenschappenAsync(host, gebruiker, wachtwoord,
                [MeterstandSocket1, VermogenTotaal, PaalNaam], ct);

            var naam = Waarde(doc.RootElement, PaalNaam)?.ToString() ?? "Alfen laadpaal";
            if (Getal(doc.RootElement, MeterstandSocket1) is null)
            {
                return ConnectResult.Fail(
                    "De paal antwoordt wel, maar geeft geen meterstand terug. Dat gebeurt bij een uitvoering zonder "
                    + "ingebouwde meter. Laat ons weten welk type het is, dan kijken we mee.");
            }

            // Het serienummer staat niet in deze eigenschappen; het IP-adres is binnen één huis uniek
            // genoeg om de paal te herkennen, en de naam zetten we erbij zodat hij herkenbaar is.
            return ConnectResult.Ok([new DiscoveredDevice(host, naam, DeviceType.Charger)]);
        }
        catch (ConnectorException ex)
        {
            return ConnectResult.Fail(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Alfen op {Host} niet bereikbaar bij koppelen", host);
            return ConnectResult.Fail(
                $"Op {host} is geen Alfen te bereiken. Hangt de paal met een simkaart aan zijn backoffice en verder "
                + "niet aan je wifi of netwerkkabel, dan heeft hij hier geen adres en kan dit niet werken.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return ConnectResult.Fail($"De Alfen op {host} reageert niet (time-out).");
        }
        catch (JsonException)
        {
            return ConnectResult.Fail($"Op {host} antwoordt iets anders dan een Alfen. Controleer het adres.");
        }
    }

    public async Task<DeviceReading> ReadAsync(Device device, DeviceCredentials credentials, CancellationToken ct)
    {
        var host = credentials.Get("host") ?? throw new ConnectorException("Adres van de Alfen ontbreekt.");
        var wachtwoord = credentials.Get("password") ?? throw new ConnectorException("Wachtwoord van de Alfen ontbreekt.");
        var gebruiker = credentials.Get("username") ?? "admin";

        try
        {
            using var doc = await LeesEigenschappenAsync(host, gebruiker, wachtwoord,
                [MeterstandSocket1, MeterstandSocket2, VermogenTotaal], ct);
            return Parse(doc.RootElement);
        }
        catch (HttpRequestException ex)
        {
            throw new ConnectorException($"Alfen op {host} niet bereikbaar.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ConnectorException($"Alfen op {host} reageert niet (time-out).", ex);
        }
        catch (JsonException ex)
        {
            throw new ConnectorException("Onverwacht antwoord van de Alfen.", ex);
        }
    }

    /// <summary>
    /// Zet het antwoord van de paal om naar onze meting.
    ///
    /// De meterstand komt in kWh binnen en wij bewaren tellers in Wh. Twee sockets tellen we op: een
    /// paal met twee aansluitingen levert twee standen, en voor de besparing gaat het om alles wat er
    /// uit die paal is gegaan.
    /// </summary>
    internal static DeviceReading Parse(JsonElement root)
    {
        var kwh = (Getal(root, MeterstandSocket1) ?? 0) + (Getal(root, MeterstandSocket2) ?? 0);
        var vermogen = Getal(root, VermogenTotaal) ?? 0;

        return new DeviceReading(
            DateTimeOffset.UtcNow,
            ChargerPowerW: vermogen,
            ChargerEnergyWh: kwh > 0 ? kwh * 1000 : null);
    }

    /// <summary>
    /// Inloggen, de gevraagde eigenschappen ophalen, uitloggen. In die volgorde en altijd uitloggen,
    /// want de paal laat maar één sessie tegelijk toe: blijft die hangen, dan kan niemand anders erbij,
    /// ook de eigenaar zelf niet met de app van Alfen.
    /// </summary>
    private async Task<JsonDocument> LeesEigenschappenAsync(string host, string gebruiker, string wachtwoord,
        string[] eigenschappen, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        var basis = $"https://{host}";

        var login = await client.PostAsJsonAsync($"{basis}/api/login", new
        {
            username = gebruiker,
            password = wachtwoord,
            displayname = "Wattzicht",
        }, ct);

        if (login.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            login.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            throw new ConnectorException(
                "De Alfen weigert het wachtwoord. Dit is niet het wachtwoord van je laadpas of van het portaal van "
                + "je leverancier, maar dat van de paal zelf. Je installateur heeft het.");
        }

        login.EnsureSuccessStatusCode();

        try
        {
            var samen = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in eigenschappen)
            {
                var antwoord = await client.GetAsync($"{basis}/api/prop?id={Uri.EscapeDataString(id)}", ct);
                if (!antwoord.IsSuccessStatusCode) continue;

                using var deel = JsonDocument.Parse(await antwoord.Content.ReadAsStringAsync(ct));
                if (!deel.RootElement.TryGetProperty("properties", out var lijst)) continue;

                foreach (var eigenschap in lijst.EnumerateArray())
                {
                    if (eigenschap.TryGetProperty("id", out var eid) && eid.GetString() is { } sleutel &&
                        eigenschap.TryGetProperty("value", out var waarde))
                    {
                        samen[sleutel] = waarde.Clone();
                    }
                }
            }

            return JsonSerializer.SerializeToDocument(samen);
        }
        finally
        {
            // Best effort: lukt uitloggen niet, dan vervalt de sessie vanzelf, maar dan zit de paal
            // tot die tijd op slot. Daarom proberen we het ook als het ophalen misging.
            try { await client.PostAsync($"{basis}/api/logout", null, CancellationToken.None); }
            catch (Exception ex) { logger.LogDebug(ex, "Uitloggen bij Alfen {Host} mislukt", host); }
        }
    }

    private static JsonElement? Waarde(JsonElement root, string id) =>
        root.TryGetProperty(id, out var waarde) ? waarde : null;

    private static double? Getal(JsonElement root, string id)
    {
        if (Waarde(root, id) is not { } waarde) return null;
        return waarde.ValueKind switch
        {
            JsonValueKind.Number => waarde.GetDouble(),
            JsonValueKind.String when double.TryParse(waarde.GetString(),
                System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) => d,
            _ => null,
        };
    }
}
