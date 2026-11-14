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

    /// <summary>
    /// Een binnengekomen melding tonen.
    ///
    /// Alles in een try: dit loopt in een achtergronddienst van Android, en wat hier omhoog komt
    /// neemt het hele proces mee. Een melding met een veld dat we niet verwachten is dan niet een
    /// melding die wegblijft maar een app die zich sluit terwijl de bewoner er niet eens naar keek.
    /// </summary>
    public override void OnMessageReceived(RemoteMessage message)
    {
        try
        {
            Toon(message);
        }
        catch (Exception)
        {
            // Een melding die niet gebracht kan worden is het verlies van die ene melding.
        }
    }

    private void Toon(RemoteMessage message)
    {
        base.OnMessageReceived(message);

        var notification = message.GetNotification();
        if (notification is null)
            return;

        var intent = new Intent(this, typeof(MainActivity));
        intent.AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        if (message.Data.TryGetValue("route", out var route) && !string.IsNullOrWhiteSpace(route))
            intent.PutExtra("route", route);

        var id = Interlocked.Increment(ref _nextId);
        var pending = PendingIntent.GetActivity(this, id, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var builder = new NotificationCompat.Builder(this, FirebasePush.ChannelId)
            .SetSmallIcon(Resource.Drawable.ic_stat_wattzicht)
            .SetContentTitle(notification.Title)
            .SetContentText(notification.Body)
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(notification.Body))
            .SetAutoCancel(true)
            .SetContentIntent(pending);

        NotificationManagerCompat.From(this).Notify(id, builder.Build());
    }
}
