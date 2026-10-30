using Android.App;
using Android.Content;
using Android.Gms.Tasks;
using Android.OS;
using Firebase;
using Firebase.Messaging;
using Wattzicht.App.Services;

namespace Wattzicht.App.Push;

/// <summary>
/// Firebase op Android. Twee manieren om te configureren:
/// <list type="number">
/// <item>Zet <c>google-services.json</c> in <c>Platforms/Android/</c>: Firebase start dan vanzelf.</item>
/// <item>Of vul de vier waarden in <see cref="AppConfig"/> in (staan ook in google-services.json; geen geheimen).</item>
/// </list>
/// Zonder een van beide is push uit en draait de app gewoon.
/// </summary>
public static class FirebasePush
{
    public const string ChannelId = "wattzicht"; // gelijk aan FcmPushSender.AndroidChannelId op de server

    private static Context AndroidContext => global::Android.App.Application.Context;

    public static bool EnsureInitialized()
    {
        CreateChannel();

        if (FirebaseApp.GetApps(AndroidContext).Count > 0)
            return true;

        if (string.IsNullOrWhiteSpace(AppConfig.FirebaseAndroidAppId)
            || string.IsNullOrWhiteSpace(AppConfig.FirebaseApiKey)
            || string.IsNullOrWhiteSpace(AppConfig.FirebaseProjectId)
            || string.IsNullOrWhiteSpace(AppConfig.FirebaseSenderId))
            return false;

        var options = new FirebaseOptions.Builder()
            .SetApplicationId(AppConfig.FirebaseAndroidAppId)
            .SetApiKey(AppConfig.FirebaseApiKey)
            .SetProjectId(AppConfig.FirebaseProjectId)
            .SetGcmSenderId(AppConfig.FirebaseSenderId)
            .Build();
        FirebaseApp.InitializeApp(AndroidContext, options);
        return true;
    }

    public static System.Threading.Tasks.Task<string?> GetTokenAsync()
    {
        var listener = new TokenListener();
        FirebaseMessaging.Instance.GetToken().AddOnCompleteListener(listener);
        return listener.Task;
    }

    /// <summary>Bij uitloggen: token van dit toestel ongeldig maken.</summary>
    public static void DeleteToken()
    {
        if (FirebaseApp.GetApps(AndroidContext).Count > 0)
            FirebaseMessaging.Instance.DeleteToken();
    }

    /// <summary>Android 8+ toont meldingen alleen in een kanaal.</summary>
    private static void CreateChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
            return;

        var manager = (NotificationManager?)AndroidContext.GetSystemService(Context.NotificationService);
        if (manager?.GetNotificationChannel(ChannelId) is not null)
            return;

        var channel = new NotificationChannel(ChannelId, "Wattzicht", NotificationImportance.Default)
        {
            Description = "Storingen en tips om je zonnestroom zelf te gebruiken"
        };
        manager?.CreateNotificationChannel(channel);
    }

    /// <summary>Haalt de route uit een melding die de app opende (FCM zet de data-velden in de extras).</summary>
    public static void HandleIntent(Intent? intent)
    {
        var route = intent?.Extras?.GetString("route");
        if (!string.IsNullOrWhiteSpace(route))
            PushService.PendingRoute = route;
    }

    private sealed class TokenListener : Java.Lang.Object, IOnCompleteListener
    {
        private readonly TaskCompletionSource<string?> _tcs = new();

        public System.Threading.Tasks.Task<string?> Task => _tcs.Task;

        public void OnComplete(global::Android.Gms.Tasks.Task task) =>
            _tcs.TrySetResult(task.IsSuccessful ? task.Result?.ToString() : null);
    }
}
