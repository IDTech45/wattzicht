using Wattzicht.Api.Connectors;

namespace Wattzicht.Api.Tests;

/// <summary>
/// De rem op het Enphase-quotum.
///
/// Enphase rekent per developer-app en niet per woning: het gratis plan geeft 1.000 aanroepen per
/// maand voor alles bij elkaar, en één uitlezing kost er twee. Zonder rem gaat dat quotum ergens
/// halverwege de maand stil over de grens, en krijgt iedereen tegelijk foutmeldingen die nergens op
/// slaan. Wie zijn eigen sleutels opgeeft heeft zijn eigen 1.000 en zit niemand in de weg; daarom
/// telt de rem per sleutel en niet per platform.
/// </summary>
public sealed class EnphaseQuotumTests : IDisposable
{
    private static readonly DateTimeOffset Maart = new(2027, 3, 10, 12, 0, 0, TimeSpan.Zero);

    public EnphaseQuotumTests() => EnphaseQuotum.Reset();
    public void Dispose() => EnphaseQuotum.Reset();

    [Fact]
    public void Tot_aan_de_grens_mag_het_en_daarna_niet_meer()
    {
        Assert.True(EnphaseQuotum.Boek("app-a", max: 3, Maart));
        Assert.True(EnphaseQuotum.Boek("app-a", max: 3, Maart));
        Assert.True(EnphaseQuotum.Boek("app-a", max: 3, Maart));

        Assert.False(EnphaseQuotum.Boek("app-a", max: 3, Maart));
        Assert.False(EnphaseQuotum.Boek("app-a", max: 3, Maart));
    }

    [Fact]
    public void Een_eigen_sleutel_heeft_zijn_eigen_ruimte()
    {
        // Dit is het hele punt van zelf een developer-app aanmaken: de ene klant kan de andere niet
        // meer buitensluiten.
        Assert.False(Vol("gedeeld", 1));
        Assert.True(EnphaseQuotum.Boek("eigen-van-de-klant", max: 1, Maart));
    }

    [Fact]
    public void Een_nieuwe_maand_begint_weer_op_nul()
    {
        Assert.False(Vol("app-b", 2));

        var april = Maart.AddMonths(1);
        Assert.True(EnphaseQuotum.Boek("app-b", max: 2, april));
    }

    [Fact]
    public void Dezelfde_maand_een_jaar_later_telt_niet_mee_als_dezelfde_maand()
    {
        // Met alleen het maandnummer als sleutel zou maart 2028 doorgaan op de stand van maart 2027.
        Assert.False(Vol("app-c", 2));

        Assert.True(EnphaseQuotum.Boek("app-c", max: 2, Maart.AddYears(1)));
    }

    /// <summary>Boekt tot de grens en geeft terug wat de volgende poging oplevert.</summary>
    private static bool Vol(string sleutel, int max)
    {
        for (var i = 0; i < max; i++) EnphaseQuotum.Boek(sleutel, max, Maart);
        return EnphaseQuotum.Boek(sleutel, max, Maart);
    }
}
