using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wattzicht.App.Drawables;
using Wattzicht.App.Services;
using Wattzicht.Shared;

namespace Wattzicht.App.ViewModels;

public enum SavingsPeriod { Day, Week, Month, Year }

/// <summary>Eén regel in de besparingslijst.</summary>
public sealed class SummaryRow
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    /// <summary>
    /// Of de netkant van deze woning gemeten wordt.
    ///
    /// Zelf gebruikt en teruggeleverd komen allebei uit de netmeting: de server rekent zelfverbruik
    /// als zon min teruglevering. Zonder P1-meter staat er nul teruglevering, en dan zegt deze rij
    /// dat alles zelf gebruikt is. Dat is geen afrondingsfout maar een verzonnen uitkomst.
    /// </summary>
    private readonly bool _meetNet;

    public DailySummaryDto Summary { get; }

    public SummaryRow(DailySummaryDto summary, bool meetNet = true)
    {
        Summary = summary;
        _meetNet = meetNet;
    }

    public string DateText
    {
        get
        {
            var date = ToDate(Summary.Date);
            if (date == DateOnly.FromDateTime(DateTime.Today)) return Taal.T("home.vandaag");
            if (date == DateOnly.FromDateTime(DateTime.Today.AddDays(-1))) return Taal.T("besparen.gisteren");
            return date.ToString("ddd d MMM", Nl);
        }
    }

    public string PvText => Kwh(Summary.PvKwh);
    public string SelfUseText => _meetNet ? Kwh(Summary.SelfUseKwh) : Taal.T("besparen.niet-gemeten");
    public string ExportText => _meetNet ? Kwh(Summary.ExportKwh) : Taal.T("besparen.niet-gemeten");
    public string SavedText => Convert.ToDecimal(Summary.SavedEur).ToString("C2", Nl);

    private static string Kwh(object? v) => Convert.ToDouble(v ?? 0d).ToString("N1", Nl) + " kWh";

    internal static DateOnly ToDate(object? value) => value switch
    {
        DateOnly d => d,
        DateTime dt => DateOnly.FromDateTime(dt),
        DateTimeOffset dto => DateOnly.FromDateTime(dto.LocalDateTime),
        _ => DateOnly.FromDateTime(DateTime.Today)
    };
}

/// <summary>
/// Eén maand zoals de leverancier hem noemt, met onze eigen meting ernaast.
///
/// Die twee naast elkaar is het hele punt: lopen ze uit elkaar, dan wil je weten waarom. En wie
/// geen P1-meter heeft ziet hier als enige plek wat er van het net kwam.
/// </summary>
public sealed class LeverancierRij
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    public LeverancierRij(LeverancierMaandDto maand) => Maand = maand;

    public LeverancierMaandDto Maand { get; }

    public string MaandTekst =>
        new DateTime(Maand.Maand.Year, Maand.Maand.Month, 1).ToString("MMMM yyyy", Nl);

    public string AfnameTekst => Maand.AfnameKwh.ToString("N0", Nl) + " kWh";

    public string TerugTekst => Maand.TerugleveringKwh is { } t
        ? t.ToString("N0", Nl) + " kWh"
        : "–";

    /// <summary>Wat wij maten, of een streepje als we die maand nog niet meekeken.</summary>
    public string OnzeTekst => Maand.OnzeAfnameKwh is { } o
        ? o.ToString("N0", Nl) + " kWh"
        : "–";
}

