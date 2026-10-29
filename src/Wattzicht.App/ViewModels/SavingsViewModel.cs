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

    private void ApplyChartTheme()
    {
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        Chart.BarColor = Res("Sun");
        Chart.BarColorLight = Res("SunLight");
        Chart.AxisColor = Res(dark ? "LineDark" : "Line");
        Chart.TextColor = Res(dark ? "MutedDark" : "Muted");
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
