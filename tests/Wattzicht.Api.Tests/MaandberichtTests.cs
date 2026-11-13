using Wattzicht.Api.Services;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Het maandbericht.
///
/// Een app die alleen iets zegt als er iets mis is, wordt een app die je alleen opent als er iets
/// mis is. Dit is het tegenovergestelde, en dan moet er wel iets in staan waar je iets aan hebt:
/// wat je maakte, wat je ervan zelf gebruikte, en wat het scheelde.
/// </summary>
public sealed class MaandberichtTests
{
    private static readonly DateOnly Oktober = new(2026, 10, 1);

    [Fact]
    public void Het_bericht_noemt_de_maand_en_de_drie_getallen()
    {
        var (titel, tekst) = MaandberichtWorker.Bericht(Oktober, 312.4, 180.2, 46m, null, "nl");

        Assert.Contains("oktober", titel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("312", tekst);
        Assert.Contains("180", tekst);
        Assert.Contains("46", tekst);
    }

    [Fact]
    public void Weten_we_wat_de_leverancier_zegt_dan_staat_dat_erbij()
    {
        // Het enige getal in de app dat van buiten komt, en dus het enige dat je kunt narekenen.
        var (_, tekst) = MaandberichtWorker.Bericht(Oktober, 312.4, 180.2, 46m, 230, "nl");

        Assert.Contains("230", tekst);
        Assert.Contains("leverancier", tekst);
    }

    [Fact]
    public void Zonder_die_cijfers_blijft_het_bericht_gewoon_kloppen()
    {
        var (_, tekst) = MaandberichtWorker.Bericht(Oktober, 312.4, 180.2, 46m, null, "nl");

        Assert.DoesNotContain("leverancier", tekst);
    }

    [Theory]
    [InlineData("en", "October")]
    [InlineData("tr", "Ekim")]
    public void De_maandnaam_volgt_de_taal(string taal, string verwacht)
    {
        var (titel, _) = MaandberichtWorker.Bericht(Oktober, 312.4, 180.2, 46m, null, taal);

        Assert.Contains(verwacht, titel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Op_de_tweede_van_de_maand_en_niet_s_nachts()
    {
        // De eerste is de dag waarop de vorige maand nog aan het afronden is: een meting van 23:55
        // komt soms pas daarna binnen.
        Assert.Equal(2, MaandberichtWorker.OpDag);
        Assert.True(MaandberichtWorker.VanafUur >= 9);
        Assert.True(MaandberichtWorker.TotUur <= 21);
    }
}
