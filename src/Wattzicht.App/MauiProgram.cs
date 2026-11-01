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
                // Zelfde fonts als LaadCash; zie docs/DESIGN-TOKENS.md. Instrument Sans en Bricolage
                // Grotesque bestaan bij Google Fonts alleen als variabele font, dus de gewichten zijn
                // met fontTools uitgesneden. Licentie: SIL OFL 1.1, zie Resources/Fonts/OFL.txt.
                fonts.AddFont("InstrumentSans-Regular.ttf", "Text");
                fonts.AddFont("InstrumentSans-Medium.ttf", "TextMedium");
                fonts.AddFont("InstrumentSans-SemiBold.ttf", "TextSemi");
                fonts.AddFont("BricolageGrotesque-SemiBold.ttf", "Display");
                fonts.AddFont("BricolageGrotesque-Bold.ttf", "DisplayBold");

                // Cijfers in mono: in een lijst met bedragen staan de cijfers dan onder elkaar, en een
                // getal dat van 9,99 naar 10,01 gaat laat de regel niet verspringen.
                fonts.AddFont("IBMPlexMono-Regular.ttf", "Mono");
                fonts.AddFont("IBMPlexMono-Medium.ttf", "MonoMedium");
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
        // Leest de P1-meter op het thuisnetwerk uit; heeft zijn eigen client met een korte tijdslimiet.
        builder.Services.AddSingleton<LokaleMeter>();
        builder.Services.AddSingleton<PushService>();

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
