using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wattzicht.App.Drawables;
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

    // ---- Het weer ----
    // Waarom dit op het beginscherm staat: alle cijfers in deze app gaan over zon, en zonder het
    // weer ernaast kan niemand zien of een matige dag aan de panelen lag of aan de bewolking.
    [ObservableProperty] private bool _heeftWeer;
    [ObservableProperty] private string _weerVandaagTekst = string.Empty;
    [ObservableProperty] private string _weerVandaagTemp = string.Empty;
    [ObservableProperty] private string _weerVandaagExtra = string.Empty;
    [ObservableProperty] private bool _heeftWeerMorgen;
    [ObservableProperty] private string _weerMorgenTekst = string.Empty;
    [ObservableProperty] private string _weerMorgenTemp = string.Empty;
    [ObservableProperty] private bool _heeftZonMorgen;
    [ObservableProperty] private string _zonMorgenTekst = string.Empty;

    /// <summary>De twee weericoontjes; zie <see cref="WeerIcoonDrawable"/>.</summary>
    public WeerIcoonDrawable WeerVandaagIcoon { get; } = new();
    public WeerIcoonDrawable WeerMorgenIcoon { get; } = new();

    /// <summary>Gaat omhoog zodra er nieuw weer in staat, zodat de pagina de icoontjes opnieuw tekent.</summary>
    [ObservableProperty] private int _weerVersie;

    // ---- Wat we niet meten ----
    /// <summary>
    /// Komt er van het huis en het net niets binnen, dan staat dat er eerlijk bij.
    ///
    /// Veel woningen hebben alleen een omvormer: geen CT-klemmen, geen P1-meter. Dan stond er op dit
    /// scherm overal 0,0 kW, alsof het huis niets gebruikte en er niets naar het net ging. Dat is
    /// geen meting maar een leeg veld, en het is het soort stilzwijgen waar iemand verkeerde
    /// conclusies uit trekt.
    /// </summary>
    [ObservableProperty] private bool _toonNietGemeten;

    // ---- Uitnodiging om meer te koppelen ----
    /// <summary>
    /// Eén rustige kaart onder de energiestroom: je kunt er een batterij of een laadpaal bij hangen.
    ///
    /// De tegels verbergen zichzelf al als het apparaat er niet is, en daarmee verdween ook het idee
    /// dat het kán. Dit is de uitnodiging, en hij verdwijnt zodra het apparaat er staat.
    /// </summary>
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ToonKoppelUitnodiging))] private bool _mistBatterij;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ToonKoppelUitnodiging))] private bool _mistLaadpaal;

    public bool ToonKoppelUitnodiging => MistBatterij || MistLaadpaal;

    partial void OnLiveChanged(LiveSnapshotDto? value)
    {
        var fresh = value?.UpdatedAt is { } at && DateTimeOffset.UtcNow - at <= TimeSpan.FromMinutes(15);
        var exportW = fresh && value!.GridPowerW < 0 ? -value.GridPowerW : 0;
        HasSurplus = exportW >= 1500;
        SurplusText = HasSurplus
            ? string.Format(Taal.T("home.je-levert-nu-terug"),
                (exportW / 1000).ToString("0.0", CultureInfo.GetCultureInfo("nl-NL")))
            : string.Empty;

        // Pas iets beweren als er een momentopname is. Zonder meting weten we niet of er een
        // batterij hangt en al helemaal niet of er een meter in de kast zit.
        MistBatterij = value is { HasBattery: false };
        MistLaadpaal = value is { HasCharger: false };
        ToonNietGemeten = value is { HasLoadData: false, HasGridData: false };
    }

    [RelayCommand]
    private Task OpenAdviceAsync() => Shell.Current.GoToAsync(AppShell.Routes.Advice);
    [ObservableProperty] private string _updatedAtText = string.Empty;

    // Stat-tiles.
    //
    // Beginwaarde een streepje en geen nul: tot het eerste antwoord van de server weten we niets,
    // en "€ 0,00 bespaard" is dan geen uitkomst maar een leeg veld dat zich voordoet als een getal.
    [ObservableProperty] private string _savedTodayText = Onbekend;
    [ObservableProperty] private string _selfUsePercentText = Onbekend;
    [ObservableProperty] private string _pvTodayText = Onbekend;

    /// <summary>
    /// Dezelfde twee getallen als kale waarde, voor de zontegel bovenaan.
    ///
    /// Die tegel toonde alleen het vermogen van dit moment. Om 18:13 in november is dat nul, en dan
    /// staat het hele scherm op nul terwijl de panelen die dag gewoon gedraaid hebben.
    /// </summary>
    [ObservableProperty] private double _pvTodayKwh;
    [ObservableProperty] private double _pvPeakKw;

    // Deze maand
    [ObservableProperty] private string _monthSavedText = Onbekend;
    [ObservableProperty] private string _monthPvText = Onbekend;
    [ObservableProperty] private string _monthSelfUseText = Onbekend;
    [ObservableProperty] private string _monthExportText = Onbekend;
    [ObservableProperty] private string _monthChargedText = Onbekend;
    [ObservableProperty] private string _yearSavedText = Onbekend;

    public ObservableCollection<AlertDto> Alerts { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AlertsSummary))]
    private int _alertCount;

    public bool HasAlerts => AlertCount > 0;

    /// <summary>Uit het taalpakket: dit stond als vaste Nederlandse tekst in de viewmodel.</summary>
    public string AlertsSummary => AlertCount == 1
        ? Taal.T("storing.aantal-een")
        : string.Format(Taal.T("storing.aantal"), AlertCount);

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
        Title = Taal.T("shell.home");
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

                ZetWeer(await _api.WeerAsync());
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
    private Task AfmakenAsync() => Shell.Current.GoToAsync(AppShell.Routes.Settings);

    /// <summary>
    /// Het weer in het scherm zetten.
    ///
    /// Geen weer is geen fout: zonder plaats bij de woning is er geen verwachting, en dan blijft de
    /// kaart weg. Een weerkaart die nul graden en bewolkt zegt omdat er niets binnenkwam is erger
    /// dan geen weerkaart, want die eerste lijkt een meting.
    /// </summary>
    private void ZetWeer(WeerDto? weer)
    {
        HeeftWeer = weer is not null;
        if (weer is null)
        {
            HeeftWeerMorgen = false;
            HeeftZonMorgen = false;
            return;
        }

        WeerVandaagIcoon.Soort = weer.Vandaag.Soort;
        WeerVandaagTekst = SoortTekst(weer.Vandaag.Soort);
        WeerVandaagTemp = Temperatuur(weer.Vandaag);
        WeerVandaagExtra = weer.Vandaag.VerwachtKwh is { } kwh
            ? string.Format(Taal.T("weer.verwacht"), kwh.ToString("0.#", Nl))
            : string.Format(Taal.T("weer.zonuren"), weer.Vandaag.ZonUren.ToString("0.#", Nl));

        HeeftWeerMorgen = weer.Morgen is not null;
        if (weer.Morgen is { } morgen)
        {
            WeerMorgenIcoon.Soort = morgen.Soort;
            WeerMorgenTekst = SoortTekst(morgen.Soort);
            WeerMorgenTemp = Temperatuur(morgen);
        }

        HeeftZonMorgen = weer.ZonVanMorgen is not null;
        ZonMorgenTekst = weer.ZonVanMorgen is { } blok
            ? string.Format(Taal.T("weer.zon-morgen"),
                blok.VanUur.ToString("00") + ":00", blok.TotUur.ToString("00") + ":00")
            : string.Empty;

        WeerVersie++;
    }

    /// <summary>Hoogste en laagste temperatuur van een dag, als één regel.</summary>
    private static string Temperatuur(DagWeerDto dag) =>
        $"{dag.TempMaxC.ToString("0", Nl)}° / {dag.TempMinC.ToString("0", Nl)}°";

    private static string SoortTekst(WeerSoort soort) => soort switch
    {
        WeerSoort.Zonnig => Taal.T("weer.zonnig"),
        WeerSoort.HalfBewolkt => Taal.T("weer.halfbewolkt"),
        WeerSoort.Mist => Taal.T("weer.mist"),
        WeerSoort.Regen => Taal.T("weer.regen"),
        WeerSoort.Sneeuw => Taal.T("weer.sneeuw"),
        WeerSoort.Onweer => Taal.T("weer.onweer"),
        _ => Taal.T("weer.bewolkt"),
    };

    private void Apply(DashboardDto dashboard)
    {
        Live = dashboard.Live;
        UpdatedAtText = dashboard.Live is null
            ? Taal.T("home.nog-geen-meting")
            : string.Format(Taal.T("home.bijgewerkt"), TimeText.Ago(dashboard.Live.UpdatedAt, Taal.T("tijd.zojuist")));

        var today = dashboard.Today;
        var month = dashboard.ThisMonth;
        var year = dashboard.ThisYear;

        // Wat deze woning werkelijk meet. Zelf gebruikt en teruggeleverd hangen allebei aan de
        // netkant: de server rekent zelfverbruik als zon min teruglevering. Zonder P1-meter of
        // CT-klemmen staat die teruglevering op nul, en dan zou hier "100% zelfverbruik" en
        // "0 kWh teruggeleverd" staan bij iemand die de halve dag aan het net levert. Dat is geen
        // nul te veel maar een verzonnen uitkomst, en het is precies het getal waar een
        // zonnepaneeleigenaar naar kijkt.
        var meetNet = dashboard.Live?.HasGridData ?? false;
        var meetLaden = dashboard.Live?.HasCharger ?? false;
        var nietGemeten = Taal.T("besparen.niet-gemeten");

        SavedTodayText = Eur(today?.SavedEur);
        PvTodayText = Kwh(today?.PvKwh, 1);
        PvTodayKwh = today?.PvKwh is { } vandaag ? Convert.ToDouble(vandaag) : 0;
        SelfUsePercentText = meetNet ? SelfUsePercent(today) : nietGemeten;

        MonthSavedText = Eur(month?.SavedEur);
        MonthPvText = Kwh(month?.PvKwh, 0);
        MonthSelfUseText = meetNet ? Kwh(month?.SelfUseKwh, 0) : nietGemeten;
        MonthExportText = meetNet ? Kwh(month?.ExportKwh, 0) : nietGemeten;
        MonthChargedText = meetLaden ? Kwh(month?.ChargedKwh, 0) : nietGemeten;
        YearSavedText = Eur(year?.SavedEur);

        Alerts.Clear();
        if (dashboard.Alerts is not null)
            foreach (var alert in dashboard.Alerts)
                Alerts.Add(alert);
        AlertCount = Alerts.Count;
    }

    /// <summary>
    /// Welk deel van de zon je zelf gebruikte.
    ///
    /// Geen dag en geen opwek betekent geen percentage. Daar stond 0%, en dat leest als "je hebt
    /// alles teruggeleverd" terwijl er niets gemaakt is om te verdelen.
    /// </summary>
    private static string SelfUsePercent(DailySummaryDto? d)
    {
        if (d is null) return Onbekend;
        var pv = Convert.ToDouble(d.PvKwh);
        var self = Convert.ToDouble(d.SelfUseKwh);
        if (pv <= 0) return Onbekend;
        return $"{Math.Clamp(self / pv * 100, 0, 100):0}%";
    }

    /// <summary>
    /// Wat er staat als we het niet weten: een streepje.
    ///
    /// Zelfde afspraak als in de energiestroom. Nul is een uitkomst, en die hoort alleen te staan
    /// als hij ook gemeten is.
    /// </summary>
    private const string Onbekend = "–";

    internal static string Eur(object? value) =>
        value is null ? Onbekend : Convert.ToDecimal(value).ToString("C2", Nl);

    internal static string Kwh(object? value, int decimals) =>
        value is null
            ? Onbekend
            : Convert.ToDouble(value).ToString(decimals == 0 ? "N0" : "N1", Nl) + " kWh";
}
