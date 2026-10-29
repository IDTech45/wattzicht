using Wattzicht.App.ViewModels;

namespace Wattzicht.App.Pages;

public partial class LoginPage : ContentPage
{
    public LoginPage(LoginViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    // Geen terug-navigatie vanaf het loginscherm (Android back-knop).
    protected override bool OnBackButtonPressed() => true;
}
