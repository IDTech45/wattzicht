using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wattzicht.App.Services;
using Wattzicht.Shared;

namespace Wattzicht.App.ViewModels;

/// <summary>
/// Een kopje met de merken die daaronder vallen.
///
/// Stap 1 was één lange lijst waarin een omvormer, een batterij en een laadpaal door elkaar stonden.
/// Wie een thuisbatterij wil koppelen moest dan eerst alle laadpalen langs om te zien of de zijne
/// erbij stond. Nu kijk je eerst naar wat voor apparaat je hebt en pas daarna naar het merk, en dat
/// is ook de volgorde waarin mensen er zelf over denken.
/// </summary>
public sealed record BrandGroep(string Titel, string Uitleg, IReadOnlyList<BrandOption> Merken);

/// <summary>Eén merk-keuzekaart in stap 1.</summary>
public sealed partial class BrandOption : ObservableObject
{
    public DeviceBrand Brand { get; }
    public string Name { get; }
    public string Description { get; }
    public DeviceType DefaultType { get; }
    public Color Accent { get; }
    public string Initial => Name[..1];

    [ObservableProperty] private bool _isSelected;

    public BrandOption(DeviceBrand brand, string name, string description, string colorKey, DeviceType defaultType)
    {
        Brand = brand;
        Name = name;
        Description = description;
        DefaultType = defaultType;
        Accent = ThemeColors.Get(colorKey);
    }
}

public partial class ConnectDeviceViewModel : BaseViewModel
{
    private readonly ApiClient _api;
    private readonly LokaleMeter _lokaal;
    private readonly AuthService _auth;
    private readonly ApparaatZoeker _zoeker;

    // Alleen merken waar echt een koppeling voor is. De P1-meter is de bijzondere: die praat alleen
    // op het thuisnetwerk, dus die leest de app zelf uit en stuurt hij door. Daarom staat hij hier
    // zonder merknaam: elke dongle leest dezelfde poort uit.
    public IReadOnlyList<BrandOption> Brands { get; } =
    [
        new(DeviceBrand.Growatt, "Growatt", "Omvormer of batterij via ShinePhone-account", "Accent500", DeviceType.Inverter),
        new(DeviceBrand.Solax, "SolaX", "Omvormer of hybride met batterij via SolaXCloud", "Accent500", DeviceType.Inverter),
        new(DeviceBrand.Solax, "SolaX laadpaal", "Smart EV Charger met MID-meter via SolaXCloud", "EnergyCharger", DeviceType.Charger),
        new(DeviceBrand.Sessy, "Sessy", "Thuisbatterij met Sessy-dongle", "EnergyBattery", DeviceType.Battery),
        new(DeviceBrand.Zaptec, "Zaptec", "Laadpaal via je eigen Zaptec-account", "EnergyCharger", DeviceType.Charger),
        // Een Alfen heeft geen cloud waar wij bij mogen: wie hem via 50five, E-Flux of de lease heeft,
        // ziet zijn data alleen in hún portaal. De paal zelf heeft wel een lokale interface, dezelfde
        // die de monteur gebruikt. Daarom staat hij hier en niet op de website: dit kan alleen vanuit
        // het huis, net als de Sessy en de P1-meter.
        new(DeviceBrand.Alfen, "Alfen", "Laadpaal op je eigen wifi of netwerkkabel", "EnergyCharger", DeviceType.Charger),
        // Via de gateway in huis en niet via de cloud van Enphase. Die cloudroute loopt op een
        // developer-app van ons met duizend aanroepen per maand voor alle huizen samen, en geeft de
        // netkant niet. De gateway geeft opwek, huisverbruik en netkant in één antwoord.
        // Peblar heeft wel een cloud, maar alleen voor hun eigen app. De paal zelf heeft een
        // gedocumenteerde lokale API die de eigenaar aanzet in de webinterface. Dat kan alleen vanuit
        // het huis, dus net als de Alfen staat hij hier en niet op de website.
        new(DeviceBrand.Peblar, "Peblar", "Laadpaal op je eigen netwerk, met een token uit de paal", "EnergyCharger", DeviceType.Charger),
        new(DeviceBrand.Enphase, "Enphase", "Zonnepanelen via de IQ Gateway in je meterkast", "Accent500", DeviceType.Inverter),
        new(DeviceBrand.HomeWizard, "P1-meter", "Dongle op je slimme meter, leest je netafname", "EnergyGrid", DeviceType.P1Meter),
        new(DeviceBrand.Demo, "Demo", "Voorbeelddata om de app te proberen", "TextMuted", DeviceType.Inverter),
    ];

