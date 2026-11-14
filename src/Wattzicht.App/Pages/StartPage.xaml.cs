namespace Wattzicht.App.Pages;

/// <summary>
/// Het scherm waarmee de app begint: het oog knippert, dan ben je binnen.
///
/// Waarom dit bestaat: het startscherm van het systeem is een stilstaand plaatje en daarna stond je
/// ineens in de app. Dat voelt als een sprong. Een knipper van een seconde maakt er een begin van,
/// en het is ook precies de seconde waarin we toch je token verversen en uitzoeken waar je heen
/// moet. Het kost dus niets aan tijd die je niet al kwijt was.
///
/// En hij blijft staan zolang het duurt. Eerst knipperen, en daarna rustig ademen tot het dashboard
/// er werkelijk is: zijn cijfers worden hier al opgehaald terwijl dit scherm nog voor staat. Zonder
/// dat kwam je op een dashboard dat zelf nog moest gaan laden, met een lege navigatiebalk en knoppen
/// die nog nergens heen gingen. Nu is er één moment waarop de app begint en één waarop hij klaar is.
///
/// Duurt het langer dan een paar tellen, dan komt er een regel bij die zegt dat hij aan het opstarten
/// is. Dat is eerlijker dan een logo dat zonder uitleg blijft staan.
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

        if (daarna is null) return;

        // Vanaf hier loopt het werk en loopt de animatie door. De twee lussen stoppen zodra het werk
        // klaar is; ze draaien dus precies zolang als het opstarten duurt en geen seconde langer.
        using var klaar = new CancellationTokenSource();
        var ademen = AdemAsync(klaar.Token);
        var wachtregel = WachtregelAsync(klaar.Token);

        try
        {
            await daarna();
        }
        finally
        {
            klaar.Cancel();
            try { await Task.WhenAll(ademen, wachtregel); } catch (Exception) { /* al weg */ }
        }
    }

    /// <summary>
    /// Zacht in en uit ademen zolang het opstarten duurt.
    ///
    /// Niet opnieuw knipperen: dat trekt de aandacht en vraagt erom gelezen te worden als een
    /// nieuwe gebeurtenis. Ademen zegt alleen dat er nog iets gebeurt.
    /// </summary>
    private async Task AdemAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Logo.ScaleTo(0.94, 900, Easing.SinInOut);
                if (ct.IsCancellationRequested) break;
                await Logo.ScaleTo(1.0, 900, Easing.SinInOut);
            }

            await Logo.ScaleTo(1.0, 150, Easing.SinOut);
        }
        catch (Exception)
        {
            // Het scherm kan al weg zijn; dan valt er niets meer te animeren.
        }
    }

    /// <summary>Na een paar tellen zeggen dat hij aan het opstarten is.</summary>
    private async Task WachtregelAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(1600, ct);
            Wachtregel.Text = Services.Taal.T("start.opstarten");
            await Wachtregel.FadeTo(0.75, 300, Easing.CubicOut);
        }
        catch (OperationCanceledException)
        {
            // Binnen een paar tellen klaar: dan hoeft er niets te staan.
        }
        catch (Exception)
        {
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

        // Geen opkomst meer. Het opstartscherm van Android liet ditzelfde logo al zien, dus een
        // fade vanaf nul betekende: logo zien, logo weg, logo weer terug. Nu staat het er gewoon en
        // knippert het, alsof het scherm van het systeem het onze is.
        await Task.Delay(180);

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
