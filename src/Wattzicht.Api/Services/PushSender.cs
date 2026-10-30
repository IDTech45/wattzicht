using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;

namespace Wattzicht.Api.Services;

/// <summary>Stuurt een pushmelding naar alle toestellen van een huishouden.</summary>
public interface IPushSender
{
    /// <summary>Of er een Firebase-sleutel is ingesteld.</summary>
    bool Enabled { get; }

    /// <summary>Verstuurt de melding. Fouten worden gelogd en nooit doorgegooid.</summary>
    /// <param name="route">Scherm in de app dat bij aantikken opent (bv. "advice" of "alerts").</param>
    Task SendToHouseholdAsync(Guid householdId, string title, string body, string? route = null, CancellationToken ct = default);
}

/// <summary>
/// Meldingen via Firebase Cloud Messaging (HTTP v1), overgenomen van LaadCash.
/// Bewust stil zonder sleutel: lokaal en zolang het Firebase-project nog niet is ingesteld draait de rest gewoon door.
/// Een mislukte melding mag nooit een pollronde tegenhouden.
/// Config: <c>Push:Fcm:ProjectId</c> en <c>Push:Fcm:ServiceAccountFile</c> (aanbevolen) of <c>Push:Fcm:ServiceAccountJson</c>.
/// </summary>
public sealed class FcmPushSender : IPushSender
{
    // Eén client voor het hele proces: een nieuwe per melding put de poorten uit.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>Android-meldingskanaal; moet gelijk zijn aan het kanaal dat de app aanmaakt.</summary>
    public const string AndroidChannelId = "wattzicht";

    private readonly AppDbContext _db;
    private readonly ILogger<FcmPushSender> _logger;
    private readonly string? _serviceAccountJson;
    private readonly string? _projectId;

    public FcmPushSender(AppDbContext db, IConfiguration config, ILogger<FcmPushSender> logger)
    {
        _db = db;
        _logger = logger;
        var file = config["Push:Fcm:ServiceAccountFile"];
        _serviceAccountJson = !string.IsNullOrWhiteSpace(file) && File.Exists(file)
            ? File.ReadAllText(file)
            : config["Push:Fcm:ServiceAccountJson"];
        _projectId = config["Push:Fcm:ProjectId"];
    }

    public bool Enabled => !string.IsNullOrWhiteSpace(_serviceAccountJson) && !string.IsNullOrWhiteSpace(_projectId);

    public async Task SendToHouseholdAsync(Guid householdId, string title, string body, string? route = null, CancellationToken ct = default)
    {
        if (!Enabled)
        {
            _logger.LogDebug("Pushmelding niet verstuurd: Push:Fcm is niet ingesteld ({Title})", title);
            return;
        }

        var devices = await _db.PushDevices.Where(d => d.HouseholdId == householdId).ToListAsync(ct);
        if (devices.Count == 0)
        {
            return;
        }

        string accessToken;
        try
        {
            accessToken = await AccessTokenAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Kon geen FCM-toegangstoken ophalen; controleer de service-accountsleutel");
            return;
        }

        var expired = new List<PushDevice>();
        foreach (var device in devices)
        {
            var payload = JsonSerializer.Serialize(new
            {
                message = new
                {
                    token = device.Token,
                    notification = new { title, body },
                    data = new Dictionary<string, string> { ["route"] = route ?? string.Empty },
                    android = new { priority = "high", notification = new { channel_id = AndroidChannelId } },
                    apns = new { payload = new { aps = new { sound = "default" } } }
                }
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, $"https://fcm.googleapis.com/v1/projects/{_projectId}/messages:send")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            try
            {
                using var response = await Http.SendAsync(request, ct);
                if (response.IsSuccessStatusCode)
                {
                    continue;
                }

                // 404 = toestel bestaat niet meer (UNREGISTERED). Blijven proberen heeft geen zin.
                // 400 kan ook een fout in onze payload zijn; alleen weggooien als Google zegt dat het token ongeldig is.
                var text = await response.Content.ReadAsStringAsync(ct);
                if (response.StatusCode == HttpStatusCode.NotFound
                    || (response.StatusCode == HttpStatusCode.BadRequest && text.Contains("registration token", StringComparison.OrdinalIgnoreCase)))
                {
                    expired.Add(device);
                    continue;
                }

                _logger.LogWarning("Pushmelding geweigerd ({Status}) voor huishouden {HouseholdId}: {Body}", (int)response.StatusCode, householdId, text);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Pushmelding versturen mislukt voor huishouden {HouseholdId}", householdId);
            }
        }

        if (expired.Count > 0)
        {
            _db.PushDevices.RemoveRange(expired);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("{Count} verlopen toestel(len) opgeruimd", expired.Count);
        }
    }

    /// <summary>Kortlevend toegangstoken via de servicesleutel; de Google-bibliotheek ververst het zelf.</summary>
    private async Task<string> AccessTokenAsync(CancellationToken ct)
    {
#pragma warning disable CS0618 // FromJson is gemarkeerd als verouderd, maar is wat LaadCash ook gebruikt; vervangen door CredentialFactory zodra we upgraden.
        var credential = GoogleCredential
            .FromJson(_serviceAccountJson)
            .CreateScoped("https://www.googleapis.com/auth/firebase.messaging");
#pragma warning restore CS0618
        return await credential.UnderlyingCredential.GetAccessTokenForRequestAsync(cancellationToken: ct);
    }
}
