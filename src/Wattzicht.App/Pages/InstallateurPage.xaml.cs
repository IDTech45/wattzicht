using Wattzicht.App.ViewModels;

namespace Wattzicht.App.Pages;

/// <summary>Het overzicht van de installateur: wat aandacht nodig heeft, en zijn tellers.</summary>
public partial class InstallateurPage : ContentPage
{
    private readonly InstallateurViewModel _vm;

    public InstallateurPage(InstallateurViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LaadAsync();
    }
}
