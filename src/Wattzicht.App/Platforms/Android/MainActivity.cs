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
