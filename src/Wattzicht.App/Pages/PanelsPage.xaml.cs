using Wattzicht.App.ViewModels;

namespace Wattzicht.App.Pages;

/// <summary>
/// Je dak, paneel voor paneel. Het apparaat komt als route-parameter mee, want een woning kan meer
/// dan één omvormer hebben en dan gaat dit scherm over die ene.
/// </summary>
[QueryProperty(nameof(DeviceId), "deviceId")]
public partial class PanelsPage : ContentPage
{
    private readonly PanelsViewModel _viewModel;

    public PanelsPage(PanelsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    /// <summary>
    /// Het apparaat uit de route.
    ///
    /// Komt er niets mee of iets onleesbaars, dan blijft de id leeg en vraagt de viewmodel niets
    /// op. Eerder ging er een verzoek uit over een apparaat met alleen nullen, en dan stond er
    /// "Niet gevonden" op het scherm: een foutmelding over de server terwijl het aan de route lag.
    /// </summary>
    public string DeviceId
    {
        set
        {
            _viewModel.DeviceId = Guid.TryParse(Uri.UnescapeDataString(value ?? string.Empty), out var id)
                ? id
                : Guid.Empty;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }
}
