using AuthenticationServices;
using Foundation;
using UIKit;
using Wattzicht.App.Services;

namespace Wattzicht.App.Platforms.iOS;

/// <summary>
/// Inloggen met Apple op de iPhone.
///
/// Dit moet er zijn: Apple keurt een app af die wél met Google laat inloggen en niet met Apple. En
/// het is de route die iPhone-gebruikers verwachten, met Face ID in plaats van een wachtwoord.
///
/// Hoe het werkt: iOS vraagt zelf om toestemming en geeft een ondertekend identity-token terug. Dat
/// token sturen we naar onze server, die het bij Apple controleert (zie ExternalAuth). Er komt geen
/// wachtwoord langs deze app, en wij bewaren geen Apple-gegevens.
///
/// Let op het e-mailadres: wie "Verberg mijn e-mailadres" kiest krijgt een privaterelay-adres. Dat
/// is een geldig adres waar post op aankomt, zolang onze afzender bij Apple geregistreerd staat.
/// Daarom staat de naam van de gebruiker hier niet in: Apple geeft die alleen bij de eerste
/// aanmelding mee, en een veld dat één keer gevuld is en daarna leeg blijft levert alleen verwarring
/// op. De server maakt de naam uit het account zelf.
/// </summary>
public sealed class AppleLoginIos : NSObject, ISocialeLogin,
    IASAuthorizationControllerDelegate, IASAuthorizationControllerPresentationContextProviding
{
    private TaskCompletionSource<SocialeLoginUitkomst>? _wachtend;

    public bool Ondersteunt(string provider) =>
        string.Equals(provider, "apple", StringComparison.OrdinalIgnoreCase)
        && UIDevice.CurrentDevice.CheckSystemVersion(13, 0);

    public Task<SocialeLoginUitkomst> TokenAsync(string provider, CancellationToken ct = default)
    {
        if (!Ondersteunt(provider))
        {
            return Task.FromResult(SocialeLoginUitkomst.Mislukt(Taal.T("sociaal.manier-onbekend")));
        }

        // Een vorige poging die nog open stond netjes afsluiten; anders blijft die voor altijd hangen
        // als iemand twee keer achter elkaar op de knop tikt.
        _wachtend?.TrySetResult(SocialeLoginUitkomst.Weggeklikt());
        _wachtend = new TaskCompletionSource<SocialeLoginUitkomst>(TaskCreationOptions.RunContinuationsAsynchronously);

        var verzoek = new ASAuthorizationAppleIdProvider().CreateRequest();
        verzoek.RequestedScopes = [ASAuthorizationScope.Email];

        var controller = new ASAuthorizationController([verzoek])
        {
            Delegate = this,
            PresentationContextProvider = this,
        };

        controller.PerformRequests();

        ct.Register(() => _wachtend?.TrySetResult(SocialeLoginUitkomst.Weggeklikt()));
        return _wachtend.Task;
    }

    [Export("authorizationController:didCompleteWithAuthorization:")]
    public void DidComplete(ASAuthorizationController controller, ASAuthorization authorization)
    {
        var wachtend = _wachtend;
        _wachtend = null;

        if (authorization.GetCredential<ASAuthorizationAppleIdCredential>() is not { } gegevens
            || gegevens.IdentityToken is null)
        {
            wachtend?.TrySetResult(SocialeLoginUitkomst.Mislukt(Taal.T("sociaal.geen-token")));
            return;
        }

        var token = new NSString(gegevens.IdentityToken, NSStringEncoding.UTF8).ToString();
        if (string.IsNullOrWhiteSpace(token))
        {
            wachtend?.TrySetResult(SocialeLoginUitkomst.Mislukt(Taal.T("sociaal.geen-token")));
            return;
        }

        wachtend?.TrySetResult(SocialeLoginUitkomst.Gelukt(token));
    }

    [Export("authorizationController:didCompleteWithError:")]
    public void DidComplete(ASAuthorizationController controller, NSError error)
    {
        var wachtend = _wachtend;
        _wachtend = null;

        // 1001 is "de gebruiker klikte het weg". Dat is geen fout en hoort niets op het scherm te
        // zetten; alles daarbuiten wel, want een knop waar je op drukt en waarna er niets gebeurt is
        // het ergste wat je iemand kunt voorzetten.
        var afgebroken = error.Code == (long)ASAuthorizationError.Canceled;

        wachtend?.TrySetResult(afgebroken
            ? SocialeLoginUitkomst.Weggeklikt()
            : SocialeLoginUitkomst.Mislukt(string.Format(Taal.T("sociaal.geweigerd"), error.Code)));
    }

    [Export("presentationAnchorForAuthorizationController:")]
    public UIWindow GetPresentationAnchor(ASAuthorizationController controller) =>
        UIApplication.SharedApplication.ConnectedScenes
            .ToArray()
            .OfType<UIWindowScene>()
            .SelectMany(s => s.Windows)
            .FirstOrDefault(w => w.IsKeyWindow)
        ?? UIApplication.SharedApplication.Windows.FirstOrDefault(w => w.IsKeyWindow)
        ?? new UIWindow();
}
