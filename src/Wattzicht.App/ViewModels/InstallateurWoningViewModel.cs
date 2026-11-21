using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wattzicht.App.Services;
using Wattzicht.Shared;

namespace Wattzicht.App.ViewModels;

/// <summary>Eén apparaat van de bewoner, zoals de installateur het wil zien.</summary>
public sealed class ApparaatRij
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    public DeviceDto Apparaat { get; }

    public ApparaatRij(DeviceDto apparaat) => Apparaat = apparaat;

    public string Naam => string.IsNullOrWhiteSpace(Apparaat.Name) ? Apparaat.Brand.ToString() : Apparaat.Name;
    public string Merk => Apparaat.Brand.ToString();

    public string LaatstGezien => Apparaat.LastSeenAt is { } gezien
        ? gezien.ToLocalTime().ToString("d MMM HH:mm", Nl)
        : Taal.T("installateur.nooit-gezien");

    public Color Kleur => Apparaat.LastSeenAt is { } gezien && DateTimeOffset.UtcNow - gezien < TimeSpan.FromHours(36)
        ? Color.FromArgb("#16A34A")
        : Color.FromArgb("#94A3B8");
}

/// <summary>
/// Eén woning, voor de installateur onderweg.
///
/// Wat hij hier zoekt is niet het verhaal van de bewoner maar het antwoord op twee vragen: doet de
/// installatie het, en waar ging het mis. Dus de meldingen bovenaan, dan de apparaten met wanneer ze
/// voor het laatst iets stuurden, en pas daarna de opbrengst.
/// </summary>
[QueryProperty(nameof(WoningId), "id")]
public sealed partial class InstallateurWoningViewModel : BaseViewModel
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    private readonly ApiClient _api;

    public InstallateurWoningViewModel(ApiClient api) => _api = api;

    [ObservableProperty] private string _woningId = string.Empty;

    [ObservableProperty] private string _naam = string.Empty;
    [ObservableProperty] private string _plaats = string.Empty;
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _vandaagTekst = "—";
    [ObservableProperty] private string _maandTekst = "—";
    [ObservableProperty] private string _laatstGezien = string.Empty;
    [ObservableProperty] private bool _isRefreshing;

    public ObservableCollection<ApparaatRij> Apparaten { get; } = [];
    public ObservableCollection<AlertDto> Meldingen { get; } = [];

    public bool HeeftMeldingen => Meldingen.Count > 0;

    partial void OnWoningIdChanged(string value) => _ = LaadAsync();

    [RelayCommand]
    private Task Verversen() => LaadAsync();

    public async Task LaadAsync(CancellationToken ct = default)
    {
        if (!Guid.TryParse(WoningId, out var id)) return;

        await RunSafeAsync(async () =>
        {
            var woning = await _api.GetInstallerHouseholdAsync(id, ct);

            Naam = woning.CustomerName;
            Title = woning.CustomerName;
            Plaats = string.Join(" ", new[] { woning.PostalCode, woning.City }.Where(s => !string.IsNullOrWhiteSpace(s)));
            Email = woning.Email ?? string.Empty;
            VandaagTekst = Kwh(woning.Today.PvKwh);
            MaandTekst = Kwh(woning.Month.PvKwh);
            LaatstGezien = woning.LastSeen is { } gezien
                ? gezien.ToLocalTime().ToString("d MMM HH:mm", Nl)
                : Taal.T("installateur.nooit-gezien");

            Apparaten.Clear();
            foreach (var apparaat in woning.Devices) Apparaten.Add(new ApparaatRij(apparaat));

            Meldingen.Clear();
            // Alleen wat nog openstaat: een opgeloste melding van drie weken terug is hier ruis.
            foreach (var melding in woning.Alerts.Where(a => a.ResolvedAt is null)) Meldingen.Add(melding);
            OnPropertyChanged(nameof(HeeftMeldingen));
        });

        IsRefreshing = false;
    }

    private static string Kwh(object? waarde) =>
        Convert.ToDouble(waarde ?? 0d).ToString("N1", Nl) + " kWh";
}
