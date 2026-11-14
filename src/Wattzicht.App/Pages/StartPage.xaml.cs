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
        Logo.Scale = 0.88;

        // Opkomen duurt even: een logo dat er in tweehonderd milliseconden staat voelt als een
        // plaatje dat aanknipt, niet als iets dat wakker wordt.
        await Task.WhenAll(
            Logo.FadeTo(1, 420, Easing.CubicOut),
            Logo.ScaleTo(1, 420, Easing.CubicOut));

        await Task.Delay(260);

        // Een echte knipper gaat snel dicht en langzamer open, met een korte rust ertussen. Twee
        // keer, want één keer leest als een storing in het beeld.
        await Knip(dicht: 130, rust: 70, open: 240);
        await Task.Delay(380);
        await Knip(dicht: 110, rust: 50, open: 200);

        await Naam.FadeTo(1, 320, Easing.CubicOut);
        await Task.Delay(420);
    }

    private async Task Knip(uint dicht, int rust, uint open)
    {
        await Logo.ScaleYTo(0.05, dicht, Easing.CubicIn);
        await Task.Delay(rust);
        await Logo.ScaleYTo(1, open, Easing.CubicOut);
    }
}
