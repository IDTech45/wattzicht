using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wattzicht.App.Models;
using Wattzicht.App.Pages;
using Wattzicht.App.Services;

namespace Wattzicht.App.ViewModels;

public partial class DevicesViewModel : BaseViewModel
{
    private readonly ApiClient _api;
    private readonly AuthService _auth;
    private readonly LokaleMeter _lokaal;

    public ObservableCollection<DeviceItem> Devices { get; } = [];

    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private bool _hasLoaded;

    public DevicesViewModel(ApiClient api, AuthService auth, LokaleMeter lokaal)
    {
        _api = api;
        _auth = auth;
        _lokaal = lokaal;
        Title = Taal.T("apparaten.apparaten");
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        await RunSafeAsync(async () =>
        {
            var devices = await _api.GetDevicesAsync();

            // Kastjes in huis leest de app zelf, en dit is het moment: de telefoon is er en dit
            // scherm gaat juist over die apparaten. Stond dit er niet, dan keek je hier naar een
            // status van gisteravond terwijl je thuis op de bank zat.
            if (devices.Count > 0)
            {
                await _lokaal.StuurDoorAsync(devices);
                devices = await _api.GetDevicesAsync();
            }

            // Het piekvermogen hangt aan de woning. Een aparte vraag dus, en eentje die mag mislukken:
            // de lijst met apparaten hoort niet leeg te blijven omdat dat ene getal niet opkwam.
            double? piek = null;
            int? panelen = null;
            try
            {
                var woning = await _api.GetHouseholdAsync();
                piek = woning.PvPeakKw;
                panelen = woning.PvPanelCount;
            }
            catch (ApiException)
            {
                piek = null;
            }

            Devices.Clear();
            foreach (var d in devices.OrderBy(d => d.Type).ThenBy(d => d.Name))
                Devices.Add(new DeviceItem(d, piek, panelen));
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

    [RelayCommand]
    private Task ConnectDeviceAsync() => Shell.Current.GoToAsync(nameof(ConnectDevicePage));

    /// <summary>
    /// Nu uitlezen: de server doet de merken met een cloud, de app de kastjes in huis.
    ///
    /// Wie net een token heeft rechtgezet wil meteen weten of het goed is, en niet een kwartier op
    /// de volgende ronde wachten.
    /// </summary>
    [RelayCommand]
    private async Task NuUitlezenAsync()
    {
        await RunSafeAsync(async () =>
        {
            var apparaten = await _api.GetDevicesAsync();
            await _lokaal.StuurDoorAsync(apparaten, forceer: true);

            var uitkomst = await _api.NuUitlezenAsync();
            await LoadAsync();

            if (uitkomst.Mislukt > 0 && !string.IsNullOrWhiteSpace(uitkomst.Reden))
            {
                await ShowAlertAsync(Taal.T("apparaten.uitlezen-mislukt"), uitkomst.Reden!);
            }
        }, _auth);
    }

    /// <summary>Naar het scherm waar je je oude cijfers kunt inlezen.</summary>
    [RelayCommand]
    private Task NaarInstellingenAsync() => Shell.Current.GoToAsync("//settings");

    /// <summary>
    /// Een apparaat hernoemen.
    ///
    /// De fabrikant levert namen als "Demo omvormer 4,2 kWp". Wie twee strings heeft wil "voorkant"
    /// en "achterkant", en dat kon nergens.
    /// </summary>
    [RelayCommand]
    private async Task HernoemAsync(DeviceItem? item)
    {
        if (item is null) return;

        var naam = await VraagAsync(
            Taal.T("apparaten.hernoemen"),
            Taal.T("apparaten.hernoemen-uitleg"),
            item.Name);

        if (string.IsNullOrWhiteSpace(naam) || naam.Trim() == item.Name) return;

        await RunSafeAsync(async () =>
        {
            await _api.HernoemAsync(item.Id, naam.Trim());
            await LoadAsync();
        }, _auth);
    }

    [RelayCommand]
    private async Task DeleteAsync(DeviceItem? item)
    {
        if (item is null) return;

        var confirmed = await ConfirmAsync(
            Taal.T("apparaten.verwijderen-vraag"),
            string.Format(Taal.T("apparaten.verwijderen-uitleg"), item.Name),
            Taal.T("apparaten.verwijderen"));
        if (!confirmed) return;

        await RunSafeAsync(async () =>
        {
            await _api.DeleteDeviceAsync(item.Id);
            Devices.Remove(item);
        }, _auth);

        if (HasError)
            await ShowAlertAsync(Taal.T("apparaten.verwijderen-mislukt"), ErrorMessage!);
    }
}
