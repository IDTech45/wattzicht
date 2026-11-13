using Wattzicht.App.ViewModels;

namespace Wattzicht.App.Pages;

public partial class EnphaseSleutelPage : ContentPage
{
    public EnphaseSleutelPage(EnphaseSleutelViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
