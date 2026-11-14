using Wattzicht.App.Services;
using Wattzicht.App.ViewModels;

namespace Wattzicht.App;

public partial class App : Application
{
    private readonly AuthService _auth;
    private readonly PushService _push;
    private readonly IServiceProvider _diensten;

    /// <summary>
    /// Hoe lang het startscherm hoogstens op het dashboard wacht.
    ///
    /// Een grens moet er zijn. Op een slecht netwerk hoort niemand naar een logo te blijven kijken:
    /// dan gaat hij naar binnen en laadt het scherm zelf verder, met zijn eigen foutmelding als het
    /// echt niet lukt. Acht seconden is ruim voor een trage verbinding en kort genoeg om niet als
    /// vastlopen te voelen.
    /// </summary>
    private static readonly TimeSpan Geduld = TimeSpan.FromSeconds(8);

    public App(AuthService auth, PushService push, IServiceProvider diensten)
    {
        InitializeComponent();
        // Donker is de standaard, licht kan bij Instellingen. Alle kleuren hebben beide varianten
        // (AppThemeBinding in Styles.xaml), dus de app staat er in allebei goed op.
        ThemaVoorkeur.Pas();
        _auth = auth;
        _push = push;
        _diensten = diensten;
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

        // Het dashboard alvast ophalen terwijl het startscherm nog staat. Zo is het scherm gevuld op
        // het moment dat je het ziet, in plaats van dat je naar een lege balk kijkt die zelf nog moet
        // gaan laden. Het startscherm ademt door zolang dit duurt.
        if (loggedIn) await VoorladenAsync();

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

    /// <summary>
    /// Het dashboard ophalen voordat we erheen gaan, met een grens aan het geduld.
    ///
    /// Fouten blijven hier: dit is voorwerk zonder scherm. Lukt het niet, dan doet het dashboard het
    /// zelf nog een keer en krijgt de bewoner daar te zien wat er mis is, op de plek waar hij er
    /// iets mee kan.
    /// </summary>
    private async Task VoorladenAsync()
    {
        try
        {
            using var geduld = new CancellationTokenSource(Geduld);
            var dashboard = _diensten.GetRequiredService<DashboardViewModel>();
            await dashboard.VoorladenAsync(geduld.Token).WaitAsync(geduld.Token);
        }
        catch (Exception)
        {
            // Te traag of mislukt; dan gaan we gewoon naar binnen.
        }
    }

    private void OnLoggedOut(object? sender, EventArgs e)
    {
        // De cijfers van de vorige bewoner horen niet in het geheugen te blijven staan.
        DashboardViewModel.VergeetVoorraad();

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (Shell.Current is not null)
                await Shell.Current.GoToAsync(AppShell.Routes.Login);
        });
    }
}
