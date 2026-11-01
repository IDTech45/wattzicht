using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Wattzicht.Api.Services;

/// <summary>Wat een store ons vertelt over een aankoop.</summary>
/// <param name="Valid">Of de aankoop echt bestaat en bij ons product hoort.</param>
/// <param name="ProductId">Welk product: maand of jaar.</param>
/// <param name="OriginalTransactionId">
/// Het nummer waaronder dit abonnement bij de store bekend staat en dat bij verlengingen gelijk blijft.
/// Daarop herkennen we een melding als dezelfde klant.
/// </param>
/// <param name="ExpiresAt">Tot wanneer deze periode loopt.</param>
/// <param name="Error">Waarom het misging, als het misging.</param>
public sealed record StoreReceipt(
    bool Valid,
    string? ProductId,
    string? OriginalTransactionId,
    DateTimeOffset? ExpiresAt,
    string? Error);

/// <summary>Controleert een aankoop bij Apple of Google.</summary>
public interface IStoreVerifier
{
    /// <summary>Welk platform: "apple" of "google".</summary>
    string Platform { get; }

    /// <summary>Of de sleutels zijn ingesteld. Zonder sleutels verifiëren we niets en geven we niets.</summary>
    bool Configured { get; }

    Task<StoreReceipt> VerifyAsync(string receipt, CancellationToken ct = default);
}

/// <summary>
/// De productnummers van Plus. Deze moeten letterlijk gelijk zijn aan wat in App Store Connect en
/// Play Console staat; zie docs/STORES.md.
/// </summary>
public static class StoreProducts
{
    public const string Monthly = "nl.wattzicht.plus.monthly";
    public const string Yearly = "nl.wattzicht.plus.yearly";

    public static readonly string[] Alle = [Monthly, Yearly];

    /// <summary>Hoe lang een periode van dit product duurt.</summary>
    public static TimeSpan? Duur(string? productId) => productId switch
    {
        Monthly => TimeSpan.FromDays(31),
        Yearly => TimeSpan.FromDays(366),
        _ => null,
    };

    public static bool IsOnze(string? productId) => productId is not null && Alle.Contains(productId);
}

