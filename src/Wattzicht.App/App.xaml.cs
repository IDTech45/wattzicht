using Wattzicht.App.Services;

namespace Wattzicht.App;

public partial class App : Application
{
    private readonly AuthService _auth;
    private readonly PushService _push;

    public App(AuthService auth, PushService push)
    {
        InitializeComponent();
        // Geen afgedwongen thema meer: alle kleuren hebben een light- en een dark-variant
        // (AppThemeBinding in Styles.xaml), dus de app volgt de telefoon. Zelfde gedrag als LaadCash.
        _auth = auth;
        _push = push;
        _auth.LoggedOut += OnLoggedOut;
        _auth.LoggedIn += (_, _) => MainThread.BeginInvokeOnMainThread(async () => await _push.RegisterAsync());
        PushService.TokenRefreshed += async (_, token) =>
        {
            if (await _auth.IsLoggedInAsync())
                await _push.SendTokenAsync(token);
        };
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell());
        window.Created += async (_, _) => await RouteToStartAsync();
        return window;
    }

    private async Task RouteToStartAsync()
    {
        var loggedIn = await _auth.IsLoggedInAsync();
        if (loggedIn)
        {
            await _auth.TryRefreshAsync();
            await Shell.Current.GoToAsync(AppShell.Routes.Home);
            // Eerst naar het scherm uit de aangetikte melding; aanmelden voor push mag daarna, op de achtergrond.
            await PushService.OpenPendingRouteAsync();
            _ = _push.RegisterAsync();
        }
        else
        {
            await Shell.Current.GoToAsync(AppShell.Routes.Login);
        }
    }

    private void OnLoggedOut(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (Shell.Current is not null)
                await Shell.Current.GoToAsync(AppShell.Routes.Login);
        });
    }
}
