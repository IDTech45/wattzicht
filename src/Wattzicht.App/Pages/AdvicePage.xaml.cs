using Wattzicht.App.ViewModels;

namespace Wattzicht.App.Pages;

public partial class AdvicePage : ContentPage
{
    private readonly AdviceViewModel _viewModel;

    public AdvicePage(AdviceViewModel viewModel)
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
