using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wattzicht.App.Services;
using Wattzicht.Shared;

namespace Wattzicht.App.ViewModels;

/// <summary>Eén tip op het Sturen-scherm.</summary>
public sealed class AdviceTipRow
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    public AdviceTipRow(AdviceTipDto tip) => Tip = tip;

    public AdviceTipDto Tip { get; }
    public string Title => Tip.Title;
    public string Detail => Tip.Detail;
    public string? Note => Tip.Note;
    public bool HasNote => !string.IsNullOrWhiteSpace(Tip.Note);
    public bool HasSaving => Tip.SavingEur > 0;
    public string SavingText => "€ " + Tip.SavingEur.ToString("0.00", Nl);

    /// <summary>Vaste kleur per stroom (zie huisstijl): laadpaal, batterij, apparaat, teruglevering.</summary>
    public Color AccentColor => ThemeColors.Get(Tip.Kind switch
    {
        AdviceTipKind.Charger => "Grid",
        AdviceTipKind.Battery => "Battery",
        AdviceTipKind.Export => "Export",
        _ => "Sun"
    });
}

/// <summary>Slim sturen: wanneer laad en verbruik je het goedkoopst, en wat levert dat op.</summary>
public partial class AdviceViewModel : BaseViewModel
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    private readonly ApiClient _api;
    private readonly AuthService _auth;

    public ObservableCollection<AdviceTipRow> Tips { get; } = [];

    [ObservableProperty] private string _totalText = "€ 0,00";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _cheapestText = string.Empty;
    [ObservableProperty] private string _expensiveText = string.Empty;
    [ObservableProperty] private bool _hasPrices;
    [ObservableProperty] private bool _hasTips;
    [ObservableProperty] private bool _hasLoaded;
    [ObservableProperty] private bool _isRefreshing;

    public AdviceViewModel(ApiClient api, AuthService auth)
    {
        _api = api;
        _auth = auth;
        Title = "Slim sturen";
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        await RunSafeAsync(async () =>
        {
            var advice = await _api.GetAdviceAsync();
            Apply(advice);
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

    private void Apply(AdviceDto advice)
    {
        TotalText = "+€ " + advice.TotalSavingEur.ToString("0.00", Nl);
        Subtitle = advice.TotalSavingEur > 0
            ? "extra als je de tips hieronder volgt"
            : advice.IsDynamic
                ? "vandaag valt er weinig te verschuiven"
                : "met een vast contract scheelt verschuiven pas vanaf 2027";

        HasPrices = advice.Cheapest is not null;
        CheapestText = advice.Cheapest is null ? string.Empty : $"Goedkoopst {Span(advice.Cheapest)} · {Cents(advice.Cheapest.AverageAllInEur)}";
        ExpensiveText = advice.MostExpensive is null ? string.Empty : $"Duurst {Span(advice.MostExpensive)} · {Cents(advice.MostExpensive.AverageAllInEur)}";

        Tips.Clear();
        foreach (var tip in advice.Tips)
            Tips.Add(new AdviceTipRow(tip));
        HasTips = Tips.Count > 0;
    }

    private static string Span(PriceWindowDto w)
    {
        var from = w.From.ToLocalTime();
        var prefix = from.Date == DateTime.Today ? string.Empty : "morgen ";
        return $"{prefix}{from:HH:mm}–{w.To.ToLocalTime():HH:mm}";
    }

    private static string Cents(decimal eur) => ((int)Math.Round(eur * 100)).ToString(Nl) + " ct/kWh";
}
