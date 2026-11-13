using System.Net;
using System.Text;
using System.Text.Json;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Connectors;

/// <summary>
/// De gegevens van onze Enphase developer-app, uit <c>Connectors:Enphase:*</c>. Als los object zodat
/// de connector net als de andere merken geen IConfiguration nodig heeft.
/// </summary>
/// <param name="ClientId">Client-id van de developer-app (developer-v4.enphase.com).</param>
/// <param name="ClientSecret">Client secret van die app.</param>
/// <param name="ApiKey">De API-sleutel die bij elke aanroep als <c>key=</c> mee moet.</param>
/// <param name="MaxPerMaand">
/// De noodrem. Het gratis plan geeft 1.000 verzoeken per maand en LaadCash deelt dat account mee, dus
/// houden wij er ruim onder: de begroting in de poller mikt op 100 en deze noodrem staat op 150, zodat
/// een fout in die begroting zichtbaar wordt lang voordat de limiet in zicht komt.
/// De normale rem is het uitleesritme in de poller; deze teller is wat er overblijft als dat faalt.
/// </param>
public sealed record EnphaseOptions(string? ClientId, string? ClientSecret, string? ApiKey, int MaxPerMaand = 150);

/// <summary>
/// Hoeveel aanroepen er deze maand op een Enphase-sleutel zijn gedaan.
///
/// Enphase rekent per developer-app en niet per woning: het gratis plan "Watt" geeft 1.000 aanroepen
/// per maand voor alles bij elkaar. Eén uitlezing kost er twee (token vernieuwen plus opvragen), dus
/// dat zijn 500 uitlezingen voor het hele platform. Zonder rem gaat dat stil over de grens en krijgt
/// iedereen tegelijk foutmeldingen die nergens op slaan, ergens halverwege de maand.
///
/// Dit is een rem, geen boekhouding: na een herstart begint de teller opnieuw. Wie er echt op wil
/// kunnen bouwen geeft zijn eigen sleutels op, en die tellen apart.
/// </summary>
internal static class EnphaseQuotum
{
    private static readonly Lock Slot = new();
    private static readonly Dictionary<string, (int Maand, int Aantal)> Stand = [];

    /// <summary>Boekt één aanroep. Geeft false als de grens bereikt is; dan hoort er niets meer uit te gaan.</summary>
    public static bool Boek(string sleutel, int max, DateTimeOffset nu)
    {
        var maand = nu.Year * 12 + nu.Month;
        lock (Slot)
        {
            var (vorigeMaand, aantal) = Stand.TryGetValue(sleutel, out var v) ? v : (maand, 0);
            if (vorigeMaand != maand) aantal = 0;

            if (aantal >= max)
            {
                Stand[sleutel] = (maand, aantal);
                return false;
            }

            Stand[sleutel] = (maand, aantal + 1);
            return true;
        }
    }

    /// <summary>Alleen voor tests: de teller leegmaken.</summary>
    internal static void Reset()
    {
        lock (Slot) Stand.Clear();
    }
}

