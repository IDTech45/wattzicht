namespace Wattzicht.Api.Services;

/// <summary>
/// Wanneer we de dag bij de fabrikant ophalen: 's avonds, als hij voorbij is.
///
/// Overdag een dagtotaal ophalen levert een half antwoord op. De zon staat dan nog aan, dus het
/// getal dat je opschrijft klopt een uur later al niet meer, en bij elke volgende ronde schuift het
/// weer. Om tien uur 's avonds is de dag klaar: wat er dan staat is wat die dag is opgewekt.
///
/// Eén keer per woning per dag, en dat is ook wat het budget toelaat. Enphase telt aanroepen per
/// developer-app en niet per woning, dus met één gedeelde sleutel delen alle klanten samen duizend
/// per maand. Eén aanroep per dag is dertig per maand per woning, en daar passen er geen honderd in.
/// Vandaar ook de eigen sleutel in het koppelscherm: die heeft zijn eigen duizend.
///
/// Het venster is een uur breed en niet een moment. De ronde komt elk kwartier langs, een deploy of
/// een herstart mag er niet toe leiden dat een dag wordt overgeslagen, en zo hoeft de klok ook niet
/// precies te lopen.
/// </summary>
public static class AvondRonde
{
    /// <summary>Het uur waarop we de dag ophalen, Nederlandse tijd.</summary>
    public const int Uur = 22;

    /// <summary>Hoe lang het venster openstaat, in uren.</summary>
    public const int VensterUren = 1;

    /// <summary>
    /// Is het nu tijd voor deze woning?
    ///
    /// Alleen binnen het avondvenster, en alleen als de vorige keer van vóór het venster van vandaag
    /// is. Zonder dat tweede deel zou de ronde binnen dat uur elk kwartier opnieuw gaan, vier
    /// aanroepen in plaats van één.
    /// </summary>
    public static bool IsTijd(DateTimeOffset nu, DateTimeOffset? laatsteKeer)
    {
        var lokaal = DutchTime.ToLocal(nu);
        if (lokaal.Hour < Uur || lokaal.Hour >= Uur + VensterUren) return false;

        if (laatsteKeer is not { } vorige) return true;

        // Het begin van het venster van vandaag, op de lokale klok.
        var vensterStart = new DateTimeOffset(
            lokaal.Year, lokaal.Month, lokaal.Day, Uur, 0, 0, lokaal.Offset);

        return DutchTime.ToLocal(vorige) < vensterStart;
    }
}
