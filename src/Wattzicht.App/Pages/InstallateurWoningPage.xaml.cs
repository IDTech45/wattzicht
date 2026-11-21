using Wattzicht.App.ViewModels;

namespace Wattzicht.App.Pages;

/// <summary>Eén woning van de installateur: meldingen, apparaten en opbrengst.</summary>
public partial class InstallateurWoningPage : ContentPage
{
    public InstallateurWoningPage(InstallateurWoningViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
