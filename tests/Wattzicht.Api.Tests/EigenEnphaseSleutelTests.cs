using Wattzicht.Api.Services;

namespace Wattzicht.Api.Tests;

/// <summary>
/// De bewoner mag zijn eigen Enphase-sleutel gebruiken.
///
/// Enphase rekent per developer-app en niet per woning. Met één gedeelde sleutel delen al onze
/// klanten samen duizend aanroepen per maand, en dat is precies waarom we een Enphase maar eens per
/// vijf uur uitlezen. Wie zelf een app aanmaakt heeft zijn eigen duizend.
///
/// Wat hier wordt vastgelegd is vooral de ondergrens: iets dat uit de verkeerde hoek van de pagina
/// is geplakt mag geen toestemmingspagina opleveren die bij Enphase stukloopt. Dan is onze eigen
/// sleutel een beter antwoord dan een kapotte link.
/// </summary>
public sealed class EigenEnphaseSleutelTests
{
    private const string Onze = "onze-gedeelde-client-id-0001";
    private const string Echte = "a1b2c3d4e5f60718293a4b5c6d7e8f90";

    [Fact]
    public void Een_echte_sleutel_van_de_bewoner_gaat_voor()
    {
        Assert.Equal(Echte, EnphaseAuthorize.Kies(Echte, Onze));
    }

    [Fact]
    public void Niets_invullen_betekent_onze_eigen_sleutel()
    {
        Assert.Equal(Onze, EnphaseAuthorize.Kies(null, Onze));
        Assert.Equal(Onze, EnphaseAuthorize.Kies("", Onze));
        Assert.Equal(Onze, EnphaseAuthorize.Kies("   ", Onze));
    }

    [Theory]
    // Te kort om een client-id te zijn, dus eerder een typefout dan een sleutel.
    [InlineData("abc123")]
    // Een stuk json, een URL of de secret met tekens erin: geplakt uit de verkeerde hoek.
    [InlineData("{\"client_id\":\"a1b2c3d4e5f60718293a4b5c6d7e8f90\"}")]
    [InlineData("https://developer-v4.enphaseenergy.com/apps/1234")]
    [InlineData("a1b2c3d4 e5f60718293a4b5c6d7e8f90")]
    public void Rommel_valt_terug_op_onze_sleutel(string geplakt)
    {
        Assert.False(EnphaseAuthorize.IsBruikbaar(geplakt));
        Assert.Equal(Onze, EnphaseAuthorize.Kies(geplakt, Onze));
    }

    [Fact]
    public void Zonder_sleutel_aan_beide_kanten_is_er_niets()
    {
        Assert.Null(EnphaseAuthorize.Kies(null, null));
        Assert.Null(EnphaseAuthorize.Kies("abc", "   "));
    }

    [Fact]
    public void Het_adres_bevat_de_sleutel_en_het_terugkeeradres()
    {
        var url = EnphaseAuthorize.Url(Echte, "https://wattzicht.nl/oauth/enphase");

        Assert.StartsWith("https://api.enphaseenergy.com/oauth/authorize?response_type=code", url);
        Assert.Contains("client_id=" + Echte, url);
        Assert.Contains("redirect_uri=https%3A%2F%2Fwattzicht.nl%2Foauth%2Fenphase", url);
    }
}
