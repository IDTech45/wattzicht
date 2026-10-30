using Android.App;
using Android.Content.PM;
using Android.OS;

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

    protected override void OnNewIntent(Android.Content.Intent? intent)
    {
        base.OnNewIntent(intent);
        Wattzicht.App.Push.FirebasePush.HandleIntent(intent);
        // App stond al open: meteen naar het juiste scherm.
        Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(async () =>
            await Wattzicht.App.Services.PushService.OpenPendingRouteAsync());
    }
}
