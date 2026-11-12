using Google.Apis.Auth;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Wattzicht.Api.Services;

/// <summary>
/// Welke client-id's wij accepteren als ontvanger van een token.
///
/// Dit zijn geen geheimen: een client-id zit zichtbaar in elke app-build en in elke webpagina. Er is
/// hier dan ook bewust geen client secret, want we gebruiken de id-token-stroom: het toestel logt in
/// bij Google of Apple, krijgt een ondertekend token, en wij controleren dat token. Er loopt geen
/// omleiding via onze server.
///
/// Meerdere waarden mogen, komma-gescheiden. Dat is nodig omdat Google per platform een eigen
/// client-id uitgeeft: Android, iOS en web zijn drie verschillende ontvangers van hetzelfde account.
/// </summary>
public sealed class ExternalAuthSettings
{
    public string? GoogleClientIds { get; set; }
    public string? AppleClientIds { get; set; }
}

/// <summary>Wat er uit een gecontroleerd token komt.</summary>
/// <param name="Provider">"google" of "apple".</param>
/// <param name="Subject">Het vaste id van deze gebruiker bij die partij.</param>
/// <param name="Email">Het mailadres uit het token.</param>
/// <param name="EmailVerified">Of die partij het adres zelf heeft gecontroleerd.</param>
/// <param name="FullName">Naam, als die meekomt. Apple geeft die alleen de allereerste keer.</param>
public sealed record ExternalUser(string Provider, string Subject, string Email, bool EmailVerified, string? FullName);

/// <summary>Het token deugde niet. De tekst is bedoeld voor het logboek, niet voor de gebruiker.</summary>
public sealed class ExternalAuthException(string message) : Exception(message);

/// <summary>
/// Controleert inlogtokens van Google en Apple.
///
/// Een token wordt nooit op zijn woord geloofd. We halen de publieke sleutels bij de partij zelf op,
/// controleren de handtekening, kijken of het token voor ons bedoeld is (de audience is onze eigen
/// client-id) en of het niet verlopen is. Zonder die drie controles kan iedereen een token in elkaar
/// zetten en zich voordoen als een willekeurige gebruiker.
///
/// Staat een partij niet ingesteld, dan is hij uit. Liever geen knop dan een knop die bij iedereen
/// stukloopt.
/// </summary>
public sealed class ExternalAuthService
{
    private readonly ExternalAuthSettings _settings;
    private readonly ConfigurationManager<OpenIdConnectConfiguration>? _apple;

    public ExternalAuthService(ExternalAuthSettings settings)
    {
        _settings = settings;

        if (AppleIds.Length > 0)
        {
            _apple = new ConfigurationManager<OpenIdConnectConfiguration>(
                "https://appleid.apple.com/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever());
        }
    }

    public string[] GoogleIds => Split(_settings.GoogleClientIds);
    public string[] AppleIds => Split(_settings.AppleClientIds);

    public bool GoogleAan => GoogleIds.Length > 0;
    public bool AppleAan => AppleIds.Length > 0;

    /// <summary>Welke knoppen de app mag tonen. Wat hier niet in staat, werkt niet.</summary>
    public IReadOnlyList<string> Beschikbaar =>
        new[] { GoogleAan ? "google" : null, AppleAan ? "apple" : null }.OfType<string>().ToArray();

    public Task<ExternalUser> ControleerAsync(string provider, string idToken, CancellationToken ct = default) =>
        provider?.Trim().ToLowerInvariant() switch
        {
            "google" => GoogleAsync(idToken),
            "apple" => AppleAsync(idToken, ct),
            _ => throw new ExternalAuthException($"Onbekende aanbieder '{provider}'."),
        };

    private async Task<ExternalUser> GoogleAsync(string idToken)
    {
        if (!GoogleAan) throw new ExternalAuthException("Inloggen met Google staat niet ingesteld.");

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(idToken,
                new GoogleJsonWebSignature.ValidationSettings { Audience = GoogleIds });
        }
        catch (InvalidJwtException ex)
        {
            throw new ExternalAuthException("Google-token afgekeurd: " + ex.Message);
        }

        if (string.IsNullOrWhiteSpace(payload.Email))
        {
            throw new ExternalAuthException("Google gaf geen e-mailadres terug.");
        }

        return new ExternalUser("google", payload.Subject, payload.Email.ToLowerInvariant(),
            payload.EmailVerified, string.IsNullOrWhiteSpace(payload.Name) ? null : payload.Name);
    }

    private async Task<ExternalUser> AppleAsync(string idToken, CancellationToken ct)
    {
        if (_apple is null) throw new ExternalAuthException("Inloggen met Apple staat niet ingesteld.");

        var config = await _apple.GetConfigurationAsync(ct);
        var resultaat = await new JsonWebTokenHandler().ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidIssuer = "https://appleid.apple.com",
            ValidAudiences = AppleIds,
            IssuerSigningKeys = config.SigningKeys,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
        });

        if (!resultaat.IsValid)
        {
            throw new ExternalAuthException("Apple-token afgekeurd: " + (resultaat.Exception?.Message ?? "onbekend"));
        }

        var token = (Microsoft.IdentityModel.JsonWebTokens.JsonWebToken)resultaat.SecurityToken;
        var subject = token.GetClaim("sub").Value;
        token.TryGetClaim("email", out var mail);

        if (mail is null || string.IsNullOrWhiteSpace(mail.Value))
        {
            // Apple stuurt het adres alleen bij de allereerste aanmelding mee. Komt iemand terug op
            // een vers toestel zonder dat wij hem kennen, dan kunnen we hem nergens aan koppelen.
            throw new ExternalAuthException("Apple gaf geen e-mailadres terug.");
        }

        token.TryGetClaim("email_verified", out var geverifieerd);

        return new ExternalUser("apple", subject, mail.Value.ToLowerInvariant(),
            // Apple geeft dit als tekst "true", niet als boolean.
            geverifieerd is null || string.Equals(geverifieerd.Value, "true", StringComparison.OrdinalIgnoreCase),
            FullName: null);
    }

    private static string[] Split(string? waarde) =>
        string.IsNullOrWhiteSpace(waarde)
            ? []
            : waarde.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
