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
    public string SolarText => string.Format(Taal.T("advies.waarvan-zon"), slot.SolarKwh.ToString("0.#", Nl));
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
                PlanError = Taal.T("advies.vul-kilowattuur-in");
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
            foreach (var slot in plan.Slots ?? [])
            {
                PlanSlots.Add(new ChargeSlotRow(slot));
            }

            PlanSummary = plan.Explanation ?? string.Empty;
            PlanHasSaving = plan.SavingEur > 0;
            PlanSavingText = string.Format(Taal.T("advies.goedkoper-dan-nu-beginnen"),
                "€ " + plan.SavingEur.ToString("0.00", Nl));
            HasPlan = true;

            if (!plan.Feasible)
            {
                PlanError = Taal.T("advies.past-niet-helemaal");
                PlanHasError = true;
            }
        }
        catch (ApiException ex)
        {
            // Zonder "when" en met een vangnet erachter.
            //
            // De filter liet 401 er doorheen, en dan is er niemand meer die hem opvangt: een
            // commando dat een Task teruggeeft gooit de fout op de hoofddraad en de app sluit.
            // Precies wat er gebeurt als iemand na een week de app opent, op Sturen blijft staan en
            // op Bereken drukt. Nu loopt een verlopen sessie langs de gewone uitlog.
            PlanError = ex.Message;
            PlanHasError = true;
            if (ex.IsUnauthorized) await _auth.LogoutAsync();
        }
        catch (Exception)
        {
            PlanError = Taal.T("algemeen.er-ging-iets-mis");
            PlanHasError = true;
        }
        finally
        {
            PlanBusy = false;
        }
    }

    private readonly AuthService _auth;

    public ObservableCollection<AdviceTipRow> Tips { get; } = [];

    /// <summary>
    /// Beginwaarde een streepje: tot het antwoord van de server binnen is weten we niet wat er te
    /// verschuiven valt, en "+€ 0,00" leest als die uitkomst. Zelfde afspraak als op de andere
    /// schermen.
    /// </summary>
    [ObservableProperty] private string _totalText = "–";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _cheapestText = string.Empty;
    [ObservableProperty] private string _expensiveText = string.Empty;
    [ObservableProperty] private bool _hasPrices;

    // ---- Zon ----
    //
    // Of het weer meetelt hoorde zichtbaar te zijn. Er stond een tijdstip op het scherm en niemand
    // kon zien of dat uit de zonverwachting kwam of alleen uit de uurprijzen. Zonder verwachting
    // zeggen we dat ook, met wat eraan ontbreekt, in plaats van de regel stilletjes weg te laten.
    [ObservableProperty] private string _zonTekst = string.Empty;
    [ObservableProperty] private string _zonBron = string.Empty;
    [ObservableProperty] private bool _heeftZon;
    [ObservableProperty] private bool _hasTips;
    [ObservableProperty] private bool _hasLoaded;
    [ObservableProperty] private bool _isRefreshing;

    public AdviceViewModel(ApiClient api, AuthService auth)
    {
        _api = api;
        _auth = auth;
        Title = Taal.T("advies.slim-sturen");
    }

    // ---- Echt sturen ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KanPaalSturen))]
    [NotifyPropertyChangedFor(nameof(VraagToestemming))]
    private DeviceDto? _paal;

    [ObservableProperty] private string? _stuurResultaat;

    /// <summary>Er is een paal die wij kunnen aansturen én waarvoor de bewoner dat heeft aangezet.</summary>
    public bool KanPaalSturen => Paal is { KanSturen: true, SturenToegestaan: true };

    /// <summary>Er is zo'n paal, maar het staat nog uit. Dan vragen we het, in plaats van niets te tonen.</summary>
    public bool VraagToestemming => Paal is { KanSturen: true, SturenToegestaan: false };

    [RelayCommand]
    public async Task LoadAsync()
    {
        await RunSafeAsync(async () =>
        {
            var advice = await _api.GetAdviceAsync();
            Apply(advice);
            HasLoaded = true;

            // De eerste paal die aangestuurd kan worden. Eén is genoeg: wie er twee heeft, stuurt ze
            // allebei met dezelfde knop zodra dat nodig blijkt.
            var apparaten = await _api.GetDevicesAsync();
            Paal = apparaten.FirstOrDefault(d => d.Type == DeviceType.Charger && d.KanSturen);
        }, _auth);
        IsRefreshing = false;
    }

    /// <summary>
    /// Toestemming geven om deze paal te starten en te pauzeren.
    ///
    /// Apart van het koppelen en standaard uit: uitlezen is kijken, sturen verandert iets in je huis.
    /// </summary>
    [RelayCommand]
    private async Task SturenAanzettenAsync()
    {
        if (Paal is not { } paal) return;

        var akkoord = await ConfirmAsync(
            Taal.T("sturen.toestemming-titel"),
            Taal.T("sturen.toestemming-tekst"),
            Taal.T("sturen.toestemming-ja"));

        if (!akkoord) return;

        await RunSafeAsync(async () => Paal = await _api.SturenToestaanAsync(paal.Id, true), _auth);
    }

    [RelayCommand]
    private Task NuLadenAsync() => StuurAsync(start: true);


    [RelayCommand]
    private Task PauzerenAsync() => StuurAsync(start: false);

    private async Task StuurAsync(bool start)
    {
        if (Paal is not { } paal) return;

        StuurResultaat = null;

        try
        {
            IsBusy = true;
            await _api.LaadopdrachtAsync(paal.Id, start);
            StuurResultaat = Taal.T(start ? "sturen.gestart" : "sturen.gepauzeerd");
        }
        catch (ApiException ex)
        {
            // De paal zegt zelf waarom het niet lukte; dat is bruikbaarder dan "mislukt".
            StuurResultaat = ex.Message;
        }
        catch (Exception)
        {
            StuurResultaat = Taal.T("algemeen.er-ging-iets-mis");
        }
        finally
        {
            IsBusy = false;
        }
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
        Subtitle = Taal.T(advice.TotalSavingEur > 0
            ? "advies.extra-als-je-de-tips-volgt"
            : advice.IsDynamic
                ? "advies.weinig-te-verschuiven"
                : "advies.vast-contract-pas-vanaf-2027");

        HasPrices = advice.Cheapest is not null;
        CheapestText = advice.Cheapest is null ? string.Empty
            : string.Format(Taal.T("advies.goedkoopst"), Span(advice.Cheapest), Cents(advice.Cheapest.AverageAllInEur));
        ExpensiveText = advice.MostExpensive is null ? string.Empty
            : string.Format(Taal.T("advies.duurst"), Span(advice.MostExpensive), Cents(advice.MostExpensive.AverageAllInEur));

        HeeftZon = true;
        if (advice.SolarSource is { Length: > 0 } bron && advice.ExpectedSolarKwh > 0)
        {
            var venster = advice.SunFrom is { } van && advice.SunTo is { } tot
                ? string.Format(Taal.T("advies.zon-venster"), Tijd(van), Tijd(tot))
                : string.Empty;
            ZonTekst = string.Format(Taal.T("advies.zon-vandaag"), advice.ExpectedSolarKwh.ToString("0.#", Nl), venster);
            ZonBron = string.Format(Taal.T("advies.zon-bron"), bron);
        }
        else
        {
            ZonTekst = Taal.T("advies.geen-zonverwachting");
            ZonBron = Taal.T("advies.geen-zonverwachting-uitleg");
        }

        Tips.Clear();
        foreach (var tip in advice.Tips ?? [])
            Tips.Add(new AdviceTipRow(tip));
        HasTips = Tips.Count > 0;
    }

    /// <summary>Tijd in de zone van het toestel; dat is voor onze gebruikers de Nederlandse.</summary>
    private static string Tijd(DateTimeOffset moment) => moment.ToLocalTime().ToString("HH:mm");

    private static string Span(PriceWindowDto w)
    {
        var from = w.From.ToLocalTime();
        var prefix = from.Date == DateTime.Today ? string.Empty : Taal.T("advies.morgen") + " ";
        return $"{prefix}{from:HH:mm}–{w.To.ToLocalTime():HH:mm}";
    }

    private static string Cents(decimal eur) =>
        string.Format(Taal.T("advies.cent-per-kwh"), ((int)Math.Round(eur * 100)).ToString(Nl));
}
