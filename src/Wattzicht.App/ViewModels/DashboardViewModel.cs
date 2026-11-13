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
    private readonly LokaleMeter _lokaal;
    private readonly AuthService _auth;

    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private bool _hasLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowContent))]
    private bool _hasDevices;

    [ObservableProperty] private LiveSnapshotDto? _live;

    /// <summary>White-label: logo en naam van de installateur bovenaan.</summary>
    [ObservableProperty] private string? _installerLogo;
    [ObservableProperty] private string? _installerName;
    [ObservableProperty] private bool _hasInstallerLogo;

    /// <summary>Banner "je levert nu terug": alleen bij een verse meting en minstens 1,5 kW naar het net.</summary>
    // De tip van vandaag. Een kaart op het scherm dat iedereen opent, geen popup: wie bij elke
    // opstart iets moet wegklikken gaat de app mijden, en deze tip is die onderbreking niet waard.
    [ObservableProperty] private bool _hasDayTip;
    [ObservableProperty] private string _dayTipHeadline = string.Empty;
    [ObservableProperty] private string _dayTipBody = string.Empty;

    [ObservableProperty] private bool _hasSurplus;
    [ObservableProperty] private string _surplusText = string.Empty;

    partial void OnLiveChanged(LiveSnapshotDto? value)
    {
        var fresh = value?.UpdatedAt is { } at && DateTimeOffset.UtcNow - at <= TimeSpan.FromMinutes(15);
        var exportW = fresh && value!.GridPowerW < 0 ? -value.GridPowerW : 0;
        HasSurplus = exportW >= 1500;
        SurplusText = HasSurplus
            ? string.Format(Taal.T("home.je-levert-nu-terug"),
                (exportW / 1000).ToString("0.0", CultureInfo.GetCultureInfo("nl-NL")))
            : string.Empty;
    }

    [RelayCommand]
    private Task OpenAdviceAsync() => Shell.Current.GoToAsync("//main/advice");
    [ObservableProperty] private string _updatedAtText = string.Empty;

    // Stat-tiles
    [ObservableProperty] private string _savedTodayText = "€ 0,00";
    [ObservableProperty] private string _selfUsePercentText = "0%";
    [ObservableProperty] private string _pvTodayText = "0,0 kWh";

    /// <summary>
    /// Dezelfde twee getallen als kale waarde, voor de zontegel bovenaan.
    ///
    /// Die tegel toonde alleen het vermogen van dit moment. Om 18:13 in november is dat nul, en dan
    /// staat het hele scherm op nul terwijl de panelen die dag gewoon gedraaid hebben.
    /// </summary>
    [ObservableProperty] private double _pvTodayKwh;
    [ObservableProperty] private double _pvPeakKw;

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

    // ---- Maak je app af ----
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ToonAfmaken))] private bool _plaatsOntbreekt;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ToonAfmaken))] private bool _tariefOntbreekt;

    /// <summary>
    /// Alleen tonen als er iets te doen is, en alleen als er al een apparaat hangt.
    ///
    /// Een lijstje met open stappen bovenaan een scherm dat verder leeg is, leest als een app die
    /// nog niet werkt. Eerst koppelen, dan pas de rest vragen.
    /// </summary>
    public bool ToonAfmaken => HasDevices && (PlaatsOntbreekt || TariefOntbreekt);

    public bool ShowEmptyState => HasLoaded && !HasDevices && !HasError;
    public bool ShowContent => HasLoaded && HasDevices;

    public string Greeting
    {
        get
        {
            var hour = DateTime.Now.Hour;
            var dagdeel = Taal.T(hour < 6 ? "home.goedenacht"
                : hour < 12 ? "home.goedemorgen"
                : hour < 18 ? "home.goedemiddag"
                : "home.goedenavond");
            var name = _auth.Household?.Name;
            return string.IsNullOrWhiteSpace(name) ? dagdeel : $"{dagdeel}, {name}";
        }
    }

    public DashboardViewModel(ApiClient api, AuthService auth, LokaleMeter lokaal)
    {
        _api = api;
        _auth = auth;
        _lokaal = lokaal;
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

            // Eerst de meterkast, dan pas het dashboard ophalen. Een P1-meter praat alleen op het
            // thuisnetwerk, dus dit is het moment waarop die meting er kan zijn: de telefoon is hier
            // en de app staat open. Lukt het niet (niet thuis, kastje uit), dan gaat de rest gewoon
            // door; de meter houdt zijn eigen tellers bij en loopt bij de volgende keer weer bij.
            if (HasDevices) await _lokaal.StuurDoorAsync(devices);

            if (HasDevices)
            {
                var dashboard = await _api.GetDashboardAsync();
                Apply(dashboard);

                var tip = await _api.GetDayTipAsync();
                HasDayTip = tip is not null;
                DayTipHeadline = tip?.Headline ?? string.Empty;
                DayTipBody = tip?.Body ?? string.Empty;
            }

            // Twee dingen waar de app zonder niet goed werkt, en die je nergens vanzelf tegenkomt:
            // zonder plaats geen zonverwachting, zonder tarief geen besparing in euro's.
            try
            {
                var woning = await _api.GetHouseholdAsync();
                PlaatsOntbreekt = !woning.HasLocation;
                PvPeakKw = woning.PvPeakKw ?? 0;

                var tarief = await _api.GetTariffAsync();
                TariefOntbreekt = tarief.ImportTariffEur <= 0;
            }
            catch (ApiException)
            {
                PlaatsOntbreekt = false;
                TariefOntbreekt = false;
            }

            if (InstallerName is null)
            {
                try
                {
                    var installer = await _api.GetInstallerAsync();
                    InstallerName = installer.Name;
                    InstallerLogo = installer.LogoDataUrl;
                    HasInstallerLogo = !string.IsNullOrWhiteSpace(installer.LogoDataUrl);
                }
                catch (ApiException ex) when (!ex.IsUnauthorized)
                {
                    // Geen logo is geen probleem.
                }
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

    /// <summary>Naar het scherm waar die twee dingen staan.</summary>
    [RelayCommand]
    private Task AfmakenAsync() => Shell.Current.GoToAsync("//settings");

    private void Apply(DashboardDto dashboard)
    {
        Live = dashboard.Live;
        UpdatedAtText = dashboard.Live is null
            ? Taal.T("home.nog-geen-meting")
            : string.Format(Taal.T("home.bijgewerkt"), TimeText.Ago(dashboard.Live.UpdatedAt, Taal.T("tijd.zojuist")));

        var today = dashboard.Today;
        var month = dashboard.ThisMonth;
        var year = dashboard.ThisYear;

        SavedTodayText = Eur(today?.SavedEur);
        PvTodayText = Kwh(today?.PvKwh, 1);
        PvTodayKwh = today?.PvKwh is { } vandaag ? Convert.ToDouble(vandaag) : 0;
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