/// <summary>
/// Enphase Enlighten API v4 (<c>https://api.enphaseenergy.com</c>) voor micro-omvormers (IQ-systemen).
///
/// Koppelen gaat met OAuth, niet met een sleutel die de klant zelf kan aanmaken. De klant geeft op de
/// Enphase-inlogpagina toestemming, komt terug met een code van één keer gebruiken, en die wisselen wij
/// hier in voor een access- en refresh-token. Credentials: <c>code</c> + <c>redirect_uri</c> bij het
/// koppelen, daarna <c>refresh_token</c>. Een al bestaand refresh-token mag ook direct meegegeven worden.
///
/// Let op: Enphase geeft bij elke vernieuwing een nieuw refresh-token en maakt het oude ongeldig. Daarom
/// zet deze connector de nieuwe waarde terug via <see cref="DeviceCredentials.Set"/>; zonder dat werkt
/// een koppeling precies één dag en daarna nooit meer.
///
/// Wat je krijgt en wat niet. Het gratis developer-plan "Watt" geeft 1.000 verzoeken per maand en het
/// systeemoverzicht (<c>/summary</c>): actueel vermogen van de panelen en de opwek-teller. Afname van en
/// teruglevering aan het net zitten in de meter-telemetrie, en die hoort bij een betaald plan ($249 per
/// maand). Daarom vult deze connector alleen de zonkant. Wil je ook zien wat het huis uit het net haalt,
/// dan is dat een P1-meter ernaast of de lokale IQ Gateway in het thuisnetwerk.
///
/// Door die 1.000 verzoeken per maand leest de poller Enphase hoogstens één keer per uur uit; zie
/// <c>PollingWorker.KortsteInterval</c>. Config: <c>Connectors:Enphase:ClientId</c>,
/// <c>:ClientSecret</c> en <c>:ApiKey</c> (developer-v4.enphase.com). Hetzelfde developer-account als
/// LaadCash gebruiken kan, maar dan delen beide apps dat quotum.
/// </summary>
public sealed class EnphaseConnector(
    IHttpClientFactory httpClientFactory,
    EnphaseOptions options,
    ILogger<EnphaseConnector> logger) : IDeviceConnector
{
    public const string HttpClientName = "enphase";
    public const string DefaultBaseUrl = "https://api.enphaseenergy.com";

    public DeviceBrand Brand => DeviceBrand.Enphase;

    /// <summary>
    /// Een IQ Gateway op het thuisnetwerk mag de app uitlezen. Dat is de snelste route en de enige
    /// die vandaag werkt: de cloudroute hangt op een developer-app van ons, en zolang die sleutels
    /// leeg zijn komt er langs die kant niets binnen. De gateway zelf heeft geen sleutel van ons
    /// nodig, alleen een token van de bewoner, en die staat gewoon in het huis.
    /// </summary>
    public bool KanDoorDeAppGelezenWorden => true;

    /// <summary>Een gateway op 192.168.x.x kan onze server niet bereiken; de telefoon wel.</summary>
    public bool IsLokaalVoor(IReadOnlyDictionary<string, string> credentials) =>
        ThuisAdres.IsPrive(credentials.Get("host"));

    /// <summary>
    /// Het antwoord van /production.json?details=1 omzetten naar een meting.
    ///
    /// De gateway zet alles in twee lijsten. In "production" staat de zonkant: een regel van het type
    /// "inverters" (de som van de micro-omvormers, altijd aanwezig) en, als er CT-klemmen hangen, een
    /// regel van het type "eim" die nauwkeuriger is. In "consumption" staan twee regels die allebei
    /// "eim" heten en alleen in measurementType verschillen: "total-consumption" is wat het huis
    /// verbruikt, "net-consumption" is wat er over de meter gaat. Dat laatste is de enige plek waar
    /// afname en teruglevering vandaan komen, en meteen de reden om dit lokaal te doen.
    ///
    /// Zonder CT-klemmen ontbreekt de hele consumption-lijst. Dan is er alleen opwek, en dat is geen
    /// fout: dat is een installatie zonder meters.
    /// </summary>
    public DeviceReading LeesLokaal(JsonElement rauw, DateTimeOffset gemeten)
    {
        var zon = Regel(rauw, "production", "eim") ?? Regel(rauw, "production", "inverters");
        var netto = Regel(rauw, "consumption", "net-consumption");
        var huis = Regel(rauw, "consumption", "total-consumption");

        // De gateway rekent teruglevering negatief op de netkant, net als onze eigen afspraak, dus
        // het getal kan rechtstreeks mee. Laten we dat aan de app over, dan hangt de betekenis van
        // een min-teken af van de versie die op de telefoon staat.
        return new DeviceReading(
            gemeten,
            PvPowerW: Getal(zon, "wNow") ?? 0,
            LoadPowerW: Getal(huis, "wNow") ?? 0,
            GridPowerW: Getal(netto, "wNow") ?? 0,
            PvEnergyWh: Getal(zon, "whLifetime"),
            ImportEnergyWh: Getal(netto, "whLifetimeImport"),
            ExportEnergyWh: Getal(netto, "whLifetimeExport"));
    }

    /// <summary>Zoekt in production/consumption de regel met het gevraagde type of measurementType.</summary>
    internal static JsonElement? Regel(JsonElement root, string lijst, string soort)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (!root.TryGetProperty(lijst, out var regels) || regels.ValueKind != JsonValueKind.Array) return null;

        foreach (var regel in regels.EnumerateArray())
        {
            if (regel.ValueKind != JsonValueKind.Object) continue;
            var type = Tekst(regel, "type");
            var meting = Tekst(regel, "measurementType");
            if (string.Equals(type, soort, StringComparison.OrdinalIgnoreCase)
                || string.Equals(meting, soort, StringComparison.OrdinalIgnoreCase))
            {
                return regel;
            }
        }
        return null;
    }

    private static string? Tekst(JsonElement regel, string naam) =>
        regel.TryGetProperty(naam, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Getal(JsonElement? regel, string naam) =>
        regel is { } r && r.TryGetProperty(naam, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : null;

    private string? ClientId => options.ClientId;
    private string? ClientSecret => options.ClientSecret;
    private string? ApiKey => options.ApiKey;

    private bool Ingesteld => !string.IsNullOrWhiteSpace(ClientId)
                              && !string.IsNullOrWhiteSpace(ClientSecret)
                              && !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>
    /// De sleutels voor één woning. Staan ze bij het apparaat, dan gaan die voor.
    ///
    /// Enphase rekent per developer-app, niet per woning, dus met één gedeelde sleutel delen alle
    /// klanten samen 1.000 aanroepen per maand. Wie zijn eigen app aanmaakt op
    /// developer-v4.enphase.com heeft zijn eigen 1.000 en zit niemand in de weg. Dat is te veel
    /// gedoe voor de gemiddelde bewoner, maar een installateur of iemand die handig is doet het zo.
    /// </summary>
    private (string Id, string Secret, string Key) Sleutels(IReadOnlyDictionary<string, string>? credentials)
    {
        var id = credentials?.Get("client_id");
        var secret = credentials?.Get("client_secret");
        var key = credentials?.Get("api_key");

        return !string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(secret) && !string.IsNullOrWhiteSpace(key)
            ? (id!, secret!, key!)
            : (ClientId!, ClientSecret!, ApiKey!);
    }

    public async Task<ConnectResult> ConnectAsync(ConnectRequest request, CancellationToken ct)
    {
        // De lokale route eerst, want die heeft onze developer-app niet nodig. Komt er een adres in
        // het huis binnen met een token van de bewoner, dan is de gateway zelf de bron en hoeft hier
        // niets te worden gecontroleerd: dat doet de app, die er wél bij kan. Onze server zou een
        // adres als 192.168.1.30 nooit kunnen bereiken, dus een proefaanroep vanaf hier zou altijd
        // mislukken en de koppeling onterecht weigeren.
        var thuisAdres = request.Credentials.Get("host") ?? request.Credentials.Get("ip");
        if (ThuisAdres.IsPrive(thuisAdres) && !string.IsNullOrWhiteSpace(request.Credentials.Get("token")))
        {
            return ConnectResult.Ok([
                new DiscoveredDevice(
                    ExternalId: $"enphase-lokaal-{thuisAdres}",
                    Name: "Zonnepanelen",
                    Type: DeviceType.Inverter)
            ]);
        }

        if (!Ingesteld)
        {
            return ConnectResult.Fail("Enphase is op de server nog niet ingesteld (Connectors:Enphase:ClientId, ClientSecret en ApiKey).");
        }

        var code = request.Credentials.Get("code");
        var refreshToken = request.Credentials.Get("refresh_token");
        if (code is null && refreshToken is null)
        {
            return ConnectResult.Fail("Geef je Enphase-toestemming af via 'Verbind met Enphase'.");
        }

        Tokens tokens;
        var sleutels = Sleutels(request.Credentials);

        try
        {
            tokens = code is not null
                ? await WisselCodeAsync(code, request.Credentials.Get("redirect_uri"), sleutels, ct)
                : await VernieuwAsync(refreshToken!, sleutels, ct);
        }
        catch (ConnectorException ex)
        {
            return ConnectResult.Fail(ex.Message);
        }

        try
        {
            var client = Client(tokens.AccessToken);
            var systemen = await SystemenAsync(client, sleutels, ct);
            if (systemen.Count == 0)
            {
                return ConnectResult.Fail("Geen Enphase-systeem gevonden op dit account.");
            }

            var devices = systemen
                .Select(s => new DiscoveredDevice(
                    s.SystemId,
                    s.Name is { Length: > 0 } ? s.Name : "Enphase zonnepanelen",
                    DeviceType.Inverter,
                    new Dictionary<string, string>
                    {
                        ["system_id"] = s.SystemId,
                        ["refresh_token"] = tokens.RefreshToken,
                    }))
                .ToList();

            return ConnectResult.Ok(devices);
        }
        catch (ConnectorException ex)
        {
            return ConnectResult.Fail(ex.Message);
        }
    }

    public async Task<DeviceReading> ReadAsync(Device device, DeviceCredentials credentials, CancellationToken ct)
    {
        if (!Ingesteld)
        {
            throw new ConnectorException("Enphase is op de server niet ingesteld.");
        }

        var refreshToken = credentials.Get("refresh_token")
                           ?? throw new ConnectorException("Geen Enphase-koppeling meer. Verbind opnieuw met Enphase.");

        var sleutels = Sleutels(credentials);
        var tokens = await VernieuwAsync(refreshToken, sleutels, ct);

        // Enphase maakt het oude refresh-token ongeldig: dit moet bewaard worden of de koppeling is dood.
        credentials.Set("refresh_token", tokens.RefreshToken);

        var systemId = credentials.Get("system_id") ?? device.ExternalId;
        if (string.IsNullOrWhiteSpace(systemId))
        {
            throw new ConnectorException("Geen Enphase-systeemnummer bij dit apparaat.");
        }

        var client = Client(tokens.AccessToken);
        using var doc = await GetAsync(client, $"/api/v4/systems/{Uri.EscapeDataString(systemId)}/summary?key={Uri.EscapeDataString(sleutels.Key)}", sleutels.Id, ct);
        var root = doc.RootElement;

        var vermogenW = root.GetDoubleOrNull("current_power", "currentPower") ?? 0;
        var opwekTotaalWh = root.GetDoubleOrNull("energy_lifetime", "energyLifetime");

        // Enphase meldt zelf wanneer het systeem voor het laatst iets doorgaf. Dat tijdstip is eerlijker
        // dan "nu": meldt een systeem al uren niets, dan hoort de meting niet van dit moment te heten.
        var gemeldOp = root.GetDoubleOrNull("last_report_at", "lastReportAt") is { } unix and > 0
            ? DateTimeOffset.FromUnixTimeSeconds((long)unix)
            : DateTimeOffset.UtcNow;

        // Enphase vertelt in dezelfde samenvatting hoe groot de installatie is en hoeveel panelen
        // eraan hangen. Dat hoort de bewoner niet uit een offerte van jaren geleden over te tikken,
        // en het kost ons geen extra aanroep: het staat in het antwoord dat we toch al ophalen. Dat
        // laatste telt, want het developer-plan heeft een maandquotum.
        var piekW = root.GetDoubleOrNull("size_w", "sizeW");
        var panelen = root.GetDoubleOrNull("modules", "module_count", "moduleCount");

        return new DeviceReading(
            Timestamp: gemeldOp,
            PvPowerW: Math.Round(vermogenW),
            PvEnergyWh: opwekTotaalWh,
            PvPeakW: piekW is > 0 ? piekW : null,
            PanelCount: panelen is > 0 and < 10_000 ? (int)panelen.Value : null);
    }

    // -----------------------------------------------------------------------
    // OAuth
    // -----------------------------------------------------------------------

    private sealed record Tokens(string AccessToken, string RefreshToken);

    private async Task<Tokens> WisselCodeAsync(string code, string? redirectUri, (string Id, string Secret, string Key) sleutels, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(redirectUri))
        {
            throw new ConnectorException("Enphase heeft ook de terugkeerpagina (redirect_uri) nodig, exact zoals die in de Enphase-app staat.");
        }

        var url = "/oauth/token?grant_type=authorization_code"
                  + "&redirect_uri=" + Uri.EscapeDataString(redirectUri)
                  + "&code=" + Uri.EscapeDataString(code);
        return await TokenAsync(url, "Toestemming van Enphase omwisselen mislukte", sleutels, ct);
    }

    private async Task<Tokens> VernieuwAsync(string refreshToken, (string Id, string Secret, string Key) sleutels, CancellationToken ct)
    {
        var url = "/oauth/token?grant_type=refresh_token&refresh_token=" + Uri.EscapeDataString(refreshToken);
        return await TokenAsync(url, "Enphase-token vernieuwen mislukte", sleutels, ct);
    }

    private async Task<Tokens> TokenAsync(string url, string foutmelding, (string Id, string Secret, string Key) sleutels, CancellationToken ct)
    {
        Rem(sleutels.Id);

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{sleutels.Id}:{sleutels.Secret}")));

        using var response = await client.SendAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Enphase {Status} op token-aanvraag: {Body}", (int)response.StatusCode, Kort(raw));
            throw new ConnectorException(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized
                ? "De Enphase-koppeling is verlopen of ingetrokken. Verbind opnieuw met Enphase."
                : $"{foutmelding} ({(int)response.StatusCode}).");
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var access = doc.RootElement.GetStringOrNull("access_token");
            var refresh = doc.RootElement.GetStringOrNull("refresh_token");
            if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(refresh))
            {
                throw new ConnectorException("Enphase gaf geen bruikbaar token terug.");
            }

            return new Tokens(access, refresh);
        }
        catch (JsonException ex)
        {
            throw new ConnectorException("Onverwacht antwoord van Enphase bij het token.", ex);
        }
    }

    // -----------------------------------------------------------------------
    // Systemen
    // -----------------------------------------------------------------------

    private sealed record EnphaseSysteem(string SystemId, string? Name);

    private async Task<List<EnphaseSysteem>> SystemenAsync(HttpClient client, (string Id, string Secret, string Key) sleutels, CancellationToken ct)
    {
        using var doc = await GetAsync(client, $"/api/v4/systems?key={Uri.EscapeDataString(sleutels.Key)}", sleutels.Id, ct);
        var lijst = new List<EnphaseSysteem>();

        if (!doc.RootElement.TryGetPropertyIgnoreCase("systems", out var systemen) || systemen.ValueKind != JsonValueKind.Array)
        {
            return lijst;
        }

        foreach (var systeem in systemen.EnumerateArray())
        {
            // Het systeemnummer komt soms als getal en soms als string terug.
            var id = systeem.GetStringOrNull("system_id", "systemId")
                     ?? systeem.GetDoubleOrNull("system_id", "systemId")?.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(id)) continue;

            lijst.Add(new EnphaseSysteem(id, systeem.GetStringOrNull("name", "system_name")));
        }

        return lijst;
    }

    /// <summary>
    /// Stopt voordat de maandgrens van het developer-plan erdoorheen is. Zonder dit gaat het quotum
    /// stil over en krijgt iedereen tegelijk foutmeldingen die nergens op slaan.
    /// </summary>
    private void Rem(string clientId)
    {
        if (EnphaseQuotum.Boek(clientId, options.MaxPerMaand, DateTimeOffset.UtcNow)) return;

        logger.LogWarning("Enphase-maandquotum bereikt op sleutel {Sleutel}", Kort(clientId));
        throw new ConnectorException(
            "Het maandquotum van onze Enphase-koppeling is op. Je gegevens komen volgende maand vanzelf "
            + "weer binnen. Wil je er niet op hoeven wachten, dan kun je je eigen Enphase-sleutels "
            + "opgeven; die tellen apart.");
    }

    private HttpClient Client(string accessToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private async Task<JsonDocument> GetAsync(HttpClient client, string url, string clientId, CancellationToken ct)
    {
        Rem(clientId);

        using var response = await client.GetAsync(url, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Enphase {Status} op {Url}: {Body}", (int)response.StatusCode, url, Kort(raw));
            throw new ConnectorException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    "Enphase wees ons af. Verbind opnieuw met Enphase.",
                HttpStatusCode.TooManyRequests =>
                    "Enphase laat even geen verzoeken meer toe (maandlimiet van het developer-plan).",
                _ => $"Enphase gaf een fout ({(int)response.StatusCode}).",
            });
        }

        try
        {
            return JsonDocument.Parse(raw);
        }
        catch (JsonException ex)
        {
            throw new ConnectorException("Onverwacht antwoord van Enphase.", ex);
        }
    }

    private static string Kort(string? s) =>
        string.IsNullOrEmpty(s) ? "(leeg)" : s.Length > 300 ? s[..300] + "…" : s;
}
