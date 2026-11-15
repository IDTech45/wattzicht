using Wattzicht.App.ViewModels;

namespace Wattzicht.App.Pages;

/// <summary>Je maandopbrengsten zelf invullen, voor wie geen export bij zijn fabrikant kan krijgen.</summary>
public partial class MaandOpgavePage : ContentPage
{
    private readonly MaandOpgaveViewModel _viewModel;

    public MaandOpgavePage(MaandOpgaveViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_viewModel.Geladen) await _viewModel.LaadAsync();
    }
}
