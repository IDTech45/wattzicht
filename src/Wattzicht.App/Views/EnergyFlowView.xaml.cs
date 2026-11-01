using Wattzicht.App.Services;
using Wattzicht.Shared;

namespace Wattzicht.App.Views;

/// <summary>
/// "Energiestroom nu": vier kaarten (Zon, Huis, Batterij, Net) en de laadpaal.
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

    public EnergyFlowView()
    {
        InitializeComponent();
        Recalculate();
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
        PvHint = pv > 50 ? "▲ Opwek" : "Geen opwek";

        LoadKwText = Kw(load);
        LoadHint = "▼ Verbruik";

        BatteryKwText = Kw(battery);
        // Conventie (Wattzicht.Shared): positief = ontladen, negatief = laden.
        BatteryHint = battery > 50 ? "▲ Ontladen" : battery < -50 ? "▼ Laden" : "Rust";
        SocText = soc is null ? "–" : $"{soc:0}%";
        SocFraction = soc is null ? 0 : Math.Clamp(soc.Value / 100.0, 0, 1);

        GridKwText = Kw(grid);
        bool exporting = grid < -50;
        bool importing = grid > 50;
        GridHint = exporting ? "▲ Teruglevering" : importing ? "▼ Afname" : "Geen uitwisseling";
        GridAccent = exporting ? Res("EnergyGrid") : importing ? Res("EnergyGrid") : Res("TextMuted");
        GridCardBackground = exporting
            ? ThemeRes("EnergyGridSoft", "EnergyGridSoftDark")
            : importing
                ? ThemeRes("EnergyChargerSoft", "EnergyChargerSoftDark")
                : ThemeRes("Surface", "SurfaceDark");

        HasCharger = s?.ChargerPowerW is not null;
        ChargerKwText = Kw(charger);
        ChargerHint = charger > 50 ? "▼ Auto laadt" : "Niet aan het laden";
    }

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
