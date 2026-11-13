using Wattzicht.App.Pages;

namespace Wattzicht.App;

public partial class AppShell : Shell
{
    /// <summary>Absolute routes voor navigatie vanuit code.</summary>
    public static class Routes
    {
        public const string Start = "//start";
        public const string Login = "//login";
        public const string Home = "//main/home";
        public const string Devices = "//main/devices";
        public const string Savings = "//main/savings";
        public const string Settings = "//main/settings";
    }

    public AppShell()
    {
        InitializeComponent();

        // Detail-routes (worden op de navigatiestack gepusht)
        Routing.RegisterRoute(nameof(ConnectDevicePage), typeof(ConnectDevicePage));
        Routing.RegisterRoute(nameof(AlertsPage), typeof(AlertsPage));
        Routing.RegisterRoute(nameof(PanelsPage), typeof(PanelsPage));
        Routing.RegisterRoute(nameof(EnphaseSleutelPage), typeof(EnphaseSleutelPage));

        Navigated += OpNieuwScherm;
    }

    /// <summary>
    /// Elk scherm komt rustig in beeld in plaats van er ineens te staan.
    ///
    /// Shell wisselt een pagina hard om: het ene moment staat er het een, het volgende het ander.
    /// Op een telefoon leest dat als een sprong, en bij het wisselen van tabblad vooral als een
    /// flits. Een korte opkomst met een duwtje van onderen maakt duidelijk dat er iets nieuws staat,
    /// zonder dat je erop hoeft te wachten: tweehonderd milliseconden, en de pagina is al bruikbaar
    /// terwijl hij komt.
    ///
    /// Alles zit in een try: een animatie is versiering en mag nooit een scherm tegenhouden.
    /// </summary>
    private static async void OpNieuwScherm(object? afzender, ShellNavigatedEventArgs e)
    {
        if (Current?.CurrentPage is not ContentPage pagina || pagina.Content is not { } inhoud) return;

        try
        {
            inhoud.Opacity = 0;
            inhoud.TranslationY = 14;

            await Task.WhenAll(
                inhoud.FadeTo(1, 200, Easing.CubicOut),
                inhoud.TranslateTo(0, 0, 200, Easing.CubicOut));
        }
        catch (Exception)
        {
            inhoud.Opacity = 1;
            inhoud.TranslationY = 0;
        }
    }
}
