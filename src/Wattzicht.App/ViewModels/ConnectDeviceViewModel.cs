using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wattzicht.App.Services;
using Wattzicht.Shared;

namespace Wattzicht.App.ViewModels;

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
    private readonly AuthService _auth;

    public IReadOnlyList<BrandOption> Brands { get; } =
    [
        new(DeviceBrand.Growatt, "Growatt", "Omvormer of batterij via ShinePhone-account", "Sun", DeviceType.Inverter),
        new(DeviceBrand.Sessy, "Sessy", "Thuisbatterij met Sessy-dongle", "Battery", DeviceType.Battery),
        new(DeviceBrand.HomeWizard, "HomeWizard", "P1-meter op je slimme meter", "Export", DeviceType.P1Meter),
        new(DeviceBrand.Demo, "Demo", "Voorbeelddata om de app te proberen", "Muted", DeviceType.Inverter),
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
    [NotifyPropertyChangedFor(nameof(IsSessy))]
    [NotifyPropertyChangedFor(nameof(IsHomeWizard))]
    [NotifyPropertyChangedFor(nameof(IsDemo))]
    [NotifyPropertyChangedFor(nameof(BrandName))]
    [NotifyPropertyChangedFor(nameof(SummaryLine))]
    private BrandOption? _selectedBrand;

    public bool HasBrand => SelectedBrand is not null;
    public bool IsGrowatt => SelectedBrand?.Brand == DeviceBrand.Growatt;
    public bool IsSessy => SelectedBrand?.Brand == DeviceBrand.Sessy;
    public bool IsHomeWizard => SelectedBrand?.Brand == DeviceBrand.HomeWizard;
    public bool IsDemo => SelectedBrand?.Brand == DeviceBrand.Demo;
    public string BrandName => SelectedBrand?.Name ?? "";

    // ---- Velden ----
    [ObservableProperty] private string _apiToken = string.Empty;
    [ObservableProperty] private string _ipAddress = string.Empty;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;

    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasApiTokenError))] private string? _apiTokenError;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasIpAddressError))] private string? _ipAddressError;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasUsernameError))] private string? _usernameError;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasPasswordError))] private string? _passwordError;

    public bool HasApiTokenError => !string.IsNullOrEmpty(ApiTokenError);
    public bool HasIpAddressError => !string.IsNullOrEmpty(IpAddressError);
    public bool HasUsernameError => !string.IsNullOrEmpty(UsernameError);
    public bool HasPasswordError => !string.IsNullOrEmpty(PasswordError);

    public string SummaryLine => SelectedBrand?.Brand switch
    {
        DeviceBrand.Growatt => "API-token: " + Mask(ApiToken),
        DeviceBrand.Sessy => $"Dongle op {IpAddress} als {Username}",
        DeviceBrand.HomeWizard => $"P1-meter op {IpAddress}",
        DeviceBrand.Demo => "Voorbeeldapparaten worden toegevoegd",
        _ => ""
    };

    [ObservableProperty] private bool _isDone;
    [ObservableProperty] private string _doneText = string.Empty;

    public ConnectDeviceViewModel(ApiClient api, AuthService auth)
    {
        _api = api;
        _auth = auth;
        Title = "Apparaat koppelen";
    }

    partial void OnApiTokenChanged(string value) { ApiTokenError = null; OnPropertyChanged(nameof(SummaryLine)); }
    partial void OnIpAddressChanged(string value) { IpAddressError = null; OnPropertyChanged(nameof(SummaryLine)); }
    partial void OnUsernameChanged(string value) { UsernameError = null; OnPropertyChanged(nameof(SummaryLine)); }
    partial void OnPasswordChanged(string value) => PasswordError = null;

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
            case DeviceBrand.Sessy:
                credentials["ip"] = IpAddress.Trim();
                credentials["username"] = Username.Trim();
                credentials["password"] = Password;
                break;
            case DeviceBrand.HomeWizard:
                credentials["ip"] = IpAddress.Trim();
                break;
        }

        await RunSafeAsync(async () =>
        {
            var request = new ConnectDeviceRequest(SelectedBrand.Brand, SelectedBrand.DefaultType, credentials);
            try
            {
                var devices = await _api.ConnectDeviceAsync(request);
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
                else if (IsSessy) PasswordError = "De dongle accepteert deze gegevens niet.";
                else if (IsHomeWizard) IpAddressError = "Geen HomeWizard-meter gevonden op dit adres.";
                else ErrorMessage = ex.Message;
            }
        }, _auth);
    }

    private bool ValidateFields()
    {
        ApiTokenError = IpAddressError = UsernameError = PasswordError = null;

        if (IsGrowatt)
        {
            if (string.IsNullOrWhiteSpace(ApiToken)) ApiTokenError = "Vul je API-token in.";
            else if (ApiToken.Trim().Length < 16) ApiTokenError = "Dit token lijkt te kort.";
        }
        if (IsSessy || IsHomeWizard)
        {
            if (string.IsNullOrWhiteSpace(IpAddress)) IpAddressError = "Vul het IP-adres in.";
            else if (!IPAddress.TryParse(IpAddress.Trim(), out _)) IpAddressError = "Dit is geen geldig IP-adres, bijv. 192.168.1.50.";
        }
        if (IsSessy)
        {
            if (string.IsNullOrWhiteSpace(Username)) UsernameError = "Vul de gebruikersnaam van de dongle in.";
            if (string.IsNullOrEmpty(Password)) PasswordError = "Vul het wachtwoord van de dongle in.";
        }

        return !HasApiTokenError && !HasIpAddressError && !HasUsernameError && !HasPasswordError;
    }

    private static string Mask(string value)
    {
        var v = value.Trim();
        return v.Length <= 6 ? "••••••" : $"{v[..3]}…{v[^3..]}";
    }
}
