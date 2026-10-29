using Wattzicht.App.ViewModels;

namespace Wattzicht.App.Pages;

public partial class DevicesPage : ContentPage
{
    private readonly DevicesViewModel _viewModel;

    public DevicesPage(DevicesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }
}
