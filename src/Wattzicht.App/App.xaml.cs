using Wattzicht.App.Services;

namespace Wattzicht.App;

public partial class App : Application
{
    private readonly AuthService _auth;

    public App(AuthService auth)
    {
        InitializeComponent();
        _auth = auth;
        _auth.LoggedOut += OnLoggedOut;
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
