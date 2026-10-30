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
    // Eigen Firebase-project 'wattzicht' (los van LaadCash), Android-app nl.wattzicht.app.
    // Makkelijkst: zet google-services.json in Platforms/Android/; dan zijn deze waarden niet nodig.
    // Deze waarden staan ook in google-services.json en zijn geen geheimen (ze zitten in elke app-build).
    // Leegmaken = push uit.
    public const string FirebaseProjectId = "wattzicht";
    public const string FirebaseAndroidAppId = "1:664049832837:android:9cfb8ed45b584f4e2feaec";   // "mobilesdk_app_id", bv. 1:1234567890:android:abc123
    public const string FirebaseApiKey = "AIzaSyBFgWW0KmwLCR3YolnOiqL4rDuAj5dvVdM";          // "current_key"
    public const string FirebaseSenderId = "664049832837";        // "project_number"

    public static string Version => AppInfo.Current.VersionString;
    public static string Build => AppInfo.Current.BuildString;
}
