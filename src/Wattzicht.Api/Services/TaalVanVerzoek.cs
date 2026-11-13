using Wattzicht.Shared.Taal;

namespace Wattzicht.Api.Services;

/// <summary>
/// In welke taal wil deze aanvrager zijn antwoord?
///
/// De meeste tekst in de app staat in de app zelf, maar niet alles kan daar: de tips en de dagtip
/// worden met gemeten waarden opgebouwd en komen dus van de server. Zonder dit zou een Turks scherm
/// een Nederlandse tip tonen, en dat is precies wat een half vertaalde app verraadt.
///
/// De app stuurt zijn gekozen taal mee in <c>Accept-Language</c>. Dat is de standaardkop daarvoor,
/// dus een browser of een ander hulpmiddel doet automatisch mee. Een onbekende taal geeft
/// Nederlands; we weigeren niets op grond van een kop.
/// </summary>
public static class TaalVanVerzoek
{
    public static string Taal(this HttpRequest request)
    {
        var kop = request.Headers.AcceptLanguage.ToString();
        if (string.IsNullOrWhiteSpace(kop)) return "nl";

        // "tr-TR,tr;q=0.9,nl;q=0.8" -> de eerste die we kennen. Geen weging: wie zijn app op Turks
        // zet wil Turks, en de rest van de rij is dan niet interessant.
        foreach (var deel in kop.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var code = deel.Split(';')[0].Trim();
            if (code.Length > 2) code = code[..2];

            if (Teksten.Codes.Contains(code, StringComparer.OrdinalIgnoreCase))
            {
                return code.ToLowerInvariant();
            }
        }

        return "nl";
    }
}
