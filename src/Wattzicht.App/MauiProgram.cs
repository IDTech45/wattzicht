using Microsoft.Extensions.Logging;
using Wattzicht.App.Pages;
using Wattzicht.App.Services;
using Wattzicht.App.ViewModels;

namespace Wattzicht.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                // Systeemfont (SF Pro / Roboto) volgens BRAND.md; geen custom fonts nodig.
            });

        // ---- Services ----
        builder.Services.AddSingleton<TokenStore>();
        builder.Services.AddSingleton(_ => new HttpClient
        {
            BaseAddress = new Uri(AppConfig.ApiBaseUrl),
            Timeout = TimeSpan.FromSeconds(20)
        });
        builder.Services.AddSingleton<ApiClient>();
        builder.Services.AddSingleton<AuthService>();

        // ---- ViewModels ----
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddTransient<DevicesViewModel>();
        builder.Services.AddTransient<ConnectDeviceViewModel>();
        builder.Services.AddTransient<AlertsViewModel>();
        builder.Services.AddTransient<SavingsViewModel>();
        builder.Services.AddTransient<AdviceViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();

        // ---- Pages ----
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<DevicesPage>();
        builder.Services.AddTransient<ConnectDevicePage>();
        builder.Services.AddTransient<AlertsPage>();
        builder.Services.AddTransient<SavingsPage>();
        builder.Services.AddTransient<AdvicePage>();
        builder.Services.AddTransient<SettingsPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
