using Android.App;
using Android.Content;
using Android.Gms.Auth.Api.SignIn;
using GmsApiException = Android.Gms.Common.Apis.ApiException;
using Wattzicht.App.Services;

namespace Wattzicht.App.Platforms.Android;

/// <summary>
/// Inloggen met Google op Android.
///
/// De gebruiker krijgt het keuzescherm van Google met de accounts die al op zijn toestel staan. Hij
/// kiest er een, Google geeft ons een ondertekend token terug, en dat sturen we door naar onze
/// server. Er komt geen wachtwoord langs deze app.
///
/// Let op het id dat we meegeven aan RequestIdToken: dat is met opzet de <b>web</b>-client, niet de
/// Android-client. Zo wil Google het: de Android-client zegt wie het vraagt, de web-client is voor
/// wie het token bedoeld is, en daar controleert onze server op. Vul je hier het Android-id in, dan
/// krijg je een token waar de server niets mee kan.
///
/// Het resultaat komt terug via onActivityResult van de activiteit. Daarom een TaskCompletionSource:
/// de aanroeper wacht netjes op het antwoord in plaats van op een gebeurtenis te moeten luisteren.
/// </summary>
public sealed class GoogleLoginAndroid : ISocialeLogin
{
    /// <summary>De web-client van het Cloud-project wattzicht; zie ExternalAuth in de server-config.</summary>
    private const string WebClientId =
        "664049832837-bt1teb98jmgkuc0proacljiaa3qfi777.apps.googleusercontent.com";

    internal const int VerzoekCode = 9001;

    private static TaskCompletionSource<string?>? _wachtend;

    public bool Ondersteunt(string provider) =>
        string.Equals(provider, "google", StringComparison.OrdinalIgnoreCase);

    public Task<string?> TokenAsync(string provider, CancellationToken ct = default)
    {
        if (!Ondersteunt(provider))
        {
            return Task.FromResult<string?>(null);
        }

        if (Platform.CurrentActivity is not Activity activity)
        {
            return Task.FromResult<string?>(null);
        }

        // Een vorige poging die nog open stond netjes afsluiten; anders blijft die voor altijd hangen
        // als iemand twee keer achter elkaar op de knop tikt.
        _wachtend?.TrySetResult(null);
        _wachtend = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var opties = new GoogleSignInOptions.Builder(GoogleSignInOptions.DefaultSignIn)
            .RequestIdToken(WebClientId)
            .RequestEmail()
            .Build();

        var client = GoogleSignIn.GetClient(activity, opties);

        // Altijd eerst uitloggen. Anders kiest Google stilletjes het account van de vorige keer, en
        // kan iemand die zich met een ander account wil aanmelden dat niet.
        client.SignOut();

        activity.StartActivityForResult(client.SignInIntent, VerzoekCode);

        ct.Register(() => _wachtend?.TrySetResult(null));
        return _wachtend.Task;
    }

    /// <summary>Wordt aangeroepen vanuit de activiteit zodra Google klaar is.</summary>
    internal static void Afhandelen(int verzoekCode, Result resultaat, Intent? data)
    {
        if (verzoekCode != VerzoekCode || _wachtend is null)
        {
            return;
        }

        var wachtend = _wachtend;
        _wachtend = null;

        if (resultaat != Result.Ok || data is null)
        {
            // Weggeklikt of afgebroken. Dat is geen fout: dan gebeurt er gewoon niets.
            wachtend.TrySetResult(null);
            return;
        }

        try
        {
            var taak = GoogleSignIn.GetSignedInAccountFromIntent(data);
            var account = taak.GetResult(Java.Lang.Class.FromType(typeof(GmsApiException)))
                as GoogleSignInAccount;
            wachtend.TrySetResult(account?.IdToken);
        }
        catch (GmsApiException)
        {
            // Google weigerde de aanmelding, bijvoorbeeld omdat de vingerafdruk van deze build niet
            // bij de client staat. Niets om hier op te lossen; het inlogscherm meldt het.
            wachtend.TrySetResult(null);
        }
    }
}
