using Android.App;
using Android.Content;
using AndroidX.Core.App;
using Firebase.Messaging;
using Wattzicht.App.Services;

namespace Wattzicht.App.Push;

/// <summary>
/// Ontvangt Firebase-berichten. Staat de app op de achtergrond, dan toont Android de melding zelf;
/// staat de app open, dan komt hij hier binnen en tonen we hem zelf.
/// </summary>
[Service(Exported = false)]
[IntentFilter(new[] { "com.google.firebase.MESSAGING_EVENT" })]
public sealed class WattzichtMessagingService : FirebaseMessagingService
{
    private static int _nextId = 1000;

    public override void OnNewToken(string token)
    {
        base.OnNewToken(token);
        PushService.RaiseTokenRefreshed(token);
    }

    public override void OnMessageReceived(RemoteMessage message)
    {
        base.OnMessageReceived(message);

        var notification = message.GetNotification();
        if (notification is null)
            return;

        var intent = new Intent(this, typeof(MainActivity));
        intent.AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        if (message.Data.TryGetValue("route", out var route) && !string.IsNullOrWhiteSpace(route))
            intent.PutExtra("route", route);

        var pending = PendingIntent.GetActivity(this, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var builder = new NotificationCompat.Builder(this, FirebasePush.ChannelId)
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetContentTitle(notification.Title)
            .SetContentText(notification.Body)
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(notification.Body))
            .SetAutoCancel(true)
            .SetContentIntent(pending);

        NotificationManagerCompat.From(this).Notify(Interlocked.Increment(ref _nextId), builder.Build());
    }
}
