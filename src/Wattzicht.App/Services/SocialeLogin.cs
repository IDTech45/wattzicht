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

    /// <summary>Haalt het token op. Geeft null als de gebruiker het venster wegklikt.</summary>
    Task<string?> TokenAsync(string provider, CancellationToken ct = default);
}

/// <summary>Voor platformen waar nog niets voor gebouwd is: alles uit, geen knoppen.</summary>
public sealed class GeenSocialeLogin : ISocialeLogin
{
    public bool Ondersteunt(string provider) => false;

    public Task<string?> TokenAsync(string provider, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);
}
