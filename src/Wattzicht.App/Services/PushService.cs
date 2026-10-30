using Wattzicht.Shared;

namespace Wattzicht.App.Services;

/// <summary>
/// Pushmeldingen: vraagt toestemming, haalt het Firebase-token op en meldt het toestel aan bij de API.
/// Werkt nu op Android. iOS volgt zodra er een APNs-sleutel en GoogleService-Info.plist zijn (net als bij LaadCash).
/// Alles is "best effort": zonder Firebase-config of zonder toestemming draait de app gewoon door.
/// </summary>
public sealed class PushService
{
    private const string TokenKey = "push_token";
    private readonly ApiClient _api;

    public PushService(ApiClient api) => _api = api;

    /// <summary>Scherm dat geopend moet worden na het aantikken van een melding ("advice" of "alerts").</summary>
    public static string? PendingRoute { get; set; }

    /// <summary>Wordt aangeroepen als Firebase een nieuw token geeft (bv. na herinstallatie).</summary>
    public static event EventHandler<string>? TokenRefreshed;

    internal static void RaiseTokenRefreshed(string token) => TokenRefreshed?.Invoke(null, token);

    /// <summary>Na inloggen en bij elke start: toestemming vragen en het toestel (opnieuw) aanmelden.</summary>
    public async Task RegisterAsync()
    {
        try
        {
#if ANDROID
            if (!Wattzicht.App.Push.FirebasePush.EnsureInitialized())
                return; // Geen Firebase-config in deze build.

            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
                if (status != PermissionStatus.Granted)
                    status = await Permissions.RequestAsync<Permissions.PostNotifications>();
                if (status != PermissionStatus.Granted)
                    return;
            }

            var token = await Wattzicht.App.Push.FirebasePush.GetTokenAsync();
            if (!string.IsNullOrWhiteSpace(token))
                await SendTokenAsync(token);
#else
            await Task.CompletedTask;
#endif
        }
        catch (Exception)
        {
            // Pushmeldingen zijn een extraatje; nooit de app laten crashen.
        }
    }

    /// <summary>Bij uitloggen: dit toestel afmelden zodat de volgende gebruiker geen meldingen van ons krijgt.</summary>
    public async Task UnregisterAsync()
    {
        var token = Preferences.Default.Get<string?>(TokenKey, null);
        if (!string.IsNullOrWhiteSpace(token))
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _api.UnregisterPushDeviceAsync(token, timeout.Token);
            }
            catch (Exception)
            {
                // Offline of token al verlopen: zie hieronder.
            }
            Preferences.Default.Remove(TokenKey);
        }

#if ANDROID
        // Het Firebase-token van dit toestel wissen. Lukte de afmelding hierboven niet, dan meldt Google
        // het oude token bij de volgende verzending als ongeldig en ruimt de server de rij op.
        try
        {
            Wattzicht.App.Push.FirebasePush.DeleteToken();
        }
        catch (Exception)
        {
        }
#endif
    }

    public async Task SendTokenAsync(string token)
    {
        try
        {
            var platform = DeviceInfo.Platform == DevicePlatform.iOS ? "ios" : "android";
            await _api.RegisterPushDeviceAsync(new RegisterPushDeviceRequest(token, platform));
            Preferences.Default.Set(TokenKey, token);
        }
        catch (Exception)
        {
            // Niet ingelogd of offline: bij de volgende start opnieuw.
        }
    }

    /// <summary>Opent het scherm uit de laatst aangetikte melding, als die er is.</summary>
    public static async Task OpenPendingRouteAsync()
    {
        var route = PendingRoute;
        PendingRoute = null;
        if (Shell.Current is null) return;

        switch (route)
        {
            case "advice":
                await Shell.Current.GoToAsync("//main/advice");
                break;
            case "alerts":
                await Shell.Current.GoToAsync(AppShell.Routes.Home);
                await Shell.Current.GoToAsync(nameof(Pages.AlertsPage));
                break;
        }
    }
}
