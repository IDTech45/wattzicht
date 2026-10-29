using System.ComponentModel;
using Wattzicht.App.ViewModels;

namespace Wattzicht.App.Pages;

public partial class SavingsPage : ContentPage
{
    private readonly SavingsViewModel _viewModel;

    public SavingsPage(SavingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // De drawable zelf is geen bindable; opnieuw tekenen zodra de data wisselt.
        if (e.PropertyName == nameof(SavingsViewModel.ChartVersion))
            ChartView.Invalidate();
    }
}
