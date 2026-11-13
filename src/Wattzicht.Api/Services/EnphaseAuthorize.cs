namespace Wattzicht.Api.Services;

/// <summary>
/// De toestemmingspagina van Enphase, met onze sleutel of met die van de bewoner zelf.
///
/// Waarom dat tweede bestaat: Enphase rekent per developer-app en niet per woning. Met één gedeelde
/// sleutel delen al onze klanten samen duizend aanroepen per maand, en dat is precies de reden dat
/// we een Enphase maar eens per vijf uur uitlezen en per paneel nog geen dagcijfers kunnen tonen.
/// Wie zelf een app aanmaakt op developer-v4.enphase.com heeft zijn eigen duizend en zit niemand in
/// de weg.
///
/// Dat is niet voor iedereen weggelegd, en daarom blijft het een keuze en geen eis: wie niets
/// invult gaat gewoon via onze sleutel mee.
/// </summary>
public static class EnphaseAuthorize
{
    /// <summary>
    /// De sleutel van de bewoner, als die bruikbaar is.
    ///
    /// Enphase geeft een client-id van cijfers en letters. Alles met spaties, schuine strepen of
    /// andere tekens is geplakt uit de verkeerde hoek van de pagina (de secret, een URL, een heel
    /// stuk json) en gebruiken we niet: dan is onze eigen sleutel een beter antwoord dan een
    /// toestemmingspagina die bij Enphase stukloopt.
    /// </summary>
    public static bool IsBruikbaar(string? clientId) =>
        !string.IsNullOrWhiteSpace(clientId)
        && clientId.Length is >= 16 and <= 128
        && clientId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>Het adres waar de bewoner naartoe gaat om toestemming te geven.</summary>
    public static string Url(string clientId, string redirectUri) =>
        "https://api.enphaseenergy.com/oauth/authorize?response_type=code"
        + "&client_id=" + Uri.EscapeDataString(clientId)
        + "&redirect_uri=" + Uri.EscapeDataString(redirectUri);

    /// <summary>Welke sleutel het wordt: die van de bewoner, anders die van ons.</summary>
    public static string? Kies(string? eigen, string? onze) =>
        IsBruikbaar(eigen) ? eigen!.Trim() : (string.IsNullOrWhiteSpace(onze) ? null : onze);
}
