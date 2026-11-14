using Android.App;
using Android.Content.PM;
using Android.OS;
using Result = Android.App.Result;

namespace Wattzicht.App;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode
                         | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    /// <summary>
    /// De terugknop van Android.
    ///
    /// Hij sloot de app af zodra je op een tabblad stond, want daar staat niets op de
    /// navigatiestapel en dan doet Android het enige wat hij kent. Een override op de Shell loste
    /// dat niet op: de knop komt hier binnen, bij de activiteit, en wordt alleen doorgegeven aan een
    /// pagina als er iets te sluiten valt.
    ///
    /// Nu gaat terug eerst naar het beginscherm, zoals in elke app met tabbladen. Sta je daar al,
    /// dan mag de app sluiten; dat is wat iemand op dat moment bedoelt.
    /// </summary>
    public override void OnBackPressed()
    {
        try
        {
            var shell = Microsoft.Maui.Controls.Shell.Current;
            if (shell is not null
                && shell.Navigation.NavigationStack.Count <= 1
                && shell.Navigation.ModalStack.Count == 0)
            {
                var route = shell.CurrentState?.Location?.OriginalString ?? string.Empty;
                var opHome = route.Contains("/home", StringComparison.OrdinalIgnoreCase)
                             || route.Contains("login", StringComparison.OrdinalIgnoreCase)
                             || route.Contains("start", StringComparison.OrdinalIgnoreCase);

                if (!opHome)
                {
                    Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(
                        async () => await shell.GoToAsync("//main/home"));
                    return;
                }
            }
        }
        catch (Exception)
        {
            // Nooit vastlopen op de terugknop; dan doet hij gewoon wat hij altijd deed.
        }

        base.OnBackPressed();
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Wattzicht.App.Push.FirebasePush.HandleIntent(Intent);
    }

    /// <summary>
    /// Het antwoord van het Google-inlogscherm komt hier binnen en gaat door naar de inlogdienst.
    /// Android levert dit nu eenmaal bij de activiteit af en niet bij de code die erom vroeg.
    /// </summary>
    protected override void OnActivityResult(int requestCode, Result resultCode, Android.Content.Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        Wattzicht.App.Platforms.Android.GoogleLoginAndroid.Afhandelen(requestCode, resultCode, data);
    }

    protected override void OnNewIntent(Android.Content.Intent? intent)
    {
        base.OnNewIntent(intent);
        Wattzicht.App.Push.FirebasePush.HandleIntent(intent);
        // App stond al open: meteen naar het juiste scherm.
        Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(async () =>
            await Wattzicht.App.Services.PushService.OpenPendingRouteAsync());
    }
}
