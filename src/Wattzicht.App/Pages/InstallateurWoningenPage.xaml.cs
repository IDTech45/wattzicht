using Wattzicht.App.ViewModels;

namespace Wattzicht.App.Pages;

/// <summary>De woningenlijst van de installateur, met zoekveld.</summary>
public partial class InstallateurWoningenPage : ContentPage
{
    private readonly InstallateurViewModel _vm;

    public InstallateurWoningenPage(InstallateurViewModel vm)
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
