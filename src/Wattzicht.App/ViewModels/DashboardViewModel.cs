using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wattzicht.App.Pages;
using Wattzicht.App.Services;
using Wattzicht.Shared;

namespace Wattzicht.App.ViewModels;

public partial class DashboardViewModel : BaseViewModel
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    private readonly ApiClient _api;
    private readonly AuthService _auth;

    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private bool _hasLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    private bool _hasDevices;

    [ObservableProperty] private LiveSnapshotDto? _live;

    /// <summary>Banner "je levert nu terug": alleen bij een verse meting en minstens 1,5 kW naar het net.</summary>
    [ObservableProperty] private bool _hasSurplus;
    [ObservableProperty] private string _surplusText = string.Empty;

    partial void OnLiveChanged(LiveSnapshotDto? value)
    {
        var fresh = value?.UpdatedAt is { } at && DateTimeOffset.UtcNow - at <= TimeSpan.FromMinutes(15);
        var exportW = fresh && value!.GridPowerW < 0 ? -value.GridPowerW : 0;
        HasSurplus = exportW >= 1500;
        SurplusText = HasSurplus
            ? $"Je levert nu {(exportW / 1000).ToString("0.0", CultureInfo.GetCultureInfo("nl-NL"))} kW terug"
            : string.Empty;
    }

    [RelayCommand]
    private Task OpenAdviceAsync() => Shell.Current.GoToAsync("//main/advice");
    [ObservableProperty] private string _updatedAtText = string.Empty;

    // Stat-tiles
    [ObservableProperty] private string _savedTodayText = "€ 0,00";
    [ObservableProperty] private string _selfUsePercentText = "0%";
    [ObservableProperty] private string _pvTodayText = "0,0 kWh";

    // Deze maand
    [ObservableProperty] private string _monthSavedText = "€ 0,00";
    [ObservableProperty] private string _monthPvText = "0 kWh";
    [ObservableProperty] private string _monthSelfUseText = "0 kWh";
    [ObservableProperty] private string _monthExportText = "0 kWh";
    [ObservableProperty] private string _monthChargedText = "0 kWh";
    [ObservableProperty] private string _yearSavedText = "€ 0,00";

    public ObservableCollection<AlertDto> Alerts { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AlertsSummary))]
    private int _alertCount;

    public bool HasAlerts => AlertCount > 0;
    public string AlertsSummary => AlertCount == 1 ? "1 storing" : $"{AlertCount} storingen";

    public bool ShowEmptyState => HasLoaded && !HasDevices && !HasError;
    public bool ShowContent => HasLoaded && HasDevices;

    public string Greeting
    {
        get
        {
            var hour = DateTime.Now.Hour;
            var dagdeel = hour < 6 ? "Goedenacht" : hour < 12 ? "Goedemorgen" : hour < 18 ? "Goedemiddag" : "Goedenavond";
            var name = _auth.Household?.Name;
            return string.IsNullOrWhiteSpace(name) ? dagdeel : $"{dagdeel}, {name}";
        }
    }

    public DashboardViewModel(ApiClient api, AuthService auth)
    {
        _api = api;
        _auth = auth;
        Title = "Home";
    }

    partial void OnAlertCountChanged(int value) => OnPropertyChanged(nameof(HasAlerts));
    partial void OnHasLoadedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowContent));
    }
    protected override void OnErrorChanged() => OnPropertyChanged(nameof(ShowEmptyState));

    [RelayCommand]
    public async Task LoadAsync()
    {
        await RunSafeAsync(async () =>
        {
            var devices = await _api.GetDevicesAsync();
            HasDevices = devices.Count > 0;

            if (HasDevices)
            {
                var dashboard = await _api.GetDashboardAsync();
                Apply(dashboard);
            }

            HasLoaded = true;
            OnPropertyChanged(nameof(Greeting));
        }, _auth);

        IsRefreshing = false;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        await LoadAsync();
    }

    [RelayCommand]
    private Task ConnectDeviceAsync() => Shell.Current.GoToAsync(nameof(ConnectDevicePage));

    [RelayCommand]
    private Task OpenAlertsAsync() => Shell.Current.GoToAsync(nameof(AlertsPage));

    private void Apply(DashboardDto dashboard)
    {
        Live = dashboard.Live;
        UpdatedAtText = dashboard.Live is null
            ? "Nog geen meting"
            : $"Bijgewerkt {TimeText.Ago(dashboard.Live.UpdatedAt, "zojuist")}";

        var today = dashboard.Today;
        var month = dashboard.ThisMonth;
        var year = dashboard.ThisYear;

        SavedTodayText = Eur(today?.SavedEur);
        PvTodayText = Kwh(today?.PvKwh, 1);
        SelfUsePercentText = SelfUsePercent(today);

        MonthSavedText = Eur(month?.SavedEur);
        MonthPvText = Kwh(month?.PvKwh, 0);
        MonthSelfUseText = Kwh(month?.SelfUseKwh, 0);
        MonthExportText = Kwh(month?.ExportKwh, 0);
        MonthChargedText = Kwh(month?.ChargedKwh, 0);
        YearSavedText = Eur(year?.SavedEur);

        Alerts.Clear();
        if (dashboard.Alerts is not null)
            foreach (var alert in dashboard.Alerts)
                Alerts.Add(alert);
        AlertCount = Alerts.Count;
    }

    private static string SelfUsePercent(DailySummaryDto? d)
    {
        if (d is null) return "0%";
        var pv = Convert.ToDouble(d.PvKwh);
        var self = Convert.ToDouble(d.SelfUseKwh);
        if (pv <= 0) return "0%";
        return $"{Math.Clamp(self / pv * 100, 0, 100):0}%";
    }

    internal static string Eur(object? value) =>
        Convert.ToDecimal(value ?? 0m).ToString("C2", Nl);

    internal static string Kwh(object? value, int decimals) =>
        Convert.ToDouble(value ?? 0d).ToString(decimals == 0 ? "N0" : "N1", Nl) + " kWh";
}
