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
    /// De terugknop van Android.
    ///
    /// Zonder dit doet Shell op een tabblad het enige wat hij kent: de app afsluiten. Sta je op
    /// Besparing en druk je op terug, dan ben je ineens buiten. Dat voelt als een crash, zeker omdat
    /// er geen scherm tussen zit.
    ///
    /// Nu gaat terug eerst naar het beginscherm, zoals elke app die vijf tabbladen heeft. Sta je al
    /// op het beginscherm, dan mag de app sluiten; dat is wel wat iemand daar bedoelt. Staat er een
    /// scherm bovenop (je panelen, het koppelen), dan handelt Shell het zelf af en gaan we een stap
    /// terug in plaats van naar huis.
    /// </summary>
    protected override bool OnBackButtonPressed()
    {
        try
        {
            if (Current?.Navigation?.NavigationStack.Count > 1
                || Current?.Navigation?.ModalStack.Count > 0)
            {
                return base.OnBackButtonPressed();
            }

            var route = Current?.CurrentState?.Location?.OriginalString ?? string.Empty;
            if (route.Contains("/home", StringComparison.OrdinalIgnoreCase)
                || route.Contains("login", StringComparison.OrdinalIgnoreCase)
                || route.Contains("start", StringComparison.OrdinalIgnoreCase))
            {
                return base.OnBackButtonPressed();
            }

            GoToAsync(Routes.Home);
            return true;
        }
        catch (Exception)
        {
            // Nooit vastlopen op de terugknop: in het ergste geval doet hij wat hij altijd deed.
            return base.OnBackButtonPressed();
        }
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
            // De richting volgt wat er gebeurde. Een scherm dat je opent komt van rechts, een stap
            // terug komt van links, en een ander tabblad komt van onderen. Dat is hoe een telefoon
            // zich hoort te gedragen: de beweging vertelt waar je heen gaat, nog voor je leest.
            var (vanX, vanY) = e.Source switch
            {
                ShellNavigationSource.Push => (46d, 0d),
                ShellNavigationSource.Pop or ShellNavigationSource.PopToRoot => (-46d, 0d),
                _ => (0d, 18d),
            };

            inhoud.Opacity = 0;
            inhoud.TranslationX = vanX;
            inhoud.TranslationY = vanY;
            inhoud.Scale = 0.985;

            await Task.WhenAll(
                inhoud.FadeTo(1, 240, Easing.CubicOut),
                inhoud.TranslateTo(0, 0, 260, Easing.CubicOut),
                inhoud.ScaleTo(1, 260, Easing.CubicOut));
        }
        catch (Exception)
        {
            // Versiering mag nooit een scherm tegenhouden; staat de animatie halverwege stil, dan
            // zetten we de pagina gewoon neer zoals hij hoort te staan.
            inhoud.Opacity = 1;
            inhoud.TranslationX = 0;
            inhoud.TranslationY = 0;
            inhoud.Scale = 1;
        }
    }
}
