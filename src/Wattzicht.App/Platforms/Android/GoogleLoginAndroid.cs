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

    private static TaskCompletionSource<SocialeLoginUitkomst>? _wachtend;

    public bool Ondersteunt(string provider) =>
        string.Equals(provider, "google", StringComparison.OrdinalIgnoreCase);

    public Task<SocialeLoginUitkomst> TokenAsync(string provider, CancellationToken ct = default)
    {
        if (!Ondersteunt(provider))
        {
            return Task.FromResult(SocialeLoginUitkomst.Mislukt("Deze manier van inloggen kent de app niet."));
        }

        if (Platform.CurrentActivity is not Activity activity)
        {
            return Task.FromResult(SocialeLoginUitkomst.Mislukt("De app kon het inlogscherm niet openen."));
        }

        // Een vorige poging die nog open stond netjes afsluiten; anders blijft die voor altijd hangen
        // als iemand twee keer achter elkaar op de knop tikt.
        _wachtend?.TrySetResult(SocialeLoginUitkomst.Weggeklikt());
        _wachtend = new TaskCompletionSource<SocialeLoginUitkomst>(TaskCreationOptions.RunContinuationsAsynchronously);

        var opties = new GoogleSignInOptions.Builder(GoogleSignInOptions.DefaultSignIn)
            .RequestIdToken(WebClientId)
            .RequestEmail()
            .Build();

        var client = GoogleSignIn.GetClient(activity, opties);

        // Altijd eerst uitloggen. Anders kiest Google stilletjes het account van de vorige keer, en
        // kan iemand die zich met een ander account wil aanmelden dat niet.
        client.SignOut();

        activity.StartActivityForResult(client.SignInIntent, VerzoekCode);

        ct.Register(() => _wachtend?.TrySetResult(SocialeLoginUitkomst.Weggeklikt()));
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

        if (data is null)
        {
            wachtend.TrySetResult(SocialeLoginUitkomst.Weggeklikt());
            return;
        }

        try
        {
            var taak = GoogleSignIn.GetSignedInAccountFromIntent(data);
            var account = taak.GetResult(Java.Lang.Class.FromType(typeof(GmsApiException)))
                as GoogleSignInAccount;

            if (string.IsNullOrWhiteSpace(account?.IdToken))
            {
                // Aangemeld, maar zonder token. Dat gebeurt als de aanvraag zonder geldig
                // serverClientId is gedaan, en dan hebben wij niets om te controleren.
                wachtend.TrySetResult(SocialeLoginUitkomst.Mislukt(
                    "Google gaf geen inlogtoken terug. Laat ons dit weten, dan zetten wij het recht."));
                return;
            }

            wachtend.TrySetResult(SocialeLoginUitkomst.Gelukt(account.IdToken!));
        }
        catch (GmsApiException ex)
        {
            // De code van Google is het enige dat zegt wat er mis is. Hem opslokken was precies de
            // fout: je drukt op de knop, er gebeurt niets, en niemand kan zien waarom.
            wachtend.TrySetResult(ex.StatusCode switch
            {
                12501 => SocialeLoginUitkomst.Weggeklikt(),
                12502 => SocialeLoginUitkomst.Mislukt("Er loopt al een aanmelding. Probeer het zo opnieuw."),
                7 => SocialeLoginUitkomst.Mislukt("Geen verbinding met Google. Controleer je internet."),
                10 => SocialeLoginUitkomst.Mislukt(
                    "Google accepteert deze versie van de app nog niet (fout 10). Dit ligt aan onze instellingen, niet aan jou."),
                _ => SocialeLoginUitkomst.Mislukt($"Google weigerde de aanmelding (fout {ex.StatusCode})."),
            });
        }
    }
}
