namespace Wattzicht.App.Services;

/// <summary>
/// Centrale app-configuratie. De API-URL verschilt per build en per platform:
/// in DEBUG praat de Android-emulator via 10.0.2.2 met localhost van de host,
/// de iOS-simulator kan localhost direct gebruiken.
/// </summary>
public static class AppConfig
{
    public const string AppName = "Wattzicht";

    /// <summary>Publiek laadtarief (€/kWh) als referentie voor laadpaal-besparing.</summary>
    public const decimal PublicChargeTariffEur = 0.59m;

    public static string ApiBaseUrl
    {
        get
        {
#if DEBUG
#if ANDROID
            return "http://10.0.2.2:8080";
#elif IOS
            return "http://localhost:8080";
#else
            return "http://localhost:8080";
#endif
#else
            return "https://api.wattzicht.nl";
#endif
        }
    }

    // ---- Firebase (pushmeldingen, Android) ----
    // Firebase-project 'laadcash' (gedeeld met LaadCash), Android-app nl.wattzicht.app.
    // Deze waarden staan ook in google-services.json en zijn geen geheimen (ze zitten in elke app-build).
    // Leegmaken = push uit.
    public const string FirebaseProjectId = "laadcash";
    public const string FirebaseAndroidAppId = "1:879195215094:android:bb3a301e8e11b6a6aac8d7";   // "mobilesdk_app_id", bv. 1:1234567890:android:abc123
    public const string FirebaseApiKey = "AIzaSyCriIPuD6simTzwSF_B4ix3ccZTAyi_ypk";          // "current_key"
    public const string FirebaseSenderId = "879195215094";        // "project_number"

    public static string Version => AppInfo.Current.VersionString;
    public static string Build => AppInfo.Current.BuildString;
}