public partial class SavingsViewModel : BaseViewModel
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    private readonly ApiClient _api;
    private readonly AuthService _auth;

    public SavingsChartDrawable Chart { get; } = new();

    public ObservableCollection<SummaryRow> Rows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDay))]
    [NotifyPropertyChangedFor(nameof(IsWeek))]
    [NotifyPropertyChangedFor(nameof(IsMonth))]
    [NotifyPropertyChangedFor(nameof(IsYear))]
    [NotifyPropertyChangedFor(nameof(PeriodTitle))]
    private SavingsPeriod _period = SavingsPeriod.Week;

    public bool IsDay => Period == SavingsPeriod.Day;
    public bool IsWeek => Period == SavingsPeriod.Week;
    public bool IsMonth => Period == SavingsPeriod.Month;
    public bool IsYear => Period == SavingsPeriod.Year;

    /// <summary>
    /// De periode in woorden. Een dag is een datum, een week een reeks, een maand een naam.
    ///
    /// Geen jaartal bij de lopende maand of week: dat weet je wel, en het maakt de regel langer dan
    /// de ruimte die ernaast staat. Bij een jaar staat het er juist wel, want dat is het hele punt.
    /// </summary>
    private string BereikTekst(DateOnly van, DateOnly tot) => Period switch
    {
        SavingsPeriod.Day => Datum(tot, "d MMMM"),
        SavingsPeriod.Week => string.Format(Taal.T("besparen.tot-en-met"), Datum(van, "d MMM"), Datum(tot, "d MMM")),
        SavingsPeriod.Month => Datum(tot, "MMMM yyyy"),
        _ => tot.Year.ToString(Nl),
    };

    private static string Datum(DateOnly dag, string vorm) =>
        new DateTime(dag.Year, dag.Month, dag.Day).ToString(vorm, Nl);

    public string PeriodTitle => Period switch
    {
        SavingsPeriod.Day => Taal.T("besparen.vandaag-bespaard"),
        SavingsPeriod.Week => Taal.T("besparen.deze-week-bespaard"),
        SavingsPeriod.Month => Taal.T("besparen.deze-maand-bespaard"),
        _ => Taal.T("home.dit-jaar-bespaard")
    };

    /// <summary>
    /// Of er vanavond nog cijfers bijkomen voor deze woning.
    ///
    /// Alleen bij een omvormer die wij bij de fabrikant kunnen opvragen. Een gateway die we via het
    /// thuisnetwerk lezen telt hier niet: die meet alleen terwijl de app openstaat, en dan zou de
    /// belofte "vanavond maken we de dag compleet" nergens op slaan.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DagLooptNog))]
    [NotifyPropertyChangedFor(nameof(DagLooptNogTekst))]
    private bool _avondAanvulling;

    /// <summary>
    /// Waarom het getal van vandaag lager staat dan je verwacht.
    ///
    /// Een dag die nog loopt is geen dagtotaal. Zonder deze regel is een lage stand om twee uur 's
    /// middags niet te onderscheiden van een storing, en dat is precies de vraag die erover kwam.
    /// </summary>
    public bool DagLooptNog => Period == SavingsPeriod.Day && Vandaag;

    public string DagLooptNogTekst =>
        Taal.T(AvondAanvulling ? "besparen.dag-loopt-nog-avond" : "besparen.dag-loopt-nog");

    /// <summary>Of de getoonde dag vandaag is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DagLooptNog))]
    private bool _vandaag = true;

    /// <summary>
    /// Wat de grafiek tekent: kilowatturen of euro's.
    ///
    /// Standaard kiest hij zelf: is er niets bespaard maar wel opgewekt, dan de opwek. Zodra de
    /// bewoner er zelf aan draait blijft zijn keuze staan; een scherm dat jouw keuze bij de
    /// volgende verversing terugzet is erger dan geen knop.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GrafiekKnopTekst))]
    private bool _grafiekInKwh;

    private bool _grafiekHandmatig;

    public string GrafiekKnopTekst => Taal.T(GrafiekInKwh ? "besparen.toon-euro" : "besparen.toon-kwh");

    private List<DailySummaryDto> _laatsteSamenvattingen = [];
    private DateOnly _laatsteVan;
    private DateOnly _laatsteTot;

    [RelayCommand]
    private void GrafiekWisselen()
    {
        _grafiekHandmatig = true;
        GrafiekInKwh = !GrafiekInKwh;
        Apply(_laatsteSamenvattingen, _laatsteVan, _laatsteTot);
        ChartVersion++;
    }

    /// <summary>
    /// Welke dagen je nu ziet, voluit.
    ///
    /// Boven de cijfers stond alleen "deze maand". Welke maand dat was moest je zelf bedenken, en
    /// bij een grafiek waar je doorheen bladert is dat precies de vraag die je hebt. Nu staat de
    /// periode er gewoon bij.
    /// </summary>
    [ObservableProperty] private string _periodeBereik = string.Empty;

    /// <summary>
    /// Hangt er al een apparaat? Zonder apparaat is elk totaal op dit scherm een leeg veld, en
    /// "€ 0,00 bespaard" met een vlakke grafiek eronder leest als een app die niet werkt.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToonCijfers))]
    [NotifyPropertyChangedFor(nameof(ToonGeenDagen))]
    private bool _geenApparaten;

    public bool ToonCijfers => !GeenApparaten;

    /// <summary>"Nog geen dagen" hoort alleen bij iemand die wel een apparaat heeft.</summary>
    public bool ToonGeenDagen => HasLoaded && !GeenApparaten;

    [ObservableProperty] private string _totalSavedText = Onbekend;
    /// <summary>
    /// Wat deze woning werkelijk meet; bepaalt of er een getal of "niet gemeten" staat.
    ///
    /// Alleen de netkant en de laadpaal staan hier, want dat zijn de twee waar de cijfers op dit
    /// scherm vanaf hangen. Zelf gebruikt hoort bij de netkant en niet bij het huisverbruik: de
    /// server rekent het als zon min teruglevering, dus zonder netmeting is het geen meting.
    /// </summary>
    [ObservableProperty] private bool _meetNet = true;
    [ObservableProperty] private bool _meetLaden = true;

    [ObservableProperty] private string _totalPvText = Onbekend;
    [ObservableProperty] private string _totalSelfUseText = Onbekend;
    [ObservableProperty] private string _totalExportText = Onbekend;
    [ObservableProperty] private string _totalChargedText = Onbekend;

    /// <summary>Wat er staat als we het niet weten; zelfde afspraak als op het beginscherm.</summary>
    private const string Onbekend = "–";
    [ObservableProperty] private bool _isRefreshing;

    // ---- Vergelijking met de vorige periode ----
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HeeftVergelijking))] private string? _vergelijking;

    public bool HeeftVergelijking => !string.IsNullOrEmpty(Vergelijking);

    /// <summary>
    /// Hoeveel je opwekte ten opzichte van dezelfde periode ervoor.
    ///
    /// "312 kWh" zegt niets; "312 kWh, een vijfde meer dan vorige maand" wel. Dit is het getal waar
    /// mensen in een energie-app naar zoeken en dat er nergens stond.
    /// </summary>
    private async Task VergelijkAsync(DateOnly van, DateOnly tot, double huidigeOpwek)
    {
        Vergelijking = null;

        var lengte = tot.DayNumber - van.DayNumber + 1;
        if (lengte < 1 || huidigeOpwek <= 0) return;

        var vorigeTot = van.AddDays(-1);
        var vorigeVan = vorigeTot.AddDays(-(lengte - 1));

        try
        {
            var vorige = await _api.GetDailySummariesAsync(vorigeVan, vorigeTot);
            var vorigeOpwek = vorige.Sum(d => d.PvKwh);
            if (vorigeOpwek <= 0) return;

            var verschil = (huidigeOpwek - vorigeOpwek) / vorigeOpwek * 100;

            // Onder de vijf procent is het weer, geen verandering. Dan liever niets zeggen dan een
            // getal dat iemand gaat verklaren.
            if (Math.Abs(verschil) < 5)
            {
                Vergelijking = string.Format(Taal.T("besparen.vergelijk-gelijk"), PeriodeWoord());
                return;
            }

            Vergelijking = string.Format(
                Taal.T(verschil > 0 ? "besparen.vergelijk-meer" : "besparen.vergelijk-minder"),
                Math.Abs(verschil).ToString("0", Nl),
                PeriodeWoord());
        }
        catch (ApiException)
        {
            Vergelijking = null;
        }
    }

    /// <summary>Het woord dat bij de gekozen periode hoort: dag, week, maand of jaar.</summary>
    private string PeriodeWoord() => Taal.T(Period switch
    {
        SavingsPeriod.Day => "besparen.vorige-dagen",
        SavingsPeriod.Week => "besparen.vorige-week",
        SavingsPeriod.Month => "besparen.vorige-maand",
        _ => "besparen.vorig-jaar",
    });

    // ---- Wat een batterij zou opleveren ----
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HeeftBatterijAdvies))] private string? _batterijRegel;
    [ObservableProperty] private string _batterijToelichting = string.Empty;

    public bool HeeftBatterijAdvies => !string.IsNullOrEmpty(BatterijRegel);

    /// <summary>De maanden die uit een overzicht van de leverancier zijn ingelezen.</summary>
    public ObservableCollection<LeverancierRij> LeverancierMaanden { get; } = [];

    [ObservableProperty][NotifyPropertyChangedFor(nameof(HeeftLeverancierCijfers))] private int _aantalLeverancierMaanden;

    public bool HeeftLeverancierCijfers => AantalLeverancierMaanden > 0;

    // ---- De optelsom onder die tabel ----
    //
    // Twaalf maandregels zijn twaalf losse getallen; wat iemand wil weten is wat hij in een jaar
    // verbruikt. En die onderste regel is meer dan een optelsom: afname min teruglevering is precies
    // het getal waarop de NEa het inboeken van laadstroom aftopt, dus wie zonnepanelen heeft ziet
    // hier waar zijn plafond ligt.
    //
    // Staan er minder dan twaalf maanden, dan noemen we het geen jaar. Een half jaar verdubbelen is
    // een gok, en een gok hoort hier niet.
    [ObservableProperty] private string _totaalKopTekst = string.Empty;
    [ObservableProperty] private string _totaalAfnameTekst = string.Empty;
    [ObservableProperty] private string _totaalTerugTekst = string.Empty;
    [ObservableProperty] private string _nettoAfnameTekst = string.Empty;

    // Terugleveren: wat kost het, wat scheelt zelf gebruiken
    [ObservableProperty] private bool _hasFeedIn;
    [ObservableProperty] private string _feedInMonthText = string.Empty;
    [ObservableProperty] private string _feedInYearText = string.Empty;
    [ObservableProperty] private string _feedIn2027Text = string.Empty;
    [ObservableProperty] private bool _feedInCostMissing;

    // Duurzaamheid dit jaar + prestatiecheck van de panelen
    [ObservableProperty] private bool _hasSustainability;
    [ObservableProperty] private string _co2Text = string.Empty;
    [ObservableProperty] private string _selfSufficiencyText = string.Empty;
    [ObservableProperty] private string _yearPvText = string.Empty;
    [ObservableProperty] private string _performanceText = string.Empty;
    [ObservableProperty] private bool _performanceLow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToonGeenDagen))]
    private bool _hasLoaded;

    /// <summary>Wordt verhoogd zodat de pagina de GraphicsView kan invalideren.</summary>
    [ObservableProperty] private int _chartVersion;

    /// <summary>
    /// Wat de grafiek je vertelt, in een zin.
    ///
    /// Hier stond niets: een rij staven met kale dagnummers eronder, en daar moest de bewoner zelf
    /// een conclusie uit trekken. De eerste gebruiker vroeg op 19-11-2026 letterlijk wat het scherm
    /// hem nu vertelde. Een getal zonder zin is geen antwoord.
    /// </summary>
    [ObservableProperty] private string _grafiekSamenvatting = string.Empty;

    /// <summary>
    /// De aangetikte staaf, voluit: "woensdag 12 november: 7,5 kWh".
    ///
    /// Boven een staaf past geen datum, dus kon je nergens aflezen hoeveel er op een dag was. Nu tik
    /// je hem aan en staat het eronder.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeeftStaafGekozen))]
    [NotifyPropertyChangedFor(nameof(ToonTikUitleg))]
    private string _staafGekozen = string.Empty;

    public bool HeeftStaafGekozen => !string.IsNullOrEmpty(StaafGekozen);

    /// <summary>De hint "tik een staaf aan" verdwijnt zodra iemand dat gedaan heeft.</summary>
    public bool ToonTikUitleg => !HeeftStaafGekozen;

    /// <summary>De dagen die bij de staven horen; nodig om een tik terug te vertalen naar een datum.</summary>
    private List<DateOnly> _staafDagen = [];

    /// <summary>Een staaf is aangetikt. Buiten de staven tikken wist de keuze weer.</summary>
    public void StaafAangetikt(int index)
    {
        if (index < 0 || index >= Chart.Bars.Count)
        {
            Chart.Gekozen = -1;
            StaafGekozen = string.Empty;
            ChartVersion++;
            return;
        }

        Chart.Gekozen = index;
        var staaf = Chart.Bars[index];
        StaafGekozen = string.Format(Taal.T("grafiek.staaf-gekozen"),
            staaf.Uitleg ?? staaf.Label, Chart.Waarde(staaf.Value));
        ChartVersion++;
    }

    public SavingsViewModel(ApiClient api, AuthService auth)
    {
        _api = api;
        _auth = auth;
        Title = Taal.T("besparen.besparing");
    }

    [RelayCommand]
    private async Task SetPeriodAsync(string period)
    {
        if (!Enum.TryParse<SavingsPeriod>(period, true, out var p) || p == Period) return;
        Period = p;
        await LoadAsync();
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        await RunSafeAsync(async () =>
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var (from, to) = Period switch
            {
                SavingsPeriod.Day => (today.AddDays(-13), today),             // 14 dagen, zodat de grafiek iets toont
                SavingsPeriod.Week => (StartOfWeek(today), today),
                SavingsPeriod.Month => (new DateOnly(today.Year, today.Month, 1), today),
                _ => (new DateOnly(today.Year, 1, 1), today)
            };

            // Hangt er iets? Zo niet, dan hoeven de totalen en de grafiek niet: die zouden allemaal
            // nul zijn, en dat is op dit scherm geen uitkomst maar een ontbrekende koppeling.
            try
            {
                var apparaten = await _api.GetDevicesAsync();
                GeenApparaten = apparaten.Count == 0;

                // Hangt er een omvormer die wij bij de fabrikant kunnen opvragen, dan wordt de dag
                // vanavond compleet gemaakt. Dat is iets anders dan een gateway die alleen vanaf het
                // thuisnetwerk te lezen is: daar meten we alleen terwijl de app openstaat.
                AvondAanvulling = apparaten.Any(d => d.Type == DeviceType.Inverter
                    && !(d.ExternalId ?? string.Empty).Contains("-lokaal-", StringComparison.OrdinalIgnoreCase));
            }
            catch (ApiException)
            {
                // Weten we het niet, dan tonen we de cijfers gewoon; die kloppen dan of ze zijn leeg.
                GeenApparaten = false;
            }

            // Wat meten we eigenlijk? Dat staat in de momentopname, die weet welke soorten apparaat
            // er hangen en of we ooit een waarde voor huis en net gezien hebben.
            try
            {
                var live = (await _api.GetDashboardAsync()).Live;
                MeetNet = live?.HasGridData ?? true;
                MeetLaden = live?.HasCharger ?? true;
            }
            catch (ApiException)
            {
                // Lukt dat niet, dan tonen we gewoon de getallen; liever een nul te veel dan een
                // scherm dat niets durft te zeggen.
                MeetNet = MeetLaden = true;
            }

            var summaries = (await _api.GetDailySummariesAsync(from, to))
                .OrderBy(s => SummaryRow.ToDate(s.Date))
                .ToList();

            PeriodeBereik = BereikTekst(from, to);
            Vandaag = to == DateOnly.FromDateTime(DateTime.Today);
            Apply(summaries, from, to);
            HasLoaded = true;

            await VergelijkAsync(from, to, summaries.Sum(d => d.PvKwh));

            if (!HasSustainability) try
            {
                var s = await _api.GetSustainabilityAsync();
                HasSustainability = s.PvKwh > 0;
                YearPvText = s.PvKwh.ToString("N0", Nl) + " kWh";
                Co2Text = s.Co2AvoidedKg >= 1000
                    ? (s.Co2AvoidedKg / 1000).ToString("N1", Nl) + " " + Taal.T("besparen.ton")
                    : s.Co2AvoidedKg.ToString("N0", Nl) + " " + Taal.T("besparen.kg");
                SelfSufficiencyText = s.SelfSufficiencyPercent is { } deel
                    ? deel + "%"
                    : Taal.T("besparen.niet-gemeten");
                PerformanceLow = s.Performance.Status == PerformanceStatus.Low;
                PerformanceText = s.Performance.Message;
            }
            catch (ApiException ex) when (!ex.IsUnauthorized)
            {
                HasSustainability = false;
            }

            try
            {
                ApplyFeedIn(await _api.GetFeedInAsync());
            }
            catch (ApiException ex) when (!ex.IsUnauthorized)
            {
                HasFeedIn = false; // los blok: de rest van het scherm blijft werken
            }

            // Wat een batterij zou doen, gerekend met de eigen dagen van deze woning. Alleen voor
            // wie er nog geen heeft, en alleen als er genoeg gemeten dagen zijn.
            try
            {
                var advies = await _api.BatterijAdviesAsync();
                if (!advies.HeeftAlBatterij && advies.Maten.Count > 0)
                {
                    var beste = advies.Maten
                        .OrderByDescending(m => m.PerJaarNaSalderingEur)
                        .First();

                    BatterijRegel = string.Format(
                        Taal.T("batterij.regel"),
                        beste.Kwh.ToString("0.#", Nl),
                        beste.PerJaarNaSalderingEur.ToString("C0", Nl));

                    BatterijToelichting = string.Format(
                        Taal.T("batterij.toelichting"),
                        beste.VerplaatstPerDagKwh.ToString("0.#", Nl),
                        advies.Dagen);
                }
                else
                {
                    BatterijRegel = null;
                }
            }
            catch (ApiException ex) when (!ex.IsUnauthorized)
            {
                BatterijRegel = null;
            }

            // De maanden uit het overzicht van je leverancier. Voor wie geen P1-meter heeft is dit
            // de enige plek waar de netkant staat, en voor wie er wel een heeft is het de plek waar
            // je ziet of de twee op elkaar lijken.
            try
            {
                var maanden = await _api.VerbruikVanLeverancierAsync();
                LeverancierMaanden.Clear();
                foreach (var maand in maanden.TakeLast(12))
                {
                    LeverancierMaanden.Add(new LeverancierRij(maand));
                }

                AantalLeverancierMaanden = LeverancierMaanden.Count;
                BerekenTotaal();
            }
            catch (ApiException ex) when (!ex.IsUnauthorized)
            {
                AantalLeverancierMaanden = 0;
            }
        }, _auth);
        IsRefreshing = false;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        await LoadAsync();
    }

    private void Apply(List<DailySummaryDto> summaries, DateOnly from, DateOnly to)
    {
        _laatsteSamenvattingen = summaries;
        _laatsteVan = from;
        _laatsteTot = to;

        // Totalen: bij "Dag" alleen vandaag, anders de hele periode.
        var scope = Period == SavingsPeriod.Day
            ? summaries.Where(s => SummaryRow.ToDate(s.Date) == to).ToList()
            : summaries;

        // Geen enkele dag waar de totalen over gaan. Dan is de som nul, maar die nul is niemands
        // uitkomst: er is nog niets gemeten. Alleen de totalen; de grafiek en de lijst hieronder
        // kijken naar de hele periode en hebben soms wel dagen terwijl vandaag er nog geen is.
        var leeg = scope.Count == 0;
        var nietGemeten = Taal.T("besparen.niet-gemeten");

        TotalSavedText = leeg ? Onbekend : Sum(scope, s => s.SavedEur).ToString("C2", Nl);
        TotalPvText = leeg ? Onbekend : KwhText(Sum(scope, s => s.PvKwh));

        // Zonder netmeting weten we niet wat er naar het net ging, en dus ook niet wat er zelf
        // gebruikt is: de server rekent zelfverbruik als zon min teruglevering. Zonder meting is
        // die teruglevering nul, en dan zou hier staan dat alles zelf gebruikt is. Dat is geen nul
        // te veel maar een verzonnen honderd procent. Hetzelfde geldt voor laden zonder laadpaal.
        // Staat er wél iets in de dagen, dan is het gemeten, wat de momentopname ook zegt. Die
        // momentopname gaat over nu en kan er even naast zitten (een koppeling die net is gezet, een
        // server die herstart); de dagtotalen liegen niet. Zonder dit stond er "niet gemeten" boven
        // een week waarin gewoon 59 kWh was geladen.
        var zelfKwh = Sum(scope, s => s.SelfUseKwh);
        var terugKwh = Sum(scope, s => s.ExportKwh);
        var geladenKwh = Sum(scope, s => s.ChargedKwh);
        var meetNet = MeetNet || zelfKwh > 0 || terugKwh > 0;
        var meetLaden = MeetLaden || geladenKwh > 0;

        TotalSelfUseText = !meetNet ? nietGemeten : leeg ? Onbekend : KwhText(zelfKwh);
        TotalExportText = !meetNet ? nietGemeten : leeg ? Onbekend : KwhText(terugKwh);
        TotalChargedText = !meetLaden ? nietGemeten : leeg ? Onbekend : KwhText(geladenKwh);

        // Lijst: nieuwste eerst
        Rows.Clear();
        foreach (var s in summaries.OrderByDescending(s => SummaryRow.ToDate(s.Date)))
            Rows.Add(new SummaryRow(s, MeetNet));

        // Grafiek: euro's of kilowatturen.
        //
        // Hij tekende altijd de besparing, en die is leeg voor wie zijn oude cijfers heeft ingelezen:
        // zo'n dag heeft alleen opwek, want wat iemand toen zelf gebruikte weten we niet. Dan staat
        // er 106 kWh opgewekt boven een lege grafiek, en dat leest als een app die niets doet.
        //
        // Dus: is er in deze periode niets bespaard maar wel opgewekt, dan tekenen we de opwek. En
        // de bewoner kan zelf wisselen, want soms wil je juist het geld zien.
        var besparingTotaal = (double)Sum(scope, s => s.SavedEur);
        var opwekTotaal = Sum(scope, s => s.PvKwh);
        if (!_grafiekHandmatig) GrafiekInKwh = besparingTotaal <= 0 && opwekTotaal > 0;

        // De staven, met labels waaraan je ziet dat het dagen zijn. Kale getallen (1, 4, 7, 10, 13)
        // zeiden niemand iets; zie GrafiekTekst voor wat er nu onder een staaf hoort te staan.
        var periode = NaarGrafiekPeriode(Period);
        double Waarde(DailySummaryDto s) =>
            GrafiekInKwh ? Convert.ToDouble(s.PvKwh) : Convert.ToDouble(s.SavedEur);

        if (Period == SavingsPeriod.Year)
        {
            var perMaand = summaries
                .GroupBy(s => SummaryRow.ToDate(s.Date).Month)
                .OrderBy(g => g.Key)
                .ToList();

            _staafDagen = perMaand.Select(g => new DateOnly(to.Year, g.Key, 1)).ToList();
            Chart.Bars = perMaand
                .Select((g, i) => new ChartBar(
                    GrafiekTekst.StaafLabel(_staafDagen[i], periode, false, Taal.Code),
                    GrafiekInKwh ? Convert.ToDouble(Sum(g, s => s.PvKwh)) : Convert.ToDouble(Sum(g, s => s.SavedEur)),
                    GrafiekTekst.VolleDatum(_staafDagen[i], periode, Taal.Code)))
                .ToList();
        }
        else
        {
            _staafDagen = summaries.Select(s => SummaryRow.ToDate(s.Date)).ToList();
            Chart.Bars = summaries
                .Select((s, i) => new ChartBar(
                    GrafiekTekst.StaafLabel(_staafDagen[i], periode,
                        i == 0 || i == summaries.Count - 1, Taal.Code),
                    Waarde(s),
                    GrafiekTekst.VolleDatum(_staafDagen[i], periode, Taal.Code)))
                .ToList();
        }

        // Een nieuwe periode betekent een nieuwe grafiek; een keuze uit de vorige slaat nergens meer op.
        Chart.Gekozen = -1;
        StaafGekozen = string.Empty;

        GrafiekSamenvatting = GrafiekTekst.Samenvatting(
            periode,
            summaries.Select(s => new GrafiekDag(SummaryRow.ToDate(s.Date), Waarde(s))).ToList(),
            from, to, GrafiekInKwh, Taal.Code);

        Chart.ValuePrefix = GrafiekInKwh ? string.Empty : "€ ";
        Chart.ValueSuffix = GrafiekInKwh ? " kWh" : string.Empty;

        ApplyChartTheme();
        ChartVersion++;
    }

    /// <summary>De periode zoals de tekstlaag hem kent. Twee opsommingen, want de app kiest ook
    /// "dag" terwijl de grafiek dan veertien dagen toont; de woorden volgen de knop, niet de reeks.</summary>
    private static GrafiekPeriode NaarGrafiekPeriode(SavingsPeriod p) => p switch
    {
        SavingsPeriod.Day => GrafiekPeriode.Dag,
        SavingsPeriod.Week => GrafiekPeriode.Week,
        SavingsPeriod.Month => GrafiekPeriode.Maand,
        _ => GrafiekPeriode.Jaar,
    };

    private void ApplyFeedIn(FeedInDto f)
    {
        HasFeedIn = f.YearExportKwh > 0 || f.MonthExportKwh > 0;
        FeedInCostMissing = f.FeedInCostPerKwh <= 0;
        FeedInMonthText = FeedInCostMissing
            ? string.Format(Taal.T("besparen.deze-maand-teruggeleverd"), f.MonthExportKwh.ToString("N0", Nl))
            : string.Format(Taal.T("besparen.terugleverkosten-deze-maand"), f.MonthCostEur.ToString("C2", Nl));
        FeedInYearText = FeedInCostMissing
            ? Taal.T("besparen.vul-terugleverkosten-in")
            : string.Format(Taal.T("besparen.per-jaar-over-kwh"), f.YearCostEur.ToString("C0", Nl),
                f.YearExportKwh.ToString("N0", Nl),
                f.YearIsEstimate ? " " + Taal.T("besparen.schatting") : string.Empty);
        FeedIn2027Text = f.SelfUseWorth2027PerKwh > 0
            ? string.Format(Taal.T("besparen.vanaf-2027"), (f.SelfUseWorth2027PerKwh * 100).ToString("0", Nl),
                f.YearValue2027Eur.ToString("C0", Nl))
            : string.Empty;
    }

    private void ApplyChartTheme()
    {
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        Chart.BarColor = Res("Accent500");
        Chart.BarColorLight = Res("Accent300");
        Chart.AxisColor = Res(dark ? "BorderDark" : "Border");
        Chart.TextColor = Res(dark ? "TextMutedDark" : "TextMuted");
    }

    private static Color Res(string key) => ThemeColors.Get(key);

    private static decimal Sum(IEnumerable<DailySummaryDto> items, Func<DailySummaryDto, object?> selector) =>
        items.Sum(s => Convert.ToDecimal(selector(s) ?? 0m));

    private static string KwhText(decimal kwh) => kwh.ToString(kwh >= 100 ? "N0" : "N1", Nl) + " kWh";

    private static DateOnly StartOfWeek(DateOnly date)
    {
        int diff = ((int)date.DayOfWeek + 6) % 7; // maandag = 0
        return date.AddDays(-diff);
    }

    /// <summary>De regel onder de maandtabel: wat er in die periode van het net kwam en wat erheen ging.</summary>
    private void BerekenTotaal()
    {
        if (LeverancierMaanden.Count == 0)
        {
            TotaalKopTekst = TotaalAfnameTekst = TotaalTerugTekst = NettoAfnameTekst = string.Empty;
            return;
        }

        var maanden = LeverancierMaanden.Count;
        var afname = LeverancierMaanden.Sum(r => r.Maand.AfnameKwh);
        var terug = LeverancierMaanden.Sum(r => r.Maand.TerugleveringKwh ?? 0);
        var heeftTerug = LeverancierMaanden.Any(r => r.Maand.TerugleveringKwh is not null);

        TotaalKopTekst = maanden >= 12
            ? Taal.T("verbruik.totaal-jaar")
            : string.Format(Taal.T("verbruik.totaal-maanden"), maanden);
        TotaalAfnameTekst = afname.ToString("N0", Nl) + " kWh";
        TotaalTerugTekst = heeftTerug ? terug.ToString("N0", Nl) + " kWh" : "–";

        // Zonder teruglevering is netto hetzelfde als afname, en dan is die regel alleen maar ruis.
        NettoAfnameTekst = heeftTerug
            ? string.Format(Taal.T("verbruik.netto"), (afname - terug).ToString("N0", Nl))
            : string.Empty;
    }

    public bool HeeftNetto => !string.IsNullOrEmpty(NettoAfnameTekst);

    partial void OnNettoAfnameTekstChanged(string value) => OnPropertyChanged(nameof(HeeftNetto));
}
