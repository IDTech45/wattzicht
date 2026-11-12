namespace Wattzicht.App.Services;

/// <summary>
/// Inloggen met een account dat je al hebt: Google, en later Apple.
///
/// De app haalt bij die partij een ondertekend token op en stuurt dat naar onze server. Die
/// controleert het en logt je in, of maakt je account aan als we je nog niet kennen. Het wachtwoord
/// van dat account komt hier nooit langs; wij zien alleen het token.
///
/// Per platform een eigen uitvoering: op Android via Google Play-diensten, op iPhone later via de
/// ingebouwde Apple-aanmelding. Op een platform zonder uitvoering geeft dit niets terug en toont het
/// inlogscherm die knop niet, in plaats van een knop die bij het aantikken stukloopt.
/// </summary>
public interface ISocialeLogin
{
    /// <summary>Kan dit toestel überhaupt met deze partij inloggen?</summary>
    bool Ondersteunt(string provider);

    /// <summary>Haalt het token op.</summary>
    Task<SocialeLoginUitkomst> TokenAsync(string provider, CancellationToken ct = default);
}

/// <summary>
/// Wat het inloggen opleverde.
///
/// Drie uitkomsten, en het verschil doet ertoe. Een token betekent binnen. Afgebroken betekent dat
/// iemand het venster wegklikte, en dan hoort er niets te gebeuren. Een fout betekent dat het aan
/// onze kant of aan de instellingen ligt, en dat hoort op het scherm te staan: een knop waar je op
/// drukt en waarna er niets gebeurt, is het ergste wat je een gebruiker kunt voorzetten.
/// </summary>
public sealed record SocialeLoginUitkomst(string? Token, string? Fout, bool Afgebroken = false)
{
    public static SocialeLoginUitkomst Gelukt(string token) => new(token, null);
    public static SocialeLoginUitkomst Weggeklikt() => new(null, null, true);
    public static SocialeLoginUitkomst Mislukt(string reden) => new(null, reden);
}

/// <summary>Voor platformen waar nog niets voor gebouwd is: alles uit, geen knoppen.</summary>
public sealed class GeenSocialeLogin : ISocialeLogin
{
    public bool Ondersteunt(string provider) => false;

    public Task<SocialeLoginUitkomst> TokenAsync(string provider, CancellationToken ct = default) =>
        Task.FromResult(SocialeLoginUitkomst.Mislukt("Op dit toestel is dat nog niet beschikbaar."));
}
