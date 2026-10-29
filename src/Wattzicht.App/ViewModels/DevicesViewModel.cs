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

    public ObservableCollection<DeviceItem> Devices { get; } = [];

    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private bool _hasLoaded;

    public DevicesViewModel(ApiClient api, AuthService auth)
    {
        _api = api;
        _auth = auth;
        Title = "Apparaten";
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        await RunSafeAsync(async () =>
        {
            var devices = await _api.GetDevicesAsync();
            Devices.Clear();
            foreach (var d in devices.OrderBy(d => d.Type).ThenBy(d => d.Name))
                Devices.Add(new DeviceItem(d));
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

    [RelayCommand]
    private async Task DeleteAsync(DeviceItem? item)
    {
        if (item is null) return;

        var confirmed = await ConfirmAsync(
            "Apparaat verwijderen?",
            $"Je verwijdert '{item.Name}'. De metingen blijven bewaard, maar er komen geen nieuwe meer binnen.",
            "Verwijderen");
        if (!confirmed) return;

        await RunSafeAsync(async () =>
        {
            await _api.DeleteDeviceAsync(item.Id);
            Devices.Remove(item);
        }, _auth);

        if (HasError)
            await ShowAlertAsync("Verwijderen mislukt", ErrorMessage!);
    }
}