/// <summary>
/// Controleert een aankoop bij Apple met de App Store Server API.
///
/// Waarom dit aan onze kant gebeurt en niet in de app: het bonnetje dat de app krijgt staat op het
/// toestel van de klant, en alles wat daar staat kan aangepast worden. Een abonnement dat wij
/// toekennen op basis van wat de app zegt, is dus geen abonnement maar een vraag of iemand zo
/// vriendelijk wil zijn te betalen. Daarom vragen we het Apple zelf, en pas als Apple ja zegt
/// schrijven we een rij weg.
///
/// Apple wil een JWT ondertekend met een private key (ES256) uit App Store Connect. De drie waarden
/// die daarvoor nodig zijn staan in de omgeving; zonder die waarden staat deze controle uit en
/// krijgt niemand Plus via Apple. Dat is met opzet: liever niets toekennen dan ongecontroleerd.
/// </summary>
public sealed class AppleStoreVerifier(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    TimeProvider clock,
    ILogger<AppleStoreVerifier> logger) : IStoreVerifier
{
    public const string HttpClientName = "apple-store";

    /// <summary>Productie. De zandbak staat op api.storekit-sandbox.itunes.apple.com.</summary>
    public const string ProductionBaseUrl = "https://api.storekit.itunes.apple.com";

    public string Platform => "apple";

    private string? KeyId => config["Store:Apple:KeyId"];
    private string? IssuerId => config["Store:Apple:IssuerId"];
    private string? BundleId => config["Store:Apple:BundleId"];
    private string? PrivateKeyPem => config["Store:Apple:PrivateKeyPem"];

    public bool Configured =>
        !string.IsNullOrWhiteSpace(KeyId)
        && !string.IsNullOrWhiteSpace(IssuerId)
        && !string.IsNullOrWhiteSpace(BundleId)
        && !string.IsNullOrWhiteSpace(PrivateKeyPem);

    public async Task<StoreReceipt> VerifyAsync(string receipt, CancellationToken ct = default)
    {
        if (!Configured)
        {
            return Mislukt("Apple is op de server nog niet ingesteld (Store:Apple:*).");
        }

        if (string.IsNullOrWhiteSpace(receipt))
        {
            return Mislukt("Geen aankoopgegevens meegestuurd.");
        }

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(
                HttpMethod.Get, $"/inApps/v1/subscriptions/{Uri.EscapeDataString(receipt)}");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", MaakToken());

            using var response = await client.SendAsync(request, ct);
            var raw = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Apple gaf {Status} bij het controleren van een aankoop: {Body}",
                    (int)response.StatusCode, Kort(raw));
                return Mislukt($"Apple kon deze aankoop niet bevestigen ({(int)response.StatusCode}).");
            }

            return LeesAntwoord(raw);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Apple-controle mislukt");
            return Mislukt("De controle bij Apple lukte niet.");
        }
    }

    /// <summary>
    /// Leest het antwoord van Apple. Apart zodat een test hem met een opgenomen antwoord kan voeren;
    /// echt bij Apple aankloppen hoort niet in een test.
    /// </summary>
    internal StoreReceipt LeesAntwoord(string json)
    {
        using var doc = JsonDocument.Parse(json);

        // Het antwoord is genest: data[] per groep, met daarin lastTransactions[].
        foreach (var groep in Array(doc.RootElement, "data"))
        {
            foreach (var tx in Array(groep, "lastTransactions"))
            {
                // De velden zelf zitten in een ondertekend JWT; wij lezen de payload (de handtekening
                // is al gecontroleerd doordat dit antwoord van Apple zelf komt, over TLS).
                var payload = Payload(Tekst(tx, "signedTransactionInfo"));
                if (payload is null) continue;

                var productId = Tekst(payload.Value, "productId");
                if (!StoreProducts.IsOnze(productId)) continue;

                var origineel = Tekst(payload.Value, "originalTransactionId") ?? Tekst(tx, "originalTransactionId");
                var verloopt = Millis(payload.Value, "expiresDate");

                if (verloopt is null || verloopt <= clock.GetUtcNow())
                {
                    return Mislukt("Dit abonnement is bij Apple niet meer geldig.");
                }

                return new StoreReceipt(true, productId, origineel, verloopt, null);
            }
        }

        return Mislukt("Apple kent geen lopend Wattzicht-abonnement op dit nummer.");
    }

    /// <summary>Het ondertekende verzoek dat Apple wil zien: ES256, geldig voor een uur.</summary>
    private string MaakToken()
    {
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var header = new { alg = "ES256", kid = KeyId, typ = "JWT" };
        var payload = new
        {
            iss = IssuerId,
            iat = now,
            exp = now + 3600,
            aud = "appstoreconnect-v1",
            bid = BundleId,
        };

        var deel1 = Base64Url(JsonSerializer.SerializeToUtf8Bytes(header));
        var deel2 = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var teTekenen = Encoding.UTF8.GetBytes($"{deel1}.{deel2}");

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(PrivateKeyPem);
        var handtekening = ecdsa.SignData(teTekenen, HashAlgorithmName.SHA256);

        return $"{deel1}.{deel2}.{Base64Url(handtekening)}";
    }

    internal static JsonElement? Payload(string? jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt)) return null;
        var delen = jwt.Split('.');
        if (delen.Length < 2) return null;

        try
        {
            var payload = delen[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
            return doc.RootElement.Clone();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IEnumerable<JsonElement> Array(JsonElement element, string naam) =>
        element.TryGetProperty(naam, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray()
            : [];

    private static string? Tekst(JsonElement element, string naam) =>
        element.TryGetProperty(naam, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTimeOffset? Millis(JsonElement element, string naam) =>
        element.TryGetProperty(naam, out var v) && v.TryGetInt64(out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : null;

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static StoreReceipt Mislukt(string reden) => new(false, null, null, null, reden);

    private static string Kort(string? s) => string.IsNullOrEmpty(s) ? "(leeg)" : s.Length > 300 ? s[..300] + "…" : s;
}

/// <summary>
/// Controleert een aankoop bij Google met de Play Developer API.
///
/// Zelfde reden als bij Apple: de app mag niet bepalen of er betaald is. Google wil een
/// servicesleutel (JSON) van een serviceaccount dat toegang heeft tot de app in Play Console. Zonder
/// die sleutel staat deze controle uit.
/// </summary>
public sealed class GoogleStoreVerifier(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    TimeProvider clock,
    ILogger<GoogleStoreVerifier> logger) : IStoreVerifier
{
    public const string HttpClientName = "google-play";
    public const string BaseUrl = "https://androidpublisher.googleapis.com";

    public string Platform => "google";

    private string? PackageName => config["Store:Google:PackageName"];
    private string? ServiceAccountFile => config["Store:Google:ServiceAccountFile"];

    public bool Configured =>
        !string.IsNullOrWhiteSpace(PackageName)
        && !string.IsNullOrWhiteSpace(ServiceAccountFile)
        && File.Exists(ServiceAccountFile);

    public async Task<StoreReceipt> VerifyAsync(string receipt, CancellationToken ct = default)
    {
        if (!Configured)
        {
            return new StoreReceipt(false, null, null, null, "Google is op de server nog niet ingesteld (Store:Google:*).");
        }

        if (string.IsNullOrWhiteSpace(receipt))
        {
            return new StoreReceipt(false, null, null, null, "Geen aankoopgegevens meegestuurd.");
        }

        try
        {
            var token = await GoogleToken.HaalAsync(ServiceAccountFile!, "https://www.googleapis.com/auth/androidpublisher", ct);
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"/androidpublisher/v3/applications/{PackageName}/purchases/subscriptionsv2/tokens/{Uri.EscapeDataString(receipt)}");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            using var response = await client.SendAsync(request, ct);
            var raw = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Google gaf {Status} bij het controleren van een aankoop: {Body}",
                    (int)response.StatusCode, raw.Length > 300 ? raw[..300] : raw);
                return new StoreReceipt(false, null, null, null,
                    $"Google kon deze aankoop niet bevestigen ({(int)response.StatusCode}).");
            }

            return LeesAntwoord(raw, receipt);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Google-controle mislukt");
            return new StoreReceipt(false, null, null, null, "De controle bij Google lukte niet.");
        }
    }

    /// <summary>Leest het antwoord van Google. Apart zodat een test hem met een opgenomen antwoord kan voeren.</summary>
    internal StoreReceipt LeesAntwoord(string json, string purchaseToken)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var state = root.TryGetProperty("subscriptionState", out var st) ? st.GetString() : null;
        if (state is not ("SUBSCRIPTION_STATE_ACTIVE" or "SUBSCRIPTION_STATE_IN_GRACE_PERIOD"))
        {
            return new StoreReceipt(false, null, null, null, $"Dit abonnement is bij Google niet actief ({state}).");
        }

        if (!root.TryGetProperty("lineItems", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return new StoreReceipt(false, null, null, null, "Google gaf geen productgegevens terug.");
        }

        foreach (var item in items.EnumerateArray())
        {
            var productId = item.TryGetProperty("productId", out var p) ? p.GetString() : null;
            if (!StoreProducts.IsOnze(productId)) continue;

            DateTimeOffset? tot = item.TryGetProperty("expiryTime", out var e)
                                  && DateTimeOffset.TryParse(e.GetString(), out var parsed)
                ? parsed
                : null;

            if (tot is null || tot <= clock.GetUtcNow())
            {
                return new StoreReceipt(false, null, null, null, "Dit abonnement is bij Google verlopen.");
            }

            // Google heeft geen "origineel transactie-id"; de purchase token blijft bij verlengingen
            // gelijk en is dus het nummer waarop wij dezelfde klant herkennen.
            return new StoreReceipt(true, productId, purchaseToken, tot, null);
        }

        return new StoreReceipt(false, null, null, null, "Google kent geen lopend Wattzicht-abonnement op dit nummer.");
    }
}
