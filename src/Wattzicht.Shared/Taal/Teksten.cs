using System.Collections.Frozen;
using System.Globalization;

namespace Wattzicht.Shared.Taal;

/// <summary>
/// Het taalpakket van Wattzicht: Nederlands, Engels en Turks.
///
/// Nederlands is de bron. De andere twee volgen exact dezelfde sleutels, en een test in
/// Wattzicht.Api.Tests houdt bij of dat zo blijft: een vergeten vertaling is niet zichtbaar in een
/// build en valt bij een gebruiker op als één Nederlandse regel tussen de Turkse.
///
/// Waarom Turks erbij: bij LaadCash bleek het Turkse pakket gewoon gebruikt te worden. Dezelfde
/// mensen hebben vaak ook zonnepanelen, en wie zijn telefoon op Turks heeft staan wil zijn eigen
/// energie-app niet in een tweede taal lezen.
/// </summary>
public static class Teksten
{
    /// <summary>De talen die er zijn.</summary>
    public static readonly string[] Codes = ["nl", "en", "tr"];

    /// <summary>
    /// De notatie voor getallen en datums die bij een taal hoort.
    ///
    /// Dit hoort bij het taalpakket en niet bij de instelling van de server: een bedrag in een
    /// melding werd met de cultuur van het proces opgemaakt, en op een Linux-server is dat de
    /// invariante cultuur. Een Nederlandse klant kreeg dan "€ 0.75" in zijn melding te zien.
    /// </summary>
    public static CultureInfo Cultuur(string? code) => code switch
    {
        "en" => CultureInfo.GetCultureInfo("en-GB"),
        "tr" => CultureInfo.GetCultureInfo("tr-TR"),
        _ => CultureInfo.GetCultureInfo("nl-NL"),
    };

    /// <summary>Het woordenboek bij een taalcode; onbekende code geeft Nederlands.</summary>
    public static FrozenDictionary<string, string> Voor(string? code) => code switch
    {
        "en" => TekstenEn.Alles,
        "tr" => TekstenTr.Alles,
        _ => TekstenNl.Alles,
    };

    /// <summary>
    /// De tekst bij een sleutel, met Nederlands als terugvaloptie en anders de sleutel zelf.
    ///
    /// De sleutel teruggeven in plaats van een lege tekst is een keuze: een leeg label ziet eruit
    /// als een opmaakfout en gaat jaren mee, een zichtbare sleutel valt bij het eerste testje op.
    /// </summary>
    public static string T(string? code, string sleutel)
    {
        if (string.IsNullOrWhiteSpace(sleutel)) return string.Empty;
        if (Voor(code).TryGetValue(sleutel, out var tekst)) return tekst;
        return TekstenNl.Alles.TryGetValue(sleutel, out var nederlands) ? nederlands : sleutel;
    }
}
