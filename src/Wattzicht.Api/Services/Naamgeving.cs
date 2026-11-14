namespace Wattzicht.Api.Services;

/// <summary>
/// Hoe een woning aan zijn naam komt.
///
/// Het beginscherm begroet je met die naam: "Goedenavond, Ibrahim". Stond er niets, dan werd het
/// "Thuis", en dat is geen begroeting maar een beginwaarde die aan het licht komt. Terwijl de naam
/// er wél was: wie met Google of Apple binnenkomt stuurt hem mee in hetzelfde token dat wij toch al
/// controleren. Daar deden we niets mee.
///
/// De naam van de aanbieder is een voorstel en geen vaststaand gegeven. Iemand die zelf iets invult
/// houdt dat, en in Instellingen is het één veld om te wijzigen.
/// </summary>
public static class Naamgeving
{
    /// <summary>De naam die we gebruiken als iemand er zelf geen heeft opgegeven.</summary>
    public const string Standaard = "Thuis";

    /// <summary>
    /// De naam voor een nieuwe woning: wat de gebruiker invulde, anders wat de aanbieder weet,
    /// anders "Thuis".
    /// </summary>
    public static string Kies(string? opgegeven, string? vanAanbieder)
    {
        if (!string.IsNullOrWhiteSpace(opgegeven)) return opgegeven.Trim();
        return Bruikbaar(vanAanbieder) ? vanAanbieder!.Trim() : Standaard;
    }

    /// <summary>
    /// Mogen we de naam van de aanbieder overnemen bij een woning die er al is?
    ///
    /// Alleen als die nog op de beginwaarde staat. Een naam die iemand zelf heeft gekozen overschrijf
    /// je niet, ook niet met een naam die "beter" lijkt.
    /// </summary>
    public static bool MagOvernemen(string? huidig, string? vanAanbieder) =>
        Bruikbaar(vanAanbieder)
        && (string.IsNullOrWhiteSpace(huidig)
            || string.Equals(huidig.Trim(), Standaard, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Is dit een naam waar je iemand mee kunt aanspreken?
    ///
    /// Een e-mailadres niet: Apple geeft bij een verborgen adres soms het adres zelf terug, en
    /// "Goedenavond, km5tt5rms6@privaterelay.appleid.com" is erger dan Thuis. Een leeg veld ook niet,
    /// en iets van één teken evenmin.
    /// </summary>
    private static bool Bruikbaar(string? naam)
    {
        if (string.IsNullOrWhiteSpace(naam)) return false;

        var schoon = naam.Trim();
        return schoon.Length >= 2 && schoon.Length <= 60 && !schoon.Contains('@');
    }
}
