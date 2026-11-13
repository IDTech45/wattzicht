using System.ComponentModel;
using Wattzicht.Shared.Taal;

namespace Wattzicht.App.Services;

/// <summary>
/// De taal van de app: Nederlands, Engels of Turks.
///
/// Waarom dit bestaat: de app was volledig Nederlands en er was nergens een knop om dat te
/// veranderen. Voor een app die je aan je installateur meegeeft is dat te smal; bij LaadCash bleek
/// het Turkse pakket ook gewoon gebruikt te worden.
///
/// Hoe de app eraan komt: XAML vraagt een tekst op met <c>{t:T sleutel}</c>. Dat is een binding op
/// <see cref="Versie"/> hieronder, met de sleutel als ConverterParameter en deze ene instantie als
/// bron. Zodra iemand van taal wisselt gaat Versie een stapje omhoog, vuurt elke binding opnieuw en
/// ververst elk label zichzelf. Zo hoeft er geen scherm opnieuw opgebouwd te worden en is er ook
/// geen herstart nodig.
///
/// Ontbreekt een sleutel in de gekozen taal, dan volgt het Nederlands, en anders de sleutel zelf.
/// Een lege tekst op het scherm is het ergste van de drie: dan zie je niet dát er iets mist.
/// </summary>
public sealed class Taal : INotifyPropertyChanged
{
    /// <summary>De bron waar alle bindingen in de XAML naar wijzen.</summary>
    public static Taal Nu { get; } = new();

    private const string Sleutel = "taal";

    /// <summary>De talen die er zijn, in de volgorde waarin ze in de keuzelijst staan.</summary>
    public static IReadOnlyList<TaalKeuze> Keuzes { get; } =
    [
        new("nl", "Nederlands"),
        new("en", "English"),
        new("tr", "Türkçe"),
    ];

    private static string _code = Lees();

    /// <summary>De gekozen taalcode: nl, en of tr.</summary>
    public static string Code => _code;

    /// <summary>De naam van de gekozen taal, voor het instellingenscherm.</summary>
    public static string Naam => Keuzes.FirstOrDefault(k => k.Code == _code)?.Naam ?? "Nederlands";

    private Taal() { }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gaat een stapje omhoog bij elke taalwissel. Hier hangt elke tekst in de XAML aan.
    ///
    /// Het getal zelf zegt niets; het bestaat om de bindingen te laten vuren. Een gewone
    /// propertynaam als pad is met opzet: een indexerpad met punten in de sleutel moet door de
    /// padlezer ontleed worden, en als dat ergens misgaat is elk label in de app leeg.
    /// </summary>
    public int Versie { get; private set; }

    /// <summary>De tekst bij een sleutel, in de gekozen taal.</summary>
    public static string T(string sleutel)
    {
        if (string.IsNullOrWhiteSpace(sleutel)) return string.Empty;

        var woordenboek = Teksten.Voor(_code);
        if (woordenboek.TryGetValue(sleutel, out var tekst)) return tekst;
        if (TekstenNl.Alles.TryGetValue(sleutel, out var nederlands)) return nederlands;

        // De sleutel zelf. Dat ziet er lelijk uit, en dat is de bedoeling: zo valt een vergeten
        // tekst op bij het testen in plaats van dat er een lege regel staat.
        return sleutel;
    }

    /// <summary>Zet de taal en onthoudt hem op dit toestel.</summary>
    public static void Zet(string code)
    {
        var gekozen = Keuzes.Any(k => k.Code == code) ? code : "nl";
        if (gekozen == _code) return;

        _code = gekozen;
        try
        {
            Preferences.Default.Set(Sleutel, gekozen);
        }
        catch (Exception)
        {
            // Opslag kan geweigerd worden. Dan geldt de keuze voor deze sessie; dat is beter dan
            // weigeren te wisselen.
        }

        Nu.Versie++;
        Nu.PropertyChanged?.Invoke(Nu, new PropertyChangedEventArgs(nameof(Versie)));
    }

    /// <summary>
    /// De eerder gekozen taal, of de taal van het toestel als er nog niets gekozen is.
    ///
    /// Dat laatste scheelt een stap: wie zijn telefoon op Turks heeft staan krijgt de app meteen in
    /// het Turks, zonder eerst in de instellingen te zoeken.
    /// </summary>
    private static string Lees()
    {
        try
        {
            var bewaard = Preferences.Default.Get(Sleutel, string.Empty);
            if (!string.IsNullOrWhiteSpace(bewaard)) return bewaard;
        }
        catch (Exception)
        {
            // Geen opslag; dan de taal van het toestel.
        }

        var toestel = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return Keuzes.Any(k => k.Code == toestel) ? toestel : "nl";
    }
}

/// <summary>Eén taal in de keuzelijst.</summary>
/// <param name="Code">nl, en of tr.</param>
/// <param name="Naam">In de taal zelf: een Turkse gebruiker zoekt "Türkçe", niet "Turks".</param>
public sealed record TaalKeuze(string Code, string Naam);
