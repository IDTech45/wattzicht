using Wattzicht.App.ViewModels;

namespace Wattzicht.App.Pages;

public partial class ConnectDevicePage : ContentPage
{
    public ConnectDevicePage(ConnectDeviceViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
