using Wattzicht.App.ViewModels;

namespace Wattzicht.App.Pages;

public partial class LoginPage : ContentPage
{
    private readonly LoginViewModel _viewModel;

    public LoginPage(LoginViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        _viewModel = viewModel;
    }

    /// <summary>
    /// Bij het openen vragen welke inlogknoppen er mogen staan. Niet in de constructor: dan zou het
    /// scherm wachten op het netwerk voordat er iets te zien is.
    /// </summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LaadProvidersAsync();
    }

    // Geen terug-navigatie vanaf het loginscherm (Android back-knop).
    protected override bool OnBackButtonPressed() => true;
}
