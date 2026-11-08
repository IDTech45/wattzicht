namespace Wattzicht.App.Services;

/// <summary>
/// Licht of donker in de app.
///
/// Donker is de standaard, net als op de site: de app laat energie zien en dat leest op navy
/// rustiger dan op wit. Wie liever licht werkt zet het om bij Instellingen, en die keuze blijft
/// staan. Bewust niet de telefoon volgen: dan ziet de ene bewoner een andere app dan de andere,
/// en zou een eigen keuze het systeem niet kunnen overrulen.
/// </summary>
public static class ThemaVoorkeur
{
    private const string Sleutel = "thema";

    /// <summary>Staat de app op donker? Dat is zo tenzij iemand licht heeft gekozen.</summary>
    public static bool IsDonker
    {
        get => Preferences.Default.Get(Sleutel, "dark") != "light";
        set
        {
            Preferences.Default.Set(Sleutel, value ? "dark" : "light");
            Pas();
        }
    }

    /// <summary>Zet het bewaarde thema op de app. Aanroepen bij het starten en na een wissel.</summary>
    public static void Pas()
    {
        if (Application.Current is { } app)
            app.UserAppTheme = IsDonker ? AppTheme.Dark : AppTheme.Light;
    }
}
