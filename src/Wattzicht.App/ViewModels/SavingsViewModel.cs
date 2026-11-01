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

    public DailySummaryDto Summary { get; }
    public SummaryRow(DailySummaryDto summary) => Summary = summary;

    public string DateText
    {
        get
        {
            var date = ToDate(Summary.Date);
            if (date == DateOnly.FromDateTime(DateTime.Today)) return "Vandaag";
            if (date == DateOnly.FromDateTime(DateTime.Today.AddDays(-1))) return "Gisteren";
            return date.ToString("ddd d MMM", Nl);
        }
    }

    public string PvText => Kwh(Summary.PvKwh);
    public string SelfUseText => Kwh(Summary.SelfUseKwh);
    public string ExportText => Kwh(Summary.ExportKwh);
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

    public string PeriodTitle => Period switch
    {
        SavingsPeriod.Day => "Vandaag bespaard",
        SavingsPeriod.Week => "Deze week bespaard",
        SavingsPeriod.Month => "Deze maand bespaard",
        _ => "Dit jaar bespaard"
    };

    [ObservableProperty] private string _totalSavedText = "€ 0,00";
    [ObservableProperty] private string _totalPvText = "0 kWh";
    [ObservableProperty] private string _totalSelfUseText = "0 kWh";
    [ObservableProperty] private string _totalExportText = "0 kWh";
    [ObservableProperty] private string _totalChargedText = "0 kWh";
    [ObservableProperty] private bool _isRefreshing;

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
    [ObservableProperty] private bool _hasLoaded;

    /// <summary>Wordt verhoogd zodat de pagina de GraphicsView kan invalideren.</summary>
    [ObservableProperty] private int _chartVersion;

    public SavingsViewModel(ApiClient api, AuthService auth)
    {
        _api = api;
        _auth = auth;
        Title = "Besparing";
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

            var summaries = (await _api.GetDailySummariesAsync(from, to))
                .OrderBy(s => SummaryRow.ToDate(s.Date))
                .ToList();

            Apply(summaries, from, to);
            HasLoaded = true;

            if (!HasSustainability) try
            {
                var s = await _api.GetSustainabilityAsync();
                HasSustainability = s.PvKwh > 0;
                YearPvText = s.PvKwh.ToString("N0", Nl) + " kWh";
                Co2Text = s.Co2AvoidedKg >= 1000
                    ? (s.Co2AvoidedKg / 1000).ToString("N1", Nl) + " ton"
                    : s.Co2AvoidedKg.ToString("N0", Nl) + " kg";
                SelfSufficiencyText = s.SelfSufficiencyPercent + "%";
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
        // Totalen: bij "Dag" alleen vandaag, anders de hele periode.
        var scope = Period == SavingsPeriod.Day
            ? summaries.Where(s => SummaryRow.ToDate(s.Date) == to).ToList()
            : summaries;

        TotalSavedText = Sum(scope, s => s.SavedEur).ToString("C2", Nl);
        TotalPvText = KwhText(Sum(scope, s => s.PvKwh));
        TotalSelfUseText = KwhText(Sum(scope, s => s.SelfUseKwh));
        TotalExportText = KwhText(Sum(scope, s => s.ExportKwh));
        TotalChargedText = KwhText(Sum(scope, s => s.ChargedKwh));

        // Lijst: nieuwste eerst
        Rows.Clear();
        foreach (var s in summaries.OrderByDescending(s => SummaryRow.ToDate(s.Date)))
            Rows.Add(new SummaryRow(s));

        // Grafiek
        Chart.Bars = Period == SavingsPeriod.Year
            ? summaries
                .GroupBy(s => SummaryRow.ToDate(s.Date).Month)
                .OrderBy(g => g.Key)
                .Select(g => new ChartBar(
                    CultureInfo.GetCultureInfo("nl-NL").DateTimeFormat.GetAbbreviatedMonthName(g.Key).TrimEnd('.'),
                    (double)Sum(g, s => s.SavedEur)))
                .ToList()
            : summaries
                .Select(s => new ChartBar(SummaryRow.ToDate(s.Date).Day.ToString(), Convert.ToDouble(s.SavedEur)))
                .ToList();

        ApplyChartTheme();
        ChartVersion++;
    }

    private void ApplyFeedIn(FeedInDto f)
    {
        HasFeedIn = f.YearExportKwh > 0 || f.MonthExportKwh > 0;
        FeedInCostMissing = f.FeedInCostPerKwh <= 0;
        FeedInMonthText = FeedInCostMissing
            ? $"Deze maand {f.MonthExportKwh.ToString("N0", Nl)} kWh teruggeleverd"
            : $"Terugleverkosten deze maand: {f.MonthCostEur.ToString("C2", Nl)}";
        FeedInYearText = FeedInCostMissing
            ? "Vul je terugleverkosten in bij Instellingen om te zien wat terugleveren je kost."
            : $"Per jaar ± {f.YearCostEur.ToString("C0", Nl)} over {f.YearExportKwh.ToString("N0", Nl)} kWh{(f.YearIsEstimate ? " (schatting)" : string.Empty)}";
        FeedIn2027Text = f.SelfUseWorth2027PerKwh > 0
            ? $"Vanaf 2027 scheelt elke kWh die je zelf gebruikt {(f.SelfUseWorth2027PerKwh * 100).ToString("0", Nl)} cent. Bij je huidige teruglevering is dat ± {f.YearValue2027Eur.ToString("C0", Nl)} per jaar."
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
}
