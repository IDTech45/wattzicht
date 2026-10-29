using Wattzicht.App.Pages;

namespace Wattzicht.App;

public partial class AppShell : Shell
{
    /// <summary>Absolute routes voor navigatie vanuit code.</summary>
    public static class Routes
    {
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
    }
}
