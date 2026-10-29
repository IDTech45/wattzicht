using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wattzicht.App.Services;
using Wattzicht.Shared;

namespace Wattzicht.App.ViewModels;

public sealed class AlertItem
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    public AlertDto Alert { get; }
    public AlertItem(AlertDto alert) => Alert = alert;

    public string DeviceName => Alert.DeviceName;
    public string Message => string.IsNullOrWhiteSpace(Alert.Message) ? TypeText : Alert.Message;

    public string TypeText => $"{Alert.Type}" switch
    {
        "NoData" => "Geen data",
        "ZeroProduction" => "Geen opwek",
        "Offline" => "Offline",
        "" => "Storing",
        var t => t
    };

    public string SinceText => TimeText.Since(Alert.Since);

    public string Advice => $"{Alert.Type}" switch
    {
        "NoData" => "Controleer of het apparaat stroom en wifi heeft.",
        "ZeroProduction" => "Overdag geen opwek? Kijk of de omvormer een foutcode toont en bel anders je installateur.",
        "Offline" => "Het apparaat is niet bereikbaar. Herstart de dongle of router.",
        _ => "Neem contact op met je installateur als dit aanhoudt."
    };
}

public partial class AlertsViewModel : BaseViewModel
{
    private readonly ApiClient _api;
    private readonly AuthService _auth;

    public ObservableCollection<AlertItem> Alerts { get; } = [];

    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private bool _hasLoaded;

    public AlertsViewModel(ApiClient api, AuthService auth)
    {
        _api = api;
        _auth = auth;
        Title = "Storingen";
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        await RunSafeAsync(async () =>
        {
            var alerts = await _api.GetAlertsAsync();
            Alerts.Clear();
            foreach (var a in alerts) Alerts.Add(new AlertItem(a));
            HasLoaded = true;
        }, _auth);
        IsRefreshing = false;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        await LoadAsync();
    }
}
