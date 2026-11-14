using Microsoft.Extensions.Time.Testing;
using Wattzicht.Api.Services;
using Xunit;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Enphase koppelen vanuit de app.
///
/// Enphase keert alleen terug op een webadres dat in hun developer-app staat, en een app kan daar
/// niet op wachten. Daarom kon de cloudkoppeling alleen op de website: iemand met zijn telefoon in
/// de hand moest eerst ergens anders inloggen om iets te doen wat in de app thuishoort.
///
/// Het kaartje lost dat op. De app vraagt er een aan, het gaat als state mee naar Enphase en komt
/// ongewijzigd terug op onze pagina; daaraan zien wij bij welke woning de toestemming hoort. Het is
/// geen sleutel tot een woning maar een afspraak voor één handeling, en dat is wat hieronder
/// vastligt: eenmalig, kort geldig, en niet te raden.
/// </summary>
public class EnphaseKoppelsessieTests
{
    private static readonly Guid Woning = Guid.NewGuid();
    private const string Terug = "https://wattzicht.nl/oauth/enphase";

    private static (EnphaseKoppelsessie Sessies, FakeTimeProvider Klok) Maak()
    {
        var klok = new FakeTimeProvider(new DateTimeOffset(2026, 11, 15, 1, 0, 0, TimeSpan.Zero));
        return (new EnphaseKoppelsessie(klok), klok);
    }

    [Fact]
    public void Een_kaartje_wijst_naar_de_woning_die_hem_aanvroeg()
    {
        var (sessies, _) = Maak();

        var kaartje = sessies.Maak(Woning, Terug, null);
        var uit = sessies.Verzilver(kaartje);

        Assert.NotNull(uit);
        Assert.Equal(Woning, uit!.Value.Woning);
        Assert.Equal(Terug, uit.Value.RedirectUri);
    }

    [Fact]
    public void Een_kaartje_werkt_maar_een_keer()
    {
        // Anders kan wie meekijkt op de terugkeerpagina de koppeling later nog eens overdoen.
        var (sessies, _) = Maak();
        var kaartje = sessies.Maak(Woning, Terug, null);

        Assert.NotNull(sessies.Verzilver(kaartje));
        Assert.Null(sessies.Verzilver(kaartje));
    }

    [Fact]
    public void Na_tien_minuten_vervalt_hij()
    {
        var (sessies, klok) = Maak();
        var kaartje = sessies.Maak(Woning, Terug, null);

        klok.Advance(EnphaseKoppelsessie.Geldig + TimeSpan.FromSeconds(1));

        Assert.Null(sessies.Verzilver(kaartje));
    }

    [Fact]
    public void Een_verzonnen_kaartje_levert_niets_op()
    {
        var (sessies, _) = Maak();
        sessies.Maak(Woning, Terug, null);

        Assert.Null(sessies.Verzilver("ABCDEF"));
        Assert.Null(sessies.Verzilver(""));
        Assert.Null(sessies.Verzilver(null));
    }

    [Fact]
    public void Twee_kaartjes_lijken_niet_op_elkaar()
    {
        // Te raden kaartjes zouden betekenen dat iemand zijn Enphase aan een vreemde woning kan hangen.
        var (sessies, _) = Maak();

        var een = sessies.Maak(Woning, Terug, null);
        var twee = sessies.Maak(Woning, Terug, null);

        Assert.NotEqual(een, twee);
        Assert.True(een.Length >= 32);
    }

    [Fact]
    public void De_eigen_sleutel_van_de_bewoner_gaat_mee()
    {
        // Wie zelf een developer-app heeft, koppelt daarmee en niet met de onze; dat moet de
        // terugkeerpagina straks ook gebruiken, anders wisselt hij de code in bij de verkeerde app.
        var (sessies, _) = Maak();

        var kaartje = sessies.Maak(Woning, Terug, "a1b2c3d4e5f60718293a4b5c6d7e8f90");

        Assert.Equal("a1b2c3d4e5f60718293a4b5c6d7e8f90", sessies.Verzilver(kaartje)!.Value.ClientId);
    }
}
