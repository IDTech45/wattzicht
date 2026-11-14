using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wattzicht.App.Services;
using Wattzicht.Shared;

namespace Wattzicht.App.ViewModels;

/// <summary>
/// Je dak, paneel voor paneel.
///
/// Waarom dit scherm bestaat: een installatie is geen apparaat maar een rij panelen, en de vraag die
/// mensen erover hebben gaat over die rij. Doet er een minder? Ligt er een in de schaduw? Een totaal
/// in kilowatt is daar geen antwoord op, en het aantal panelen op een kaart ook niet.
///
/// Alles komt van de gateway in de meterkast, dus van het thuisnetwerk. Dat betekent ook dat het zo
/// vers is als de laatste keer dat de app thuis openstond, en dat staat er met zoveel woorden bij.
/// </summary>
public partial class PanelsViewModel(ApiClient api, AuthService auth) : BaseViewModel
{
    private readonly ApiClient _api = api;
    private readonly AuthService _auth = auth;
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    public ObservableCollection<PaneelItem> Panelen { get; } = [];

    public Guid DeviceId { get; set; }

    [ObservableProperty] private bool _isRefreshing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToonLeeg))]
    private bool _hasLoaded;

    [ObservableProperty] private string _kop = string.Empty;
    [ObservableProperty] private string _bijgewerkt = string.Empty;
    [ObservableProperty] private string _oordeel = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToonLeeg))]
    private bool _heeftPanelen;

    /// <summary>
    /// Dit scherm is geopend zonder bruikbaar apparaat. Dat is iets anders dan een omvormer die nog
    /// niets gemeld heeft, en hoort dus ook iets anders te zeggen.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToonLeeg))]
    private bool _geenApparaat;

    public bool ToonLeeg => HasLoaded && !HeeftPanelen && !GeenApparaat;

    public async Task LoadAsync()
    {
        // Zonder bruikbaar apparaat valt er niets op te vragen. Dat zeggen we met zoveel woorden in
        // plaats van een verzoek te doen over een id van alleen nullen en de 404 van de server door
        // te geven als "Niet gevonden".
        if (DeviceId == Guid.Empty)
        {
            Panelen.Clear();
            HeeftPanelen = false;
            Kop = string.Empty;
            Bijgewerkt = string.Empty;
            Oordeel = string.Empty;
            GeenApparaat = true;
            HasLoaded = true;
            IsRefreshing = false;
            return;
        }

        GeenApparaat = false;

        await RunSafeAsync(async () =>
        {
            var uit = await _api.PanelenAsync(DeviceId);

            Panelen.Clear();

            // De beste van dit moment is de maat op het scherm: daar wordt elke balk aan afgemeten.
            // Zo zie je in een oogopslag wie meeloopt en wie niet, ook als de hele rij halve kracht
            // draait omdat het bewolkt is.
            var lijst = uit.Panelen ?? [];
            var beste = lijst.Count == 0 ? 0 : lijst.Max(p => p.Watt);

            foreach (var p in lijst)
            {
                Panelen.Add(new PaneelItem(p, beste));
            }

            HeeftPanelen = Panelen.Count > 0;

            Kop = string.Format(
                Taal.T(Panelen.Count == 1 ? "panelen.kop-een" : "panelen.kop"),
                Panelen.Count,
                (uit.TotaalWatt / 1000.0).ToString("0.0", Nl));

            Bijgewerkt = uit.BijgewerktOp is { } op
                ? string.Format(Taal.T("panelen.bijgewerkt"), TimeText.Ago(op, Taal.T("tijd.zojuist")))
                : string.Empty;

            Oordeel = Oordelen(uit);
            HasLoaded = true;
        }, _auth);

        IsRefreshing = false;
    }

    /// <summary>
    /// Eén zin over de hele rij.
    ///
    /// Alleen iets zeggen als er iets te zeggen valt. "Alles doet het ongeveer even goed" is ook een
    /// uitkomst en mag er staan, maar alleen als de zon er is: 's avonds staat alles op nul en dan is
    /// elke uitspraak over achterblijvers onzin.
    /// </summary>
    private static string Oordelen(PanelenDto uit)
    {
        var lijst = uit.Panelen ?? [];
        if (lijst.Count == 0) return string.Empty;
        if (uit.TotaalWatt < Panelen_MinimumTotaal) return Taal.T("panelen.te-donker");

        if (uit.Achterblijvers == 0) return Taal.T("panelen.alles-gelijk");

        var achter = lijst.Where(p => p.BlijftAchter).ToList();
        return achter.Count == 1
            ? string.Format(Taal.T("panelen.achterblijver"), achter[0].Nummer)
            : string.Format(Taal.T("panelen.achterblijvers"), achter.Count);
    }

    /// <summary>Naar de uitleg over je eigen Enphase-sleutel.</summary>
    [RelayCommand]
    private Task EigenSleutelAsync() => Shell.Current.GoToAsync(nameof(Pages.EnphaseSleutelPage));

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        await LoadAsync();
    }

    /// <summary>Onder dit totaal is het schemer en zegt vergelijken niets.</summary>
    private const double Panelen_MinimumTotaal = 200;
}

/// <summary>Eén paneel op het scherm.</summary>
public sealed class PaneelItem
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    public PaneelItem(PaneelDto paneel, double besteWatt)
    {
        Nummer = paneel.Nummer;
        Watt = paneel.Watt;
        BlijftAchter = paneel.BlijftAchter;
        MaxWatt = paneel.MaxWatt;

        // Minstens een streepje, ook bij nul: een lege kolom leest als een ontbrekend paneel.
        Vulling = besteWatt <= 0 ? 0.04 : Math.Clamp(paneel.Watt / besteWatt, 0.04, 1);
    }

    public int Nummer { get; }
    public double Watt { get; }
    public double MaxWatt { get; }
    public bool BlijftAchter { get; }
    public double Vulling { get; }

    public string NummerTekst => Nummer.ToString(Nl);
    public string WattTekst => $"{Watt.ToString("0", Nl)} W";
    public string MaxTekst => MaxWatt > 0 ? string.Format(Taal.T("panelen.max"), MaxWatt.ToString("0", Nl)) : string.Empty;

    /// <summary>De hoogte van de balk in het vakje; vaste maat zodat de rij netjes blijft.</summary>
    public double BalkHoogte => Math.Round(Vulling * 54, 1);

    public Color BalkKleur => BlijftAchter
        ? ThemeColors.Themed("WarningText", "WarningTextDark")
        : ThemeColors.Themed("EnergySolar", "EnergySolar");
}
