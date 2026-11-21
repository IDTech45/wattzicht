using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wattzicht.App.Services;
using Wattzicht.Shared;

namespace Wattzicht.App.ViewModels;

/// <summary>Eén woning in de lijst van de installateur.</summary>
public sealed class WoningRij
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    public InstallerHouseholdDto Woning { get; }

    public WoningRij(InstallerHouseholdDto woning) => Woning = woning;

    public Guid Id => Woning.Id;
    public string Naam => Woning.CustomerName;

    public string Plaats => string.IsNullOrWhiteSpace(Woning.City)
        ? Woning.PostalCode ?? string.Empty
        : Woning.City!;

    public string OpwekVandaag => Woning.TodayPvKwh.ToString("N1", Nl) + " kWh";

    /// <summary>
    /// Wanneer er voor het laatst iets binnenkwam, in woorden. Een datum zegt een installateur niets;
    /// "11 dagen stil" is precies waar hij op zoekt.
    /// </summary>
    public string LaatstGezien
    {
        get
        {
            if (Woning.LastSeen is not { } gezien) return Taal.T("installateur.nooit-gezien");
            var verschil = DateTimeOffset.UtcNow - gezien;
            if (verschil < TimeSpan.FromMinutes(90)) return Taal.T("installateur.net-nog");
            if (verschil < TimeSpan.FromHours(36)) return Taal.T("installateur.vandaag-gezien");
            return string.Format(Nl, Taal.T("installateur.dagen-stil"), (int)verschil.TotalDays);
        }
    }

    public string StatusTekst => Woning.Status switch
    {
        HouseholdStatus.Fault => Taal.T("installateur.status-storing"),
        HouseholdStatus.NoData => Taal.T("installateur.status-stil"),
        _ => Taal.T("installateur.status-goed"),
    };

    /// <summary>Rood bij een storing, grijs bij stilte, groen als het gewoon loopt.</summary>
    public Color StatusKleur => Woning.Status switch
    {
        HouseholdStatus.Fault => Color.FromArgb("#DC2626"),
        HouseholdStatus.NoData => Color.FromArgb("#94A3B8"),
        _ => Color.FromArgb("#16A34A"),
    };

    public bool HeeftMeldingen => Woning.OpenAlerts > 0;
    public string MeldingenTekst => Woning.OpenAlerts.ToString(Nl);
}

/// <summary>Eén regel op de aandachtlijst.</summary>
public sealed class AandachtRij
{
    public AandachtDto Punt { get; }

    public AandachtRij(AandachtDto punt) => Punt = punt;

    public Guid Id => Punt.HouseholdId;
    public string Naam => Punt.Naam;
    public string Plaats => Punt.Plaats ?? string.Empty;
    public string Toelichting => Punt.Toelichting ?? string.Empty;

    public string SoortTekst => Punt.Soort == AandachtSoort.Stil
        ? Taal.T("installateur.soort-stil")
        : Taal.T("installateur.soort-levert-te-weinig");

    public Color SoortKleur => Punt.Soort == AandachtSoort.Stil
        ? Color.FromArgb("#94A3B8")
        : Color.FromArgb("#F59E0B");
}

/// <summary>
/// Het scherm van de installateur in de app.
///
/// De installateur had alleen een portal in de browser. Dat is prima achter een bureau, maar wie op
/// een dak staat of in de bus zit pakt zijn telefoon, en dan is een tabel van twintig kolommen
/// onbruikbaar. Hier staan daarom twee dingen: wat er mis is, en de lijst met zijn woningen.
///
/// Hij ziet alleen woningen van zijn eigen bedrijf die hem inzage hebben gegeven; dat filtert de
/// server, niet dit scherm.
/// </summary>
public sealed partial class InstallateurViewModel : BaseViewModel
{
    private readonly ApiClient _api;
    private readonly AuthService _auth;

    private List<WoningRij> _alleWoningen = [];

    public InstallateurViewModel(ApiClient api, AuthService auth)
    {
        _api = api;
        _auth = auth;
    }

    [ObservableProperty] private string _bedrijfsnaam = string.Empty;
    [ObservableProperty] private int _totaalGoed;
    [ObservableProperty] private int _totaalStoring;
    [ObservableProperty] private int _totaalStil;
    [ObservableProperty] private bool _isRefreshing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeeftAandacht))]
    [NotifyPropertyChangedFor(nameof(AlleGoed))]
    private ObservableCollection<AandachtRij> _aandacht = [];

    public ObservableCollection<WoningRij> Woningen { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GeenZoekresultaat))]
    private string _zoek = string.Empty;

    public bool HeeftAandacht => Aandacht.Count > 0;

    /// <summary>Niets aan de hand is ook een uitkomst, en die hoort op het scherm te staan.</summary>
    public bool AlleGoed => !IsBusy && Aandacht.Count == 0 && _alleWoningen.Count > 0;

    public bool GeenZoekresultaat => Woningen.Count == 0 && _alleWoningen.Count > 0;

    [RelayCommand]
    private Task Verversen() => LaadAsync();

    public async Task LaadAsync(CancellationToken ct = default)
    {
        await RunSafeAsync(async () =>
        {
            var overzicht = await _api.GetInstallerOverviewAsync(ct);

            Bedrijfsnaam = overzicht.Installer.Name;
            TotaalGoed = overzicht.TotalOnline;
            TotaalStoring = overzicht.TotalFault;
            TotaalStil = overzicht.TotalNoData;

            _alleWoningen = overzicht.Households
                .OrderBy(h => h.Status == HouseholdStatus.Online)
                .ThenBy(h => h.CustomerName, StringComparer.CurrentCultureIgnoreCase)
                .Select(h => new WoningRij(h))
                .ToList();
            FilterWoningen();

            // De aandachtlijst komt apart: hij kijkt naar het weer van de afgelopen dagen en dat is
            // een zwaardere vraag. Mislukt hij, dan hoort de woningenlijst er gewoon te staan.
            try
            {
                var punten = await _api.GetInstallerAandachtAsync(ct);
                Aandacht = new ObservableCollection<AandachtRij>(punten.Select(p => new AandachtRij(p)));
            }
            catch (ApiException)
            {
                Aandacht = [];
            }

            OnPropertyChanged(nameof(AlleGoed));
        });

        IsRefreshing = false;
    }

    partial void OnZoekChanged(string value) => FilterWoningen();

    private void FilterWoningen()
    {
        var term = Zoek.Trim();
        var gefilterd = string.IsNullOrEmpty(term)
            ? _alleWoningen
            : _alleWoningen.Where(w =>
                w.Naam.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                || w.Plaats.Contains(term, StringComparison.CurrentCultureIgnoreCase)).ToList();

        Woningen.Clear();
        foreach (var rij in gefilterd) Woningen.Add(rij);
        OnPropertyChanged(nameof(GeenZoekresultaat));
    }

    [RelayCommand]
    private static async Task OpenWoning(Guid id)
    {
        if (id == Guid.Empty) return;
        await Shell.Current.GoToAsync($"{nameof(Pages.InstallateurWoningPage)}?id={id}");
    }

    [RelayCommand]
    private async Task Uitloggen()
    {
        await _auth.LogoutAsync();
        await Shell.Current.GoToAsync(AppShell.Routes.Login);
    }
}
