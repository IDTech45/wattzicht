using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Wattzicht.Api.Services;

/// <summary>
/// Haalt een toegangstoken voor een Google-API op met een servicesleutel.
///
/// Dit is dezelfde dans als bij de pushmeldingen: een JWT ondertekenen met de private sleutel uit het
/// servicesleutel-bestand en die bij Google inwisselen voor een token van een uur. Het token wordt
/// bewaard tot vlak voor het verloopt; elke aanroep opnieuw een token halen zou een extra
/// netwerkrondje per controle betekenen, en Google gaat daar op den duur over klagen.
/// </summary>
public static class GoogleToken
{
    private const string TokenUrl = "https://oauth2.googleapis.com/token";

    private static readonly ConcurrentDictionary<string, (string Token, DateTimeOffset Tot)> Cache = new();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>Een geldig toegangstoken voor dit bereik, uit de cache of vers opgehaald.</summary>
    public static async Task<string> HaalAsync(string serviceAccountFile, string scope, CancellationToken ct = default)
    {
        var sleutel = serviceAccountFile + "|" + scope;
        if (Cache.TryGetValue(sleutel, out var bewaard) && bewaard.Tot > DateTimeOffset.UtcNow.AddMinutes(2))
        {
            return bewaard.Token;
        }

        var json = await File.ReadAllTextAsync(serviceAccountFile, ct);
        using var doc = JsonDocument.Parse(json);
        var email = doc.RootElement.GetProperty("client_email").GetString()!;
        var privateKey = doc.RootElement.GetProperty("private_key").GetString()!;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = new { alg = "RS256", typ = "JWT" };
        var payload = new
        {
            iss = email,
            scope,
            aud = TokenUrl,
            iat = now,
            exp = now + 3600,
        };

        var deel1 = Base64Url(JsonSerializer.SerializeToUtf8Bytes(header));
        var deel2 = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));

        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKey);
        var handtekening = rsa.SignData(
            Encoding.UTF8.GetBytes($"{deel1}.{deel2}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var assertion = $"{deel1}.{deel2}.{Base64Url(handtekening)}";

        using var response = await Http.PostAsync(TokenUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
            ["assertion"] = assertion,
        }), ct);

        response.EnsureSuccessStatusCode();
        var antwoord = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var token = antwoord.GetProperty("access_token").GetString()!;
        var geldig = antwoord.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;

        Cache[sleutel] = (token, DateTimeOffset.UtcNow.AddSeconds(geldig));
        return token;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
