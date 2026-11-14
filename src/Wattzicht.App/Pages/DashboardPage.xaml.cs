using System.ComponentModel;
using Wattzicht.App.ViewModels;

namespace Wattzicht.App.Pages;

public partial class DashboardPage : ContentPage
{
    private readonly DashboardViewModel _viewModel;

    public DashboardPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _viewModel.PropertyChanged += OpWijziging;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    /// <summary>
    /// De weericoontjes opnieuw tekenen.
    ///
    /// Een drawable is geen bindable: het object blijft hetzelfde en alleen de soort erin wisselt.
    /// Zonder deze duw blijft het icoon van de vorige keer staan, en dat zag je pas een dag later.
    /// </summary>
    private void OpWijziging(object? afzender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DashboardViewModel.WeerVersie))
        {
            return;
        }

        WeerVandaagView.Invalidate();
        WeerMorgenView.Invalidate();
    }
}
