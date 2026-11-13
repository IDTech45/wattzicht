namespace Wattzicht.App.Pages;

/// <summary>
/// Het scherm waarmee de app begint: het oog knippert, dan ben je binnen.
///
/// Waarom dit bestaat: het startscherm van het systeem is een stilstaand plaatje en daarna stond je
/// ineens in de app. Dat voelt als een sprong. Een knipper van een seconde maakt er een begin van,
/// en het is ook precies de seconde waarin we toch je token verversen en uitzoeken waar je heen
/// moet. Het kost dus niets aan tijd die je niet al kwijt was.
///
/// De animatie wacht nergens op: komt het antwoord eerder, dan maakt de knipper zich af en gaan we
/// door. Duurt het langer (slecht netwerk), dan blijft het logo staan in plaats van dat er een leeg
/// scherm valt.
/// </summary>
public partial class StartPage : ContentPage
{
    /// <summary>Wordt gezet door degene die ons hierheen stuurde; wij roepen het aan als de knipper klaar is.</summary>
    public static Func<Task>? Daarna { get; set; }

    private bool _gedraaid;

    public StartPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Eén keer. Een pagina kan vaker verschijnen (terug uit de achtergrond) en dan hoort de app
        // niet opnieuw te beginnen met knipperen.
        if (_gedraaid) return;
        _gedraaid = true;

        try
        {
            await KnipperAsync();
        }
        catch (Exception)
        {
            // Een animatie die struikelt mag de app niet tegenhouden.
        }

        var daarna = Daarna;
        Daarna = null;

        if (daarna is not null)
        {
            await daarna();
        }
    }

    /// <summary>
    /// Opkomen, twee keer knipperen, en de naam erbij.
    ///
    /// Knipperen is het oog platdrukken en weer openen: de hoogte naar bijna nul en terug. Dat leest
    /// als een ooglid, terwijl een fade alleen maar flikkert.
    /// </summary>
    private async Task KnipperAsync()
    {
        Logo.AnchorY = 0.5;

        await Task.WhenAll(
            Logo.FadeTo(1, 220, Easing.CubicOut),
            Logo.ScaleTo(1.04, 220, Easing.CubicOut));

        await Knip(110);
        await Task.Delay(90);
        await Knip(90);

        await Task.WhenAll(
            Naam.FadeTo(1, 200, Easing.CubicOut),
            Logo.ScaleTo(1, 200, Easing.CubicOut));

        await Task.Delay(220);
    }

    private async Task Knip(uint duur)
    {
        await Logo.ScaleYTo(0.06, duur / 2, Easing.CubicIn);
        await Logo.ScaleYTo(1, duur, Easing.CubicOut);
    }
}
