# Wattzicht.App (.NET MAUI)

De mobiele app van Wattzicht voor Android en iOS: energiestroom nu, besparing per dag/week/maand/jaar, apparaten koppelen (Growatt, Sessy, HomeWizard, Demo) en tarief instellen. MVVM met CommunityToolkit.Mvvm, navigatie via Shell-tabs.

## Vereisten

- .NET 9 SDK met de MAUI-workload: `dotnet workload install maui`
- Android: Android SDK (API 35) + een emulator of toestel
- iOS: macOS met Xcode 16

## Bouwen

```bash
# Android
dotnet build -f net9.0-android

# Android, direct op emulator/toestel starten
dotnet build -f net9.0-android -t:Run

# iOS (alleen op macOS)
dotnet build -f net9.0-ios
dotnet build -f net9.0-ios -t:Run -p:_DeviceName=:v2:udid=<simulator-udid>
```

Release-build (geen HTTP meer toegestaan, productie-API):

```bash
dotnet publish -f net9.0-android -c Release
```

## API-URL instellen

De URL staat in `Services/AppConfig.cs`:

| Build   | Platform | URL                        |
| ------- | -------- | -------------------------- |
| Debug   | Android  | `http://10.0.2.2:8080`     |
| Debug   | iOS      | `http://localhost:8080`    |
| Release | beide    | `https://api.wattzicht.nl` |

`10.0.2.2` is het adres van je eigen computer vanuit de Android-emulator. Draait de API op een andere poort of een fysiek toestel? Pas dan de waarde in `AppConfig.ApiBaseUrl` aan (bijvoorbeeld `http://192.168.1.20:8080`) en voeg dat adres toe aan:

- Android: `Platforms/Android/NetworkSecurity/debug/network_security_config.xml`
- iOS: `Platforms/iOS/Info.Debug.plist` (`NSExceptionDomains`)

Beide bestanden worden alleen in Debug meegenomen (zie `Wattzicht.App.csproj`); in Release is uitsluitend HTTPS toegestaan.

## Structuur

```
Wattzicht.App/
├── App.xaml(.cs)             Resources + start (login of home)
├── AppShell.xaml(.cs)        Login buiten de tabs; tabs Home/Apparaten/Besparing/Instellingen
├── MauiProgram.cs            DI: HttpClient, ApiClient, AuthService, ViewModels, Pages
├── Pages/                    LoginPage, DashboardPage, DevicesPage, ConnectDevicePage, AlertsPage, SavingsPage, SettingsPage
├── ViewModels/               Eén ViewModel per pagina (CommunityToolkit.Mvvm)
├── Views/EnergyFlowView      "Energiestroom nu"-kaarten
├── Drawables/                Staafgrafiek (GraphicsView, geen externe lib)
├── Services/                 ApiClient, AuthService, TokenStore (SecureStorage), AppConfig, ApiException
├── Converters/               XAML-converters
├── Models/                   Presentatiemodellen (DeviceItem); DTO's komen uit Wattzicht.Shared
├── Resources/Styles/         Colors.xaml (BRAND.md-tokens) en Styles.xaml
├── Resources/AppIcon|Splash|Images
└── Platforms/Android|iOS
```

## Conventies

- Kleuren alleen via `Colors.xaml` (`{StaticResource Sun}` enz.), nooit hex in pagina's.
- Dark mode via `{AppThemeBinding Light=..., Dark=...}`; elke token heeft een `...Dark`-variant.
- Lijsten met `CollectionView` + `EmptyView`; knoppen minimaal 48 dp hoog.
- Alle teksten Nederlands, jij-vorm, kort.
- DTO's komen uit `Wattzicht.Shared`; wijkt een property-naam daar af, pas dan de ViewModel aan, niet Shared.
