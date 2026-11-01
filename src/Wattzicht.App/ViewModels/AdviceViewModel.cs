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
        AdviceTipKind.Charger => "EnergyCharger",
        AdviceTipKind.Battery => "EnergyBattery",
        AdviceTipKind.Export => "EnergyGrid",
        _ => "Accent500"
    });
}

/// <summary>Eén blok van het laadplan: een uur waarin de auto laadt.</summary>
public sealed class ChargeSlotRow(ChargeSlotDto slot)
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    public string TimeText => slot.StartUtc.ToLocalTime().ToString("HH:mm");
    public string KwhText => slot.Kwh.ToString("0.#", Nl) + " kWh";

    /// <summary>Hoeveel daarvan van het eigen dak komt; dat is het hele punt van deze planner.</summary>
    public bool HasSolar => slot.SolarKwh > 0.05;
    public string SolarText => "waarvan " + slot.SolarKwh.ToString("0.#", Nl) + " kWh zon";
    public string CostText => "€ " + slot.CostEur.ToString("0.00", Nl);
}

/// <summary>Slim sturen: wanneer laad en verbruik je het goedkoopst, en wat levert dat op.</summary>
public partial class AdviceViewModel : BaseViewModel
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    private readonly ApiClient _api;

    // ---- Laadplanner ----
    //
    // Bewust vragen in plaats van een vast schema: de vraag verschilt per dag. Vanavond moet er 30 kWh
    // in voor een rit naar Groningen, morgen is 10 genoeg. Een vast schema zou daar altijd naast zitten.

    /// <summary>Hoeveel er in moet. Tekst, want een invoerveld op een telefoon geeft tekst.</summary>
    [ObservableProperty] private string _planKwh = "20";

    /// <summary>Hoe laat hij klaar moet zijn, morgenochtend.</summary>
    [ObservableProperty] private TimeSpan _planKlaarOm = new(7, 0, 0);

    [ObservableProperty] private bool _hasPlan;
    [ObservableProperty] private bool _planBusy;
    [ObservableProperty] private string _planSummary = string.Empty;
    [ObservableProperty] private string _planSavingText = string.Empty;
    [ObservableProperty] private bool _planHasSaving;
    [ObservableProperty] private string? _planError;
    [ObservableProperty] private bool _planHasError;

    public ObservableCollection<ChargeSlotRow> PlanSlots { get; } = [];

    /// <summary>Vraagt het laadplan op voor wat er in de velden staat.</summary>
    [RelayCommand]
    private async Task MaakLaadplanAsync()
    {
        PlanBusy = true;
        PlanHasError = false;
        PlanError = null;

        try
        {
            if (!double.TryParse(PlanKwh.Replace(',', '.'),
                    System.Globalization.NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var kwh) || kwh is <= 0 or > 200)
            {
                PlanError = "Vul in hoeveel kilowattuur erin moet, bijvoorbeeld 20.";
                PlanHasError = true;
                return;
            }

            // Het eerstvolgende moment dat dit tijdstip langskomt: vanavond om elf uur is "morgen
            // zeven uur" bedoeld, vanochtend om vijf uur is dat vandaag.
            var nu = DateTimeOffset.Now;
            var klaar = new DateTimeOffset(nu.Year, nu.Month, nu.Day, 0, 0, 0, nu.Offset) + PlanKlaarOm;
            if (klaar <= nu) klaar = klaar.AddDays(1);

            var plan = await _api.GetChargePlanAsync(kwh, klaar.ToUniversalTime());

            PlanSlots.Clear();
            foreach (var slot in plan.Slots)
            {
                PlanSlots.Add(new ChargeSlotRow(slot));
            }

            PlanSummary = plan.Explanation;
            PlanHasSaving = plan.SavingEur > 0;
            PlanSavingText = "€ " + plan.SavingEur.ToString("0.00", Nl) + " goedkoper dan nu beginnen";
            HasPlan = true;

            if (!plan.Feasible)
            {
                PlanError = "In die tijd past het niet helemaal. Dit is het beste dat erin past.";
                PlanHasError = true;
            }
        }
        catch (ApiException ex) when (!ex.IsUnauthorized)
        {
            PlanError = ex.Message;
            PlanHasError = true;
        }
        finally
        {
            PlanBusy = false;
        }
    }

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
