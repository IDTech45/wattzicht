using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wattzicht.Shared;

namespace Wattzicht.App.Services;

/// <summary>
/// De P1-meter in de meterkast uitlezen en doorsturen.
///
/// Zo'n kastje heeft een adres als 192.168.1.50 en praat alleen op het eigen netwerk, zonder cloud
/// en zonder sleutel. Onze server komt daar nooit bij, de telefoon wel zodra iemand thuis is. Dus
/// doet de app het: ophalen bij het kastje en het antwoord onvertaald doorsturen naar de API. De
/// omzetting gebeurt daar, zodat een oudere app geen eigen uitleg van de velden meestuurt.
///
/// Sinds 05-11-2026 doet dit ook de Sessy-thuisbatterij. Die praat net zo goed alleen op het eigen
/// netwerk, maar stond bij ons als cloudkoppeling: de server probeerde elke vijf minuten 192.168.1.50
/// te bereiken en dat kon per definitie niet lukken, bij geen enkele klant.
///
/// Dat betekent ook dat de gegevens alleen binnenkomen als iemand de app opent terwijl hij thuis is.
/// Dat is genoeg: de meter houdt zijn eigen tellers bij, dus een gemiste dag haalt hij bij de
/// volgende uitlezing weer in bij het verschil nemen. Alleen het vermogen van dát moment missen we.
/// </summary>
public sealed class LokaleMeter(ApiClient api, ILogger<LokaleMeter> logger)
{
    /// <summary>
    /// Eigen client met een korte tijdslimiet. Zit je niet op het goede netwerk, dan hoort de app
    /// niet twintig tellen te blijven hangen maar gewoon door te gaan met de rest van het scherm.
    /// </summary>
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(4) };

    /// <summary>
    /// Aparte client voor een Alfen-laadpaal. Die draagt een zelfondertekend certificaat op een
    /// IP-adres; daar valt niets aan te controleren, dus voor deze client zetten we de controle uit.
    /// Het verkeer blijft binnen het huis: de telefoon praat rechtstreeks met de paal.
    /// </summary>
    private static readonly HttpClient PaalClient = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
    })
    { Timeout = TimeSpan.FromSeconds(6) };

    /// <summary>Niet vaker dan dit per apparaat, ook niet als de gebruiker het scherm blijft openen.</summary>
    private static readonly TimeSpan Tussenpoos = TimeSpan.FromMinutes(1);

    private static readonly Dictionary<Guid, DateTimeOffset> _laatste = [];

    /// <summary>
    /// Alle lokale apparaten van deze woning uitlezen en doorsturen. Faalt nooit naar buiten toe: we
    /// zitten niet op het goede netwerk, het kastje is uit, de wifi is traag. Dat zijn allemaal
    /// normale situaties en geen reden om het startscherm te laten struikelen.
    /// </summary>
    public async Task StuurDoorAsync(IEnumerable<DeviceDto> apparaten, CancellationToken ct = default)
    {
        foreach (var apparaat in apparaten.Where(IsLokaal))
        {
            if (_laatste.TryGetValue(apparaat.Id, out var vorige)
                && DateTimeOffset.UtcNow - vorige < Tussenpoos)
            {
                continue;
            }

            try
            {
                var adres = await AdresAsync(apparaat.Id, ct);
                if (adres is null) continue;

                var rauw = apparaat.Brand switch
                {
                    DeviceBrand.Sessy => await HaalSessyAsync(adres, apparaat.Id, ct),
                    DeviceBrand.Alfen => await HaalAlfenAsync(adres, apparaat.Id, ct),
                    _ => await HaalAsync(adres, ct),
                };
                if (rauw is null) continue;

                await api.StuurLokaleMetingAsync(apparaat.Id, new LocalReadingRequest(rauw.Value, DateTimeOffset.UtcNow), ct);
                _laatste[apparaat.Id] = DateTimeOffset.UtcNow;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Lokale meter {Id} niet gelezen", apparaat.Id);
            }
        }
    }

    /// <summary>
    /// Het adres van het kastje. Staat bij het koppelen in de app bewaard: de server geeft het niet
    /// terug, want daar staat het versleuteld en hij heeft er zelf niets aan.
    /// </summary>
    private static Task<string?> AdresAsync(Guid deviceId, CancellationToken ct) =>
        SecureStorage.Default.GetAsync(Sleutel(deviceId));

    public static Task BewaarAdresAsync(Guid deviceId, string adres) =>
        SecureStorage.Default.SetAsync(Sleutel(deviceId), adres);

    private static string Sleutel(Guid deviceId) => $"lokaal-adres-{deviceId}";

    /// <summary>
    /// Welke apparaten leest de app zelf uit? De P1-meter altijd, en een Sessy-thuisbatterij alleen als
    /// hij op een thuisadres staat. Wie een poort heeft opengezet laat de server het doen: die kijkt
    /// elke vijf minuten, en dat is beter dan wachten tot iemand de app opent.
    /// </summary>
    private static bool IsLokaal(DeviceDto apparaat) => apparaat.Brand switch
    {
        DeviceBrand.HomeWizard => true,
        DeviceBrand.Sessy => true,
        // Een Alfen heeft geen cloud waar wij bij mogen. Hangt hij aan een simkaart van de
        // leverancier, dan praat hij met hún backoffice en zien wij niets; op het thuisnetwerk is
        // hij wél aan te spreken, en daar is de telefoon.
        DeviceBrand.Alfen => true,
        _ => false,
    };

    /// <summary>
    /// De Sessy aanspreken. Anders dan de P1-dongle wil deze een gebruikersnaam en wachtwoord, en die
    /// staan op de dongle zelf. Ze blijven op de telefoon: de server heeft ze niet nodig zolang hij
    /// toch niet bij de batterij kan.
    /// </summary>
    private async Task<JsonElement?> HaalSessyAsync(string adres, Guid deviceId, CancellationToken ct)
    {
        var inlog = await SecureStorage.Default.GetAsync(SessySleutel(deviceId));
        if (string.IsNullOrWhiteSpace(inlog)) return null;

        try
        {
            using var verzoek = new HttpRequestMessage(HttpMethod.Get, $"http://{adres}/api/v1/power/status");
            verzoek.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(inlog)));

            using var antwoord = await Client.SendAsync(verzoek, ct);
            if (!antwoord.IsSuccessStatusCode) return null;

            var doc = await antwoord.Content.ReadFromJsonAsync<JsonElement>(ct);
            return doc.ValueKind == JsonValueKind.Object ? doc : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// De Alfen aanspreken: inloggen, de meterstand ophalen, uitloggen.
    ///
    /// Die laatste stap is geen beleefdheid. De paal laat maar één sessie tegelijk toe en koppelt
    /// die aan het IP-adres van wie inlogt; blijft hij hangen, dan kan niemand anders er meer bij,
    /// ook de eigenaar niet met de app van Alfen. Daarom loggen we ook uit als het ophalen misging.
    /// </summary>
    private async Task<JsonElement?> HaalAlfenAsync(string adres, Guid deviceId, CancellationToken ct)
    {
        var inlog = await SecureStorage.Default.GetAsync(AlfenSleutel(deviceId));
        if (string.IsNullOrWhiteSpace(inlog)) return null;

        var scheiding = inlog.IndexOf(':');
        if (scheiding <= 0) return null;
        var gebruiker = inlog[..scheiding];
        var wachtwoord = inlog[(scheiding + 1)..];

        var basis = $"https://{adres}";
        try
        {
            using var login = await PaalClient.PostAsJsonAsync($"{basis}/api/login",
                new { username = gebruiker, password = wachtwoord, displayname = "Wattzicht" }, ct);
            if (!login.IsSuccessStatusCode) return null;

            try
            {
                var samen = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var id in AlfenEigenschappen)
                {
                    using var antwoord = await PaalClient.GetAsync($"{basis}/api/prop?id={id}", ct);
                    if (!antwoord.IsSuccessStatusCode) continue;

                    var deel = await antwoord.Content.ReadFromJsonAsync<JsonElement>(ct);
                    if (deel.ValueKind != JsonValueKind.Object) continue;
                    if (!deel.TryGetProperty("properties", out var lijst)) continue;

                    foreach (var eigenschap in lijst.EnumerateArray())
                    {
                        if (eigenschap.TryGetProperty("id", out var eid) && eid.GetString() is { } sleutel &&
                            eigenschap.TryGetProperty("value", out var waarde))
                        {
                            samen[sleutel] = waarde.Clone();
                        }
                    }
                }

                if (samen.Count == 0) return null;
                return JsonSerializer.SerializeToDocument(samen).RootElement.Clone();
            }
            finally
            {
                try { await PaalClient.PostAsync($"{basis}/api/logout", null, CancellationToken.None); }
                catch (Exception ex) { logger.LogDebug(ex, "Uitloggen bij de Alfen mislukt"); }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>Meterstand socket 1 en 2 in kWh, en het actieve vermogen in watt.</summary>
    private static readonly string[] AlfenEigenschappen = ["2221_22", "3221_22", "2221_16"];

    /// <summary>Gebruikersnaam en wachtwoord van de laadpaal, als "gebruiker:wachtwoord".</summary>
    public static Task BewaarAlfenInlogAsync(Guid deviceId, string gebruiker, string wachtwoord) =>
        SecureStorage.Default.SetAsync(AlfenSleutel(deviceId), $"{gebruiker}:{wachtwoord}");

    private static string AlfenSleutel(Guid deviceId) => $"alfen-inlog-{deviceId}";

    /// <summary>Gebruikersnaam en wachtwoord van de Sessy, als "gebruiker:wachtwoord".</summary>
    public static Task BewaarSessyInlogAsync(Guid deviceId, string gebruiker, string wachtwoord) =>
        SecureStorage.Default.SetAsync(SessySleutel(deviceId), $"{gebruiker}:{wachtwoord}");

    private static string SessySleutel(Guid deviceId) => $"sessy-inlog-{deviceId}";

    /// <summary>Het kastje aanspreken. Geeft null als het er niet is; dat is geen fout.</summary>
    public async Task<JsonElement?> HaalAsync(string adres, CancellationToken ct = default)
    {
        try
        {
            using var antwoord = await Client.GetAsync($"http://{adres}/api/v1/data", ct);
            if (!antwoord.IsSuccessStatusCode) return null;

            var doc = await antwoord.Content.ReadFromJsonAsync<JsonElement>(ct);
            return doc.ValueKind == JsonValueKind.Object ? doc : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Niet thuis, kastje uit, of een adres dat de router opnieuw heeft uitgedeeld.
            return null;
        }
    }
}
