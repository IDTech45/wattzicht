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
        "NoData" => Taal.T("storing.geen-data"),
        "ZeroProduction" => Taal.T("storing.geen-opwek"),
        "Offline" => Taal.T("storing.offline"),
        "" => Taal.T("storing.storing"),
        var t => t
    };

    public string SinceText => TimeText.Since(Alert.Since);

    public string Advice => $"{Alert.Type}" switch
    {
        "NoData" => Taal.T("storing.advies-geen-data"),
        "ZeroProduction" => Taal.T("storing.advies-geen-opwek"),
        "Offline" => Taal.T("storing.advies-offline"),
        _ => Taal.T("storing.advies-overig")
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
        Title = Taal.T("meldingen.storingen");
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
