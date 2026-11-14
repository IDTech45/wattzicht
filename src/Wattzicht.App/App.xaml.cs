using Wattzicht.App.Services;

namespace Wattzicht.App;

public partial class App : Application
{
    private readonly AuthService _auth;
    private readonly PushService _push;

    public App(AuthService auth, PushService push)
    {
        InitializeComponent();
        // Donker is de standaard, licht kan bij Instellingen. Alle kleuren hebben beide varianten
        // (AppThemeBinding in Styles.xaml), dus de app staat er in allebei goed op.
        ThemaVoorkeur.Pas();
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
        // Het startscherm knippert en roept daarna dit aan. Zo valt het uitzoeken van waar je heen
        // moet samen met de animatie in plaats van erna: het kost dus geen extra wachttijd.
        Pages.StartPage.Daarna = RouteToStartAsync;
        return new Window(new AppShell());
    }

    /// <summary>
    /// Waar de app heen gaat na de knipper.
    ///
    /// Alles zit in een try, en dat is hier belangrijker dan ergens anders: dit wordt aangeroepen
    /// vanuit OnAppearing van het startscherm, en dat is een async void. Een fout die hier omhoog
    /// komt is er dus een die niemand meer opvangt, en dan sluit de app zich op het logo. Struikelt
    /// het verversen van het token, dan gaan we naar binnen met het token dat we al hadden; loopt
    /// zelfs dat vast, dan belandt iemand op het inlogscherm in plaats van op een splash die blijft
    /// staan.
    /// </summary>
    private async Task RouteToStartAsync()
    {
        var loggedIn = false;
        try
        {
            loggedIn = await _auth.IsLoggedInAsync();
            if (loggedIn)
            {
                await _auth.TryRefreshAsync();
            }
        }
        catch (Exception)
        {
            // Het token staat er nog; verder met wat we hebben.
        }

        try
        {
            await Shell.Current.GoToAsync(loggedIn ? AppShell.Routes.Home : AppShell.Routes.Login);
            if (loggedIn)
            {
                // Eerst naar het scherm uit de aangetikte melding; aanmelden voor push mag daarna, op de achtergrond.
                await PushService.OpenPendingRouteAsync();
                _ = _push.RegisterAsync();
            }
        }
        catch (Exception)
        {
            try
            {
                await Shell.Current.GoToAsync(AppShell.Routes.Login);
            }
            catch (Exception)
            {
                // Dan is er niets meer te doen; het startscherm blijft staan en is nog zichtbaar.
            }
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