    /// <summary>
    /// Dezelfde merken, maar op soort apparaat. De volgorde is niet willekeurig: zon staat bovenaan
    /// omdat vrijwel iedereen daarmee begint, en de demo onderaan omdat dat geen apparaat is.
    /// </summary>
    public IReadOnlyList<BrandGroep> Groepen { get; }

    private IReadOnlyList<BrandGroep> MaakGroepen() =>
    [
        new("Zonnepanelen", "Je omvormer, of de gateway in je meterkast",
            Brands.Where(b => b.DefaultType == DeviceType.Inverter && b.Brand != DeviceBrand.Demo).ToList()),
        new("Thuisbatterij", "Je batterij of de dongle die erbij hoort",
            Brands.Where(b => b.DefaultType == DeviceType.Battery).ToList()),
        new("Laadpaal", "De paal waar je je auto mee laadt",
            Brands.Where(b => b.DefaultType == DeviceType.Charger).ToList()),
        new("Slimme meter", "Een dongle op je P1-poort; leest wat je van het net haalt",
            Brands.Where(b => b.DefaultType == DeviceType.P1Meter).ToList()),
        new("Even proberen", "Zonder apparaat kijken hoe de app werkt",
            Brands.Where(b => b.Brand == DeviceBrand.Demo).ToList()),
    ];

    // ---- Stap ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStep1))]
    [NotifyPropertyChangedFor(nameof(IsStep2))]
    [NotifyPropertyChangedFor(nameof(IsStep3))]
    [NotifyPropertyChangedFor(nameof(StepText))]
    [NotifyPropertyChangedFor(nameof(PrimaryButtonText))]
    private int _step = 1;

    public bool IsStep1 => Step == 1;
    public bool IsStep2 => Step == 2;
    public bool IsStep3 => Step == 3;
    public string StepText => $"Stap {Step} van 3";
    public string PrimaryButtonText => Step == 3 ? "Koppelen" : "Volgende";

    // ---- Merk ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBrand))]
    [NotifyPropertyChangedFor(nameof(IsGrowatt))]
    [NotifyPropertyChangedFor(nameof(IsSolax))]
    [NotifyPropertyChangedFor(nameof(IsSolaxCharger))]
    [NotifyPropertyChangedFor(nameof(IsSolaxInverter))]
    [NotifyPropertyChangedFor(nameof(SolaxSnLabel))]
    [NotifyPropertyChangedFor(nameof(SolaxSnPlaceholder))]
    [NotifyPropertyChangedFor(nameof(SolaxHelp))]
    [NotifyPropertyChangedFor(nameof(IsSessy))]
    [NotifyPropertyChangedFor(nameof(IsZaptec))]
    [NotifyPropertyChangedFor(nameof(IsAlfen))]
    [NotifyPropertyChangedFor(nameof(IsPeblar))]
    [NotifyPropertyChangedFor(nameof(IsEnphase))]
    [NotifyPropertyChangedFor(nameof(IsHomeWizard))]
    [NotifyPropertyChangedFor(nameof(IsDemo))]
    [NotifyPropertyChangedFor(nameof(BrandName))]
    [NotifyPropertyChangedFor(nameof(SummaryLine))]
    private BrandOption? _selectedBrand;

