using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wattzicht.App.Services;
using Wattzicht.Shared;

namespace Wattzicht.App.ViewModels;

/// <summary>Eén maand in het invulscherm.</summary>
public partial class MaandRegel : ObservableObject
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    public MaandRegel(int maand, double kwh, bool gemeten)
    {
        Maand = maand;
        Gemeten = gemeten;
        _waarde = kwh > 0 ? kwh.ToString("0.#", Nl) : string.Empty;
    }

    public int Maand { get; }

    /// <summary>Hebben wij deze maand zelf gemeten? Dan telt een opgave niet mee.</summary>
    public bool Gemeten { get; }

    public string Naam => new DateTime(2000, Maand, 1).ToString("MMMM", Nl);

    [ObservableProperty] private string _waarde;

    /// <summary>Wat er ingevuld staat, of 0 als het niets of onzin is.</summary>
    public double Kwh =>
        double.TryParse((Waarde ?? string.Empty).Replace(',', '.'), NumberStyles.Any,
            CultureInfo.InvariantCulture, out var d) && d > 0
            ? d
            : 0;
}

/// <summary>
/// Je maandopbrengsten zelf invullen.
///
/// Dit bestaat omdat de weg eromheen dicht zit. De dagreeks van Enphase zit op het gratis plan
/// achter slot ("Not authorized to access this resource"), en in hun eigen app staat nergens een
/// export. Wat er wél staat is een jaargrafiek met twaalf balkjes, en die getallen kun je overtypen.
///
/// Elf getallen overnemen is lelijk. Een leeg jaar terwijl je dak vier megawattuur heeft gemaakt is
/// erger, en dat is precies wat er stond.
/// </summary>
public partial class MaandOpgaveViewModel(ApiClient api, AuthService auth) : BaseViewModel
{
    private readonly ApiClient _api = api;
    private readonly AuthService _auth = auth;

    public ObservableCollection<MaandRegel> Maanden { get; } = [];

    [ObservableProperty] private int _jaar = DateTime.Today.Year;

    [ObservableProperty] private string _resultaat = string.Empty;

    /// <summary>Of de maanden er al staan; het scherm hoeft ze niet bij elke terugkeer op te halen.</summary>
    public bool Geladen { get; private set; }

    public bool HeeftResultaat => !string.IsNullOrEmpty(Resultaat);

    partial void OnResultaatChanged(string value) => OnPropertyChanged(nameof(HeeftResultaat));

    public async Task LaadAsync()
    {
        await RunSafeAsync(async () =>
        {
            var uit = await _api.MaandOpgaveAsync(Jaar);
            Maanden.Clear();
            foreach (var maand in uit.Maanden.OrderBy(m => m.Month))
            {
                Maanden.Add(new MaandRegel(maand.Month, maand.PvKwh, maand.Gemeten));
            }

            Geladen = true;
        }, _auth);
    }

    [RelayCommand]
    private Task VorigJaarAsync()
    {
        Jaar--;
        Resultaat = string.Empty;
        return LaadAsync();
    }

    [RelayCommand]
    private Task VolgendJaarAsync()
    {
        if (Jaar >= DateTime.Today.Year) return Task.CompletedTask;
        Jaar++;
        Resultaat = string.Empty;
        return LaadAsync();
    }

    [RelayCommand]
    private async Task OpslaanAsync()
    {
        await RunSafeAsync(async () =>
        {
            var waarden = Maanden
                .Where(m => !m.Gemeten)
                .Select(m => new MaandOpgaveWaarde(m.Maand, m.Kwh))
                .ToList();

            var uit = await _api.MaandOpgaveOpslaanAsync(new MaandOpgaveRequest(Jaar, waarden));

            var totaal = uit.Maanden.Sum(m => m.PvKwh);
            Resultaat = string.Format(Taal.T("maandopgave.gelukt"), totaal.ToString("N0", CultureInfo.GetCultureInfo("nl-NL")));
        }, _auth);
    }
}
