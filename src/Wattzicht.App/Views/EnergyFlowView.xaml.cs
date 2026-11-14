using System.Text;
using Wattzicht.App.Drawables;
using Wattzicht.App.Services;
using Wattzicht.Shared;

namespace Wattzicht.App.Views;

/// <summary>
/// "Energiestroom nu": de tekening met zon, huis, net, batterij en laadpaal, en daaronder de kaarten.
/// Richting van de stroom wordt getoond met ▲ (eruit/teruglevering/ontladen) en ▼ (erin/afname/laden).
/// Tekenconventie: GridPowerW &gt; 0 = afname van het net, &lt; 0 = teruglevering;
/// BatteryPowerW &gt; 0 = ontladen, &lt; 0 = laden (conventie uit Wattzicht.Shared).
/// </summary>
public partial class EnergyFlowView : ContentView
{
    public static readonly BindableProperty SnapshotProperty = BindableProperty.Create(
        nameof(Snapshot), typeof(LiveSnapshotDto), typeof(EnergyFlowView), null,
        propertyChanged: (b, _, _) => ((EnergyFlowView)b).Recalculate());

    public LiveSnapshotDto? Snapshot
    {
        get => (LiveSnapshotDto?)GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    /// <summary>
    /// Wat de panelen vandaag al gemaakt hebben.
    ///
    /// Zonder dit staat er 's avonds vijf keer 0,0 kW op het beginscherm en lijkt de app dood,
    /// terwijl het de hele dag mooi weer was. Het vermogen van nu is maar het halve verhaal.
    /// </summary>
    public static readonly BindableProperty PvTodayKwhProperty = BindableProperty.Create(
        nameof(PvTodayKwh), typeof(double), typeof(EnergyFlowView), 0d,
        propertyChanged: (b, _, _) => ((EnergyFlowView)b).Recalculate());

    public double PvTodayKwh
    {
        get => (double)GetValue(PvTodayKwhProperty);
        set => SetValue(PvTodayKwhProperty, value);
    }

    /// <summary>Het piekvermogen van de panelen; staat bij het kopje Zon zodra het bekend is.</summary>
    public static readonly BindableProperty PvPeakKwProperty = BindableProperty.Create(
        nameof(PvPeakKw), typeof(double), typeof(EnergyFlowView), 0d,
        propertyChanged: (b, _, _) => ((EnergyFlowView)b).Recalculate());

    public double PvPeakKw
    {
        get => (double)GetValue(PvPeakKwProperty);
        set => SetValue(PvPeakKwProperty, value);
    }

    // Afgeleide, read-only bindable properties voor de XAML
    public static readonly BindableProperty PvKwTextProperty = BindableProperty.Create(nameof(PvKwText), typeof(string), typeof(EnergyFlowView), "0,0 kW");
    public static readonly BindableProperty PvHintProperty = BindableProperty.Create(nameof(PvHint), typeof(string), typeof(EnergyFlowView), "Geen opwek");
    public static readonly BindableProperty LoadKwTextProperty = BindableProperty.Create(nameof(LoadKwText), typeof(string), typeof(EnergyFlowView), "0,0 kW");
    public static readonly BindableProperty LoadHintProperty = BindableProperty.Create(nameof(LoadHint), typeof(string), typeof(EnergyFlowView), "Verbruik");
    public static readonly BindableProperty BatteryKwTextProperty = BindableProperty.Create(nameof(BatteryKwText), typeof(string), typeof(EnergyFlowView), "0,0 kW");
    public static readonly BindableProperty BatteryHintProperty = BindableProperty.Create(nameof(BatteryHint), typeof(string), typeof(EnergyFlowView), "Rust");
    public static readonly BindableProperty SocTextProperty = BindableProperty.Create(nameof(SocText), typeof(string), typeof(EnergyFlowView), "–");
    public static readonly BindableProperty SocFractionProperty = BindableProperty.Create(nameof(SocFraction), typeof(double), typeof(EnergyFlowView), 0d);
    public static readonly BindableProperty GridKwTextProperty = BindableProperty.Create(nameof(GridKwText), typeof(string), typeof(EnergyFlowView), "0,0 kW");
    public static readonly BindableProperty GridHintProperty = BindableProperty.Create(nameof(GridHint), typeof(string), typeof(EnergyFlowView), "Geen uitwisseling");
    public static readonly BindableProperty GridAccentProperty = BindableProperty.Create(nameof(GridAccent), typeof(Color), typeof(EnergyFlowView), Colors.Gray);
    public static readonly BindableProperty GridCardBackgroundProperty = BindableProperty.Create(nameof(GridCardBackground), typeof(Color), typeof(EnergyFlowView), Colors.Transparent);
    public static readonly BindableProperty ChargerKwTextProperty = BindableProperty.Create(nameof(ChargerKwText), typeof(string), typeof(EnergyFlowView), "0,0 kW");
    public static readonly BindableProperty ChargerHintProperty = BindableProperty.Create(nameof(ChargerHint), typeof(string), typeof(EnergyFlowView), "Niet aan het laden");
    public static readonly BindableProperty HasChargerProperty = BindableProperty.Create(nameof(HasCharger), typeof(bool), typeof(EnergyFlowView), false);
    public static readonly BindableProperty HasBatteryProperty = BindableProperty.Create(nameof(HasBattery), typeof(bool), typeof(EnergyFlowView), false);
    public static readonly BindableProperty PvTitleProperty = BindableProperty.Create(nameof(PvTitle), typeof(string), typeof(EnergyFlowView), "Zon");
    public static readonly BindableProperty NetColumnProperty = BindableProperty.Create(nameof(NetColumn), typeof(int), typeof(EnergyFlowView), 1);
    public static readonly BindableProperty NetColumnSpanProperty = BindableProperty.Create(nameof(NetColumnSpan), typeof(int), typeof(EnergyFlowView), 1);

    public string PvKwText { get => (string)GetValue(PvKwTextProperty); private set => SetValue(PvKwTextProperty, value); }
    public string PvHint { get => (string)GetValue(PvHintProperty); private set => SetValue(PvHintProperty, value); }
    public string LoadKwText { get => (string)GetValue(LoadKwTextProperty); private set => SetValue(LoadKwTextProperty, value); }
    public string LoadHint { get => (string)GetValue(LoadHintProperty); private set => SetValue(LoadHintProperty, value); }
    public string BatteryKwText { get => (string)GetValue(BatteryKwTextProperty); private set => SetValue(BatteryKwTextProperty, value); }
    public string BatteryHint { get => (string)GetValue(BatteryHintProperty); private set => SetValue(BatteryHintProperty, value); }
    public string SocText { get => (string)GetValue(SocTextProperty); private set => SetValue(SocTextProperty, value); }
    public double SocFraction { get => (double)GetValue(SocFractionProperty); private set => SetValue(SocFractionProperty, value); }
    public string GridKwText { get => (string)GetValue(GridKwTextProperty); private set => SetValue(GridKwTextProperty, value); }
    public string GridHint { get => (string)GetValue(GridHintProperty); private set => SetValue(GridHintProperty, value); }
    public Color GridAccent { get => (Color)GetValue(GridAccentProperty); private set => SetValue(GridAccentProperty, value); }
    public Color GridCardBackground { get => (Color)GetValue(GridCardBackgroundProperty); private set => SetValue(GridCardBackgroundProperty, value); }
    public string ChargerKwText { get => (string)GetValue(ChargerKwTextProperty); private set => SetValue(ChargerKwTextProperty, value); }
    public string ChargerHint { get => (string)GetValue(ChargerHintProperty); private set => SetValue(ChargerHintProperty, value); }
    public bool HasCharger { get => (bool)GetValue(HasChargerProperty); private set => SetValue(HasChargerProperty, value); }
    public bool HasBattery { get => (bool)GetValue(HasBatteryProperty); private set => SetValue(HasBatteryProperty, value); }
    public string PvTitle { get => (string)GetValue(PvTitleProperty); private set => SetValue(PvTitleProperty, value); }

    /// <summary>Zonder batterij schuift het net naar links en vult het de hele rij; anders blijft er een gat staan.</summary>
    public int NetColumn { get => (int)GetValue(NetColumnProperty); private set => SetValue(NetColumnProperty, value); }
    public int NetColumnSpan { get => (int)GetValue(NetColumnSpanProperty); private set => SetValue(NetColumnSpanProperty, value); }

    /// <summary>De tekening zelf; zie <see cref="EnergyFlowDrawable"/>.</summary>
    public EnergyFlowDrawable Tekening { get; } = new();

    public static readonly BindableProperty TekeningHoogteProperty = BindableProperty.Create(
        // De beginwaarde moet echt een double zijn: MAUI vergelijkt het type van de standaardwaarde
        // met het opgegeven type en gooit anders bij het laden van de klasse. RijHoogte is een float.
        nameof(TekeningHoogte), typeof(double), typeof(EnergyFlowView), (double)(EnergyFlowDrawable.RijHoogte * 2));

    /// <summary>Hoogte van de tekening: twee rijen, of drie als er een batterij of laadpaal is.</summary>
    public double TekeningHoogte
    {
        get => (double)GetValue(TekeningHoogteProperty);
        private set => SetValue(TekeningHoogteProperty, value);
    }

    public static readonly BindableProperty TekeningBeschrijvingProperty = BindableProperty.Create(
        nameof(TekeningBeschrijving), typeof(string), typeof(EnergyFlowView), string.Empty);

    /// <summary>
    /// Wat de tekening zegt, in woorden. Een canvas is voor een schermlezer een leeg vlak, dus staat
    /// hier dezelfde inhoud als tekst.
    /// </summary>
    public string TekeningBeschrijving
    {
        get => (string)GetValue(TekeningBeschrijvingProperty);
        private set => SetValue(TekeningBeschrijvingProperty, value);
    }

    public EnergyFlowView()
    {
        InitializeComponent();
        Recalculate();

        // De tekening haalt zijn kleuren uit Colors.xaml op het moment van tekenen. Wisselt het
        // thema terwijl de app open staat, dan moet hij opnieuw: anders blijven de lijnen van het
        // lichte thema op een donkere achtergrond staan.
        if (Application.Current is { } app)
        {
            app.RequestedThemeChanged += (_, _) => Stroom?.Invalidate();
        }
    }

    private void Recalculate()
    {
        var s = Snapshot;
        double pv = ToDouble(s?.PvPowerW);
        double load = ToDouble(s?.LoadPowerW);
        double grid = ToDouble(s?.GridPowerW);
        double battery = ToDouble(s?.BatteryPowerW);
        double charger = ToDouble(s?.ChargerPowerW);
        double? soc = s?.BatterySoc is null ? null : ToDouble(s.BatterySoc);

        PvKwText = Kw(pv);

        // De tegel zegt nu drie dingen in plaats van een: hoeveel er op dit moment binnenkomt, of
        // dat oploopt, en wat er vandaag al in zit. Dat laatste is het getal waar iemand 's avonds
        // naar zoekt, en dat stond alleen onderaan de pagina.
        PvHint = PvTodayKwh > 0
            ? string.Format(Taal.T("stroom.vandaag"), Getal(PvTodayKwh))
            : pv > 50 ? Taal.T("stroom.opwek") : Taal.T("stroom.geen-opwek");

        PvTitle = PvPeakKw > 0
            ? $"{Taal.T("stroom.zon")} · {Getal(PvPeakKw)} kWp"
            : Taal.T("stroom.zon");

        LoadKwText = Kw(load);
        LoadHint = Taal.T("stroom.verbruik");

        BatteryKwText = Kw(battery);
        // Conventie (Wattzicht.Shared): positief = ontladen, negatief = laden.
        BatteryHint = battery > 50 ? Taal.T("stroom.ontladen")
            : battery < -50 ? Taal.T("stroom.laden")
            : Taal.T("stroom.rust");
        SocText = soc is null ? "–" : $"{soc:0}%";
        SocFraction = soc is null ? 0 : Math.Clamp(soc.Value / 100.0, 0, 1);

        GridKwText = Kw(grid);
        bool exporting = grid < -50;
        bool importing = grid > 50;
        GridHint = exporting ? Taal.T("stroom.teruglevering")
            : importing ? Taal.T("stroom.afname")
            : Taal.T("stroom.geen-uitwisseling");
        // Bij uitwisseling de netkleur; staat er niets op het net, dan de gewone tekstkleur. Die moet
        // wel met het thema meegaan: TextMuted is voor een lichte achtergrond en haalt op donker 2,2:1.
        GridAccent = exporting || importing
            ? Res("EnergyGrid")
            : ThemeRes("TextMuted", "TextMutedDark");
        GridCardBackground = exporting
            ? ThemeRes("EnergyGridSoft", "EnergyGridSoftDark")
            : importing
                ? ThemeRes("EnergyChargerSoft", "EnergyChargerSoftDark")
                : ThemeRes("Surface", "SurfaceDark");

        // Wat de woning werkelijk heeft, niet wat er toevallig nul watt staat te doen. Een optelling
        // van nul is geen bewijs van bezit, en zo stond er bij een woning met alleen panelen toch
        // een batterij "in rust" en een laadpaal "niet aan het laden".
        HasBattery = s?.HasBattery ?? false;
        HasCharger = s?.HasCharger ?? false;

        NetColumn = HasBattery ? 1 : 0;
        NetColumnSpan = HasBattery ? 1 : 2;

        ChargerKwText = Kw(charger);
        ChargerHint = charger > 50 ? "▼ " + Taal.T("stroom.auto-laadt") : Taal.T("stroom.niet-aan-het-laden");

        // Meten we het huisverbruik of de netkant niet, dan hoort er geen 0,0 kW te staan. Dat
        // leest als "er gebeurt niets" en het betekent "wij weten het niet". De kaart die uitlegt
        // hoe je dat oplost staat op het beginscherm, niet hier: deze tegels zijn de meting.
        if (s is { HasLoadData: false })
        {
            LoadKwText = Taal.T("stroom.niet-gemeten");
            LoadHint = string.Empty;
        }

        if (s is { HasGridData: false })
        {
            GridKwText = Taal.T("stroom.niet-gemeten");
            GridHint = string.Empty;
            GridAccent = ThemeRes("TextMuted", "TextMutedDark");
            GridCardBackground = ThemeRes("Surface", "SurfaceDark");
        }

        Tekening.Snapshot = s;
        TekeningHoogte = EnergyFlowDrawable.HoogteVoor(s);
        TekeningBeschrijving = Beschrijf();
        Stroom?.Invalidate();
    }

    /// <summary>Dezelfde inhoud als de tekening, maar als zin voor een schermlezer.</summary>
    private string Beschrijf()
    {
        var zin = new StringBuilder()
            .Append(Taal.T("stroom.zon")).Append(' ').Append(PvKwText)
            .Append(", ").Append(Taal.T("stroom.huis")).Append(' ').Append(LoadKwText)
            .Append(", ").Append(Taal.T("stroom.net")).Append(' ').Append(GridKwText);

        if (HasBattery)
        {
            zin.Append(", ").Append(Taal.T("stroom.batterij")).Append(' ').Append(BatteryKwText);
        }

        if (HasCharger)
        {
            zin.Append(", ").Append(Taal.T("stroom.laadpaal")).Append(' ').Append(ChargerKwText);
        }

        return zin.ToString();
    }

    private static string Getal(double waarde) => waarde.ToString("0.#").Replace('.', ',');

    private static string Kw(double watts) =>
        $"{Math.Abs(watts) / 1000.0:0.0} kW".Replace('.', ',');

    private static double ToDouble(object? value) => value switch
    {
        null => 0,
        int i => i,
        long l => l,
        double d => d,
        float f => f,
        decimal m => (double)m,
        _ => 0
    };

    private static Color Res(string key) => ThemeColors.Get(key);

    private static Color ThemeRes(string lightKey, string darkKey) => ThemeColors.Themed(lightKey, darkKey);
}