    public bool HasBrand => SelectedBrand is not null;
    public bool IsGrowatt => SelectedBrand?.Brand == DeviceBrand.Growatt;
    public bool IsSolax => SelectedBrand?.Brand == DeviceBrand.Solax;
    public bool IsSolaxCharger => IsSolax && SelectedBrand?.DefaultType == DeviceType.Charger;
    public bool IsSolaxInverter => IsSolax && !IsSolaxCharger;
    public string SolaxSnLabel => IsSolaxCharger ? "Serienummer laadpaal" : "Registratienummer dongle";
    public string SolaxSnPlaceholder => IsSolaxCharger ? "Staat op de zijkant van de laadpaal" : "Bijv. SXXXXXXXXX";
    public string SolaxHelp => IsSolaxCharger
        ? "Log in op solaxcloud.com, ga naar Service → API en kopieer je TokenID. Het serienummer van de laadpaal staat op de zijkant en in SolaXCloud bij het apparaat."
        : "Log in op solaxcloud.com, ga naar Service → API en kopieer je TokenID. Het registratienummer (10 tekens) staat op de wifi-stick aan je omvormer.";
    public bool IsSessy => SelectedBrand?.Brand == DeviceBrand.Sessy;
    public bool IsHomeWizard => SelectedBrand?.Brand == DeviceBrand.HomeWizard;
    public bool IsZaptec => SelectedBrand?.Brand == DeviceBrand.Zaptec;
    public bool IsAlfen => SelectedBrand?.Brand == DeviceBrand.Alfen;
    public bool IsPeblar => SelectedBrand?.Brand == DeviceBrand.Peblar;
    public bool IsEnphase => SelectedBrand?.Brand == DeviceBrand.Enphase;

    /// <summary>Merken die de app zelf uitleest; die moeten adres en inlog op de telefoon bewaren.</summary>
    private bool IsLokaalGelezen => IsHomeWizard || IsSessy || IsAlfen || IsEnphase || IsPeblar;
    public bool IsDemo => SelectedBrand?.Brand == DeviceBrand.Demo;
    public string BrandName => SelectedBrand?.Name ?? "";

