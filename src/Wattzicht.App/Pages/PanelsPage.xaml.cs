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

    public string DeviceId
    {
        set
        {
            if (Guid.TryParse(Uri.UnescapeDataString(value ?? string.Empty), out var id))
            {
                _viewModel.DeviceId = id;
            }
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }
}
