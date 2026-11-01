using Wattzicht.Api.Services;

namespace Wattzicht.Api.Tests;

/// <summary>
/// De abonnementskeuze van een bewoner. Dit legt alleen vast wat iemand koos; er wordt niets
/// afgeschreven zolang er geen betaalpartij is aangesloten.
///
/// Waarom het toch getoetst wordt: op het beheerpaneel staan drie tellingen per plan naast elkaar. Als
/// een onbekende waarde blijft staan zoals hij binnenkomt, tellen die drie niet op tot het aantal
/// klanten en ga je zoeken naar een fout die er niet is.
/// </summary>
public sealed class PlansTests
{
    [Theory]
    [InlineData("installer", Plans.ViaInstaller)]
    [InlineData("monthly", Plans.Monthly)]
    [InlineData("yearly", Plans.Yearly)]
    [InlineData("YEARLY", Plans.Yearly)]
    [InlineData("  monthly  ", Plans.Monthly)]
    public void Een_geldige_keuze_blijft_staan(string ingevoerd, string verwacht)
    {
        Assert.Equal(verwacht, Plans.Normaliseer(ingevoerd));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("gratis")]
    [InlineData("jaarabonnement")]
    public void Onbekend_of_leeg_wordt_via_de_installateur(string? ingevoerd)
    {
        // De veilige kant: via de installateur betaalt de bewoner niets. Een onbekende waarde mag nooit
        // stilletjes een betaald plan worden.
        Assert.Equal(Plans.ViaInstaller, Plans.Normaliseer(ingevoerd));
    }

    [Fact]
    public void De_drie_plannen_staan_vast()
    {
        // Het beheerpaneel telt per plan; komt er een vierde bij, dan moet die telling mee.
        Assert.Equal([Plans.ViaInstaller, Plans.Monthly, Plans.Yearly], Plans.Alle);
    }
}