    // ---- Velden ----
    [ObservableProperty] private string _apiToken = string.Empty;
    [ObservableProperty] private string _ipAddress = string.Empty;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _dongleSn = string.Empty;

    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasApiTokenError))] private string? _apiTokenError;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasIpAddressError))] private string? _ipAddressError;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasUsernameError))] private string? _usernameError;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasPasswordError))] private string? _passwordError;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasDongleSnError))] private string? _dongleSnError;

    public bool HasApiTokenError => !string.IsNullOrEmpty(ApiTokenError);
    public bool HasIpAddressError => !string.IsNullOrEmpty(IpAddressError);
    public bool HasUsernameError => !string.IsNullOrEmpty(UsernameError);
    public bool HasPasswordError => !string.IsNullOrEmpty(PasswordError);
    public bool HasDongleSnError => !string.IsNullOrEmpty(DongleSnError);

    public string SummaryLine => SelectedBrand?.Brand switch
    {
        DeviceBrand.Growatt => "API-token: " + Mask(ApiToken),
        DeviceBrand.Solax => $"TokenID {Mask(ApiToken)}, {(IsSolaxCharger ? "laadpaal" : "dongle")} {DongleSn.Trim().ToUpperInvariant()}",
        DeviceBrand.Sessy => $"Dongle op {IpAddress} als {Username}",
        DeviceBrand.HomeWizard => $"P1-meter op {IpAddress}",
        DeviceBrand.Alfen => $"Laadpaal op {IpAddress}",
        DeviceBrand.Peblar => $"Laadpaal op {IpAddress}, token {Mask(ApiToken)}",
        DeviceBrand.Enphase => $"Gateway op {IpAddress}, token {Mask(ApiToken)}",
        DeviceBrand.Demo => "Voorbeeldapparaten worden toegevoegd",
        _ => ""
    };

    [ObservableProperty] private bool _isDone;
    [ObservableProperty] private string _doneText = string.Empty;

    public ConnectDeviceViewModel(ApiClient api, AuthService auth, LokaleMeter lokaal, ApparaatZoeker zoeker)
    {
        _api = api;
        _auth = auth;
        _lokaal = lokaal;
        _zoeker = zoeker;
        Title = "Apparaat koppelen";
        Groepen = MaakGroepen();
    }

    partial void OnApiTokenChanged(string value) { ApiTokenError = null; OnPropertyChanged(nameof(SummaryLine)); }
    partial void OnIpAddressChanged(string value) { IpAddressError = null; OnPropertyChanged(nameof(SummaryLine)); }
    partial void OnUsernameChanged(string value) { UsernameError = null; OnPropertyChanged(nameof(SummaryLine)); }
    partial void OnPasswordChanged(string value) => PasswordError = null;
    partial void OnDongleSnChanged(string value) { DongleSnError = null; OnPropertyChanged(nameof(SummaryLine)); }

    // ---- Zelf zoeken op het netwerk ----
    //
    // Het IP-adres van een gateway of dongle staat nergens op het apparaat; daarvoor moet je in je
    // router kijken, en daar haakt iemand af. De telefoon zit op hetzelfde netwerk en kan het
    // gewoon opzoeken.
    [ObservableProperty] private bool _isZoeken;
    [ObservableProperty] private string? _zoekResultaat;

    [RelayCommand]
    private async Task ZoekApparaatAsync()
    {
        IsZoeken = true;
        ZoekResultaat = "Bezig met zoeken op je wifi-netwerk, dit duurt tot een halve minuut…";

        try
        {
            var gevonden = IsEnphase
                ? await _zoeker.ZoekEnphaseAsync()
                : IsHomeWizard
                    ? await _zoeker.ZoekP1Async()
                    : null;

            if (gevonden is null)
            {
                var subnet = ApparaatZoeker.EigenSubnet();
                ZoekResultaat = subnet is null
                    ? "Je telefoon zit niet op wifi. Verbind met je thuisnetwerk en probeer het opnieuw."
                    : $"Niets gevonden op {subnet}.1 tot {subnet}.254. Zit je telefoon op dezelfde wifi als het apparaat?";
                return;
            }

            IpAddress = gevonden.Adres;
            IpAddressError = null;
            ZoekResultaat = $"Gevonden: {gevonden.Soort} op {gevonden.Adres}";
        }
        finally
        {
            IsZoeken = false;
        }
    }

    /// <summary>De pagina van Enphase waar je zelf een token aanmaakt.</summary>
    [RelayCommand]
    private Task OpenEntrezAsync() =>
        Launcher.Default.OpenAsync("https://entrez.enphaseenergy.com");

    [RelayCommand]
    private void SelectBrand(BrandOption option)
    {
        foreach (var b in Brands) b.IsSelected = ReferenceEquals(b, option);
        SelectedBrand = option;
        ErrorMessage = null;
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        ErrorMessage = null;
        switch (Step)
        {
            case 1:
                if (!HasBrand) { ErrorMessage = "Kies eerst een merk."; return; }
                Step = 2;
                break;
            case 2:
                if (!ValidateFields()) return;
                Step = 3;
                break;
            case 3:
                await ConnectAsync();
                break;
        }
    }

    [RelayCommand]
    private async Task BackAsync()
    {
        ErrorMessage = null;
        if (Step > 1) Step--;
        else await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private Task FinishAsync() => Shell.Current.GoToAsync("..");

    private async Task ConnectAsync()
    {
        if (SelectedBrand is null) return;

        var credentials = new Dictionary<string, string>();
        switch (SelectedBrand.Brand)
        {
            case DeviceBrand.Growatt:
                credentials["token"] = ApiToken.Trim();
                break;
            case DeviceBrand.Solax:
                credentials["token"] = ApiToken.Trim();
                credentials[IsSolaxCharger ? "charger_sn" : "sn"] = DongleSn.Trim().ToUpperInvariant();
                break;
            case DeviceBrand.Sessy:
                credentials["ip"] = IpAddress.Trim();
                credentials["username"] = Username.Trim();
                credentials["password"] = Password;
                break;
            case DeviceBrand.HomeWizard:
                credentials["ip"] = IpAddress.Trim();
                break;
            case DeviceBrand.Zaptec:
                credentials["username"] = Username.Trim();
                credentials["password"] = Password;
                break;
            case DeviceBrand.Enphase:
                credentials["ip"] = IpAddress.Trim();
                credentials["token"] = ApiToken.Trim();
                break;
            case DeviceBrand.Peblar:
                credentials["ip"] = IpAddress.Trim();
                credentials["token"] = ApiToken.Trim();
                break;
            case DeviceBrand.Alfen:
                credentials["ip"] = IpAddress.Trim();
                // De gebruikersnaam van een Alfen is altijd admin. Daar een veld voor tonen levert
                // alleen typefouten op in iets wat niet kan variëren.
                credentials["username"] = "admin";
                credentials["password"] = Password;
                break;
        }

        await RunSafeAsync(async () =>
        {
            // De P1-meter eerst zelf aanspreken. De server kan dat niet, dus zou een typefout in het
            // adres pas weken later opvallen als "deze meter heeft nog nooit iets gestuurd".
            if (IsHomeWizard)
            {
                var proef = await _lokaal.HaalAsync(IpAddress.Trim());
                if (proef is null)
                {
                    Step = 2;
                    IpAddressError = "Geen meter gevonden op dit adres. Staat je telefoon op hetzelfde wifi-netwerk?";
                    return;
                }

                if (proef.Value.TryGetProperty("unique_id", out var uniek) && uniek.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    credentials["serial"] = uniek.GetString()!;
                }
            }

            var request = new ConnectDeviceRequest(SelectedBrand.Brand, SelectedBrand.DefaultType, credentials);
            try
            {
                var devices = await _api.ConnectDeviceAsync(request);

                // Het adres en de inloggegevens blijven op de telefoon. De server heeft er niets aan
                // en stuurt ze niet terug, dus zonder dit weet de app na een herstart niet waar ze
                // moet kijken en waarmee ze binnenkomt.
                //
                // Dit gold tot 09-11-2026 alleen voor de P1-meter. Een Sessy en een Alfen hebben
                // precies hetzelfde nodig, en zonder opgeslagen adres sloeg de lokale uitlezer ze
                // stilzwijgend over: gekoppeld op het scherm, nooit één meting. Er was geen
                // foutmelding, want er ging ook niets fout; er gebeurde alleen niets.
                if (devices is not null && IsLokaalGelezen)
                {
                    foreach (var apparaat in devices)
                    {
                        await LokaleMeter.BewaarAdresAsync(apparaat.Id, IpAddress.Trim());
                        if (IsSessy) await LokaleMeter.BewaarSessyInlogAsync(apparaat.Id, Username.Trim(), Password);
                        if (IsAlfen) await LokaleMeter.BewaarAlfenInlogAsync(apparaat.Id, "admin", Password);
                        if (IsEnphase) await LokaleMeter.BewaarEnphaseTokenAsync(apparaat.Id, ApiToken);
                        if (IsPeblar) await LokaleMeter.BewaarPeblarTokenAsync(apparaat.Id, ApiToken);
                    }
                }
                var count = devices?.Count ?? 0;
                DoneText = count switch
                {
                    0 => "Gekoppeld. Er zijn nog geen apparaten gevonden; dat kan even duren.",
                    1 => $"Gelukt! {devices![0].Name} is gekoppeld.",
                    _ => $"Gelukt! {count} apparaten zijn gekoppeld."
                };
                IsDone = true;
            }
            catch (ApiException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.UnprocessableEntity)
            {
                // Credentials afgekeurd door de connector: terug naar stap 2 met melding onder het veld.
                Step = 2;
                if (IsGrowatt) ApiTokenError = "Dit token wordt niet geaccepteerd. Controleer het in ShinePhone.";
                else if (IsSolax) ApiTokenError = "SolaXCloud accepteert dit token of dongle-nummer niet. Controleer beide.";
                else if (IsSessy) PasswordError = "De dongle accepteert deze gegevens niet.";
                else if (IsHomeWizard) IpAddressError = "Geen HomeWizard-meter gevonden op dit adres.";
                else if (IsZaptec) PasswordError = "Zaptec accepteert deze gegevens niet. Werkt het wel in de Zaptec-app zelf?";
                // Een Alfen laat maar één sessie tegelijk toe. Staat de ACE Service Installer van de
                // monteur open, dan word je geweigerd met een wachtwoord dat gewoon klopt.
                else if (IsEnphase) ApiTokenError = "De gateway accepteert dit token niet. Haal een nieuw token op bij entrez.enphaseenergy.com.";
                else if (IsAlfen) PasswordError = "De laadpaal accepteert dit niet. Controleer het wachtwoord, en sluit de ACE Service Installer als die openstaat.";
                // Een nieuw token aanmaken in de webinterface maakt het vorige ongeldig. Dat is bij
                // Peblar de gewone oorzaak van een weigering, dus noemen we hem meteen.
                else if (IsPeblar) ApiTokenError = "De laadpaal accepteert dit token niet. Maak in de webinterface van de paal een nieuw token aan; een nieuw token maakt het oude ongeldig.";
                else ErrorMessage = ex.Message;
            }
        }, _auth);
    }

    /// <summary>
    /// Een adres waar een kastje op je eigen netwerk mee te bereiken is.
    ///
    /// Vier getallen met punten ertussen, of een naam die het apparaat zelf op het netwerk omroept,
    /// zoals envoy.local of p1meter.local. Dat tweede hoort erbij sinds de app het adres zelf
    /// opzoekt: hij vindt de gateway op zijn naam, vult die in, en dan werd zijn eigen vondst
    /// vervolgens afgekeurd als "geen geldig IP-adres".
    ///
    /// Voor de getalvorm niet IPAddress.TryParse gebruiken: die accepteert de oude verkorte
    /// schrijfwijze, waarin "192.168.1" stilletjes 192.168.0.1 wordt. Wie zijn adres half overtypt
    /// kreeg dan geen foutmelding maar een poging naar een heel ander apparaat in zijn huis.
    /// </summary>
    private static bool IsBruikbaarAdres(string waarde)
    {
        var schoon = waarde.Trim();
        if (schoon.Length == 0) return false;

        if (schoon.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || schoon.EndsWith(".home", StringComparison.OrdinalIgnoreCase)
            || schoon.EndsWith(".lan", StringComparison.OrdinalIgnoreCase))
        {
            return schoon.Length > ".local".Length;
        }

        var delen = schoon.Split('.');
        return delen.Length == 4
               && delen.All(d => d.Length is > 0 and <= 3 && d.All(char.IsAsciiDigit) && int.Parse(d) <= 255);
    }

    private bool ValidateFields()
    {
        ApiTokenError = IpAddressError = UsernameError = PasswordError = DongleSnError = null;

        if (IsGrowatt)
        {
            if (string.IsNullOrWhiteSpace(ApiToken)) ApiTokenError = "Vul je API-token in.";
            else if (ApiToken.Trim().Length < 16) ApiTokenError = "Dit token lijkt te kort.";
        }
        if (IsSolax)
        {
            if (string.IsNullOrWhiteSpace(ApiToken)) ApiTokenError = "Vul je SolaXCloud TokenID in.";
            else if (ApiToken.Trim().Length < 16) ApiTokenError = "Dit TokenID lijkt te kort.";
            var sn = DongleSn.Trim();
            if (IsSolaxCharger)
            {
                if (string.IsNullOrWhiteSpace(sn)) DongleSnError = "Vul het serienummer van de laadpaal in.";
                else if (sn.Length < 8) DongleSnError = "Dit serienummer lijkt te kort.";
            }
            else if (string.IsNullOrWhiteSpace(sn)) DongleSnError = "Vul het registratienummer van de dongle in.";
            else if (sn.Length != 10) DongleSnError = "Het registratienummer heeft 10 tekens.";
        }
        if (IsSessy || IsHomeWizard || IsAlfen || IsEnphase || IsPeblar)
        {
            if (string.IsNullOrWhiteSpace(IpAddress)) IpAddressError = "Vul het IP-adres in.";
            else if (!IsBruikbaarAdres(IpAddress))
                IpAddressError = "Vul een adres in zoals 192.168.1.50, of laat de app je apparaat opzoeken.";
        }
        if (IsSessy)
        {
            if (string.IsNullOrWhiteSpace(Username)) UsernameError = "Vul de gebruikersnaam van de dongle in.";
            if (string.IsNullOrEmpty(Password)) PasswordError = "Vul het wachtwoord van de dongle in.";
        }
        if (IsEnphase && string.IsNullOrWhiteSpace(ApiToken))
            ApiTokenError = "Vul het token van je gateway in.";
        if (IsPeblar && string.IsNullOrWhiteSpace(ApiToken))
            ApiTokenError = "Vul het API-token van je laadpaal in.";
        if (IsAlfen && string.IsNullOrEmpty(Password))
            PasswordError = "Vul het wachtwoord van je laadpaal in. Dat heb je van je installateur gekregen.";
        if (IsZaptec)
        {
            if (string.IsNullOrWhiteSpace(Username)) UsernameError = "Vul het e-mailadres van je Zaptec-account in.";
            if (string.IsNullOrEmpty(Password)) PasswordError = "Vul het wachtwoord van je Zaptec-account in.";
        }

        return !HasApiTokenError && !HasIpAddressError && !HasUsernameError && !HasPasswordError && !HasDongleSnError;
    }

    private static string Mask(string value)
    {
        var v = value.Trim();
        return v.Length <= 6 ? "••••••" : $"{v[..3]}…{v[^3..]}";
    }
}
