using Wattzicht.Shared;
using Xunit;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Wat er onder de staven staat, en wat de grafiek je vertelt.
///
/// De aanleiding staat in de klacht van de eerste gebruiker op 19-11-2026: onder de grafiek stond
/// "1 4 7 10 13" en daarboven een rij blokjes, en zijn vraag was of dat dagen waren, hoeveel één
/// staaf dan is, en wat het scherm hem eigenlijk vertelde. Alle drie terecht.
///
/// Het scherpste geval staat onderaan: een week waarin wij twee dagen niet gemeten hebben. Dat is
/// iets anders dan een week waarin de zon niet scheen, en dat verschil hoort de bewoner te lezen in
/// plaats van zelf te moeten raden.
/// </summary>
public class GrafiekTekstTests
{
    private static DateOnly D(int dag, int maand = 11, int jaar = 2026) => new(jaar, maand, dag);

    private static GrafiekDag Dag(int dag, double waarde) => new(D(dag), waarde);

    [Fact]
    public void Bij_een_week_staat_de_dagnaam_onder_de_staaf()
    {
        // 16 november 2026 is een maandag.
        Assert.Equal("ma", GrafiekTekst.StaafLabel(D(16), GrafiekPeriode.Week, false));
        Assert.Equal("wo", GrafiekTekst.StaafLabel(D(18), GrafiekPeriode.Week, false));
    }

    /// <summary>
    /// In een maand passen dertig dagnamen niet, dus daar staat het dagnummer. De eerste en de
    /// laatste krijgen de maand erbij: anders blijft het een rij losse getallen, en dat was precies
    /// de klacht.
    /// </summary>
    [Fact]
    public void Bij_een_maand_krijgen_de_randen_de_maand_erbij()
    {
        Assert.Equal("12", GrafiekTekst.StaafLabel(D(12), GrafiekPeriode.Maand, false));
        Assert.Equal("12 nov", GrafiekTekst.StaafLabel(D(12), GrafiekPeriode.Maand, eersteOfLaatste: true));
    }

    /// <summary>De eerste van de maand krijgt de maandnaam ook midden in een reeks van veertien
    /// dagen: zonder dat telt 30, 31, 1, 2 nergens naartoe.</summary>
    [Fact]
    public void De_eerste_van_de_maand_noemt_altijd_de_maand()
    {
        Assert.Equal("1 dec", GrafiekTekst.StaafLabel(D(1, 12), GrafiekPeriode.Dag, false));
    }

    [Fact]
    public void Bij_een_jaar_staan_de_maanden_eronder()
    {
        Assert.Equal("nov", GrafiekTekst.StaafLabel(D(1), GrafiekPeriode.Jaar, false));
    }

    [Fact]
    public void De_volle_datum_is_de_datum_zoals_je_hem_uitspreekt()
    {
        Assert.Equal("woensdag 18 november", GrafiekTekst.VolleDatum(D(18), GrafiekPeriode.Week));
        Assert.Equal("november 2026", GrafiekTekst.VolleDatum(D(1), GrafiekPeriode.Jaar));
    }

    [Fact]
    public void Ontbrekende_dagen_zijn_de_dagen_zonder_meting()
    {
        var mist = GrafiekTekst.OntbrekendeDagen(D(16), D(19), [Dag(16, 3), Dag(19, 0)]);

        Assert.Equal([D(17), D(18)], mist);
    }

    /// <summary>
    /// Het geval van 19-11-2026: de week begon maandag, wij hebben op maandag en dinsdag niets
    /// gemeten, en woensdagochtend staat de teller nog op nul. Dan hoort er niet "€ 0,00" te staan
    /// met een lege grafiek eronder, maar waarom er niets staat.
    /// </summary>
    [Fact]
    public void Een_lege_week_noemt_de_dagen_die_ontbreken()
    {
        var zin = GrafiekTekst.Samenvatting(
            GrafiekPeriode.Week, [Dag(19, 0)], D(16), D(19), inKwh: true);

        Assert.Contains("17 november", zin);
        Assert.Contains("18 november", zin);
        Assert.DoesNotContain("0 kWh", zin);
    }

    /// <summary>
    /// Zonder gaten is een lege periode gewoon een lege periode: dan ligt het aan de zon en niet aan
    /// ons, en moet er geen verwijt naar onszelf staan dat nergens op slaat.
    /// </summary>
    [Fact]
    public void Een_complete_maar_lege_periode_wijst_niet_naar_ons()
    {
        var zin = GrafiekTekst.Samenvatting(
            GrafiekPeriode.Week, [Dag(16, 0), Dag(17, 0), Dag(18, 0), Dag(19, 0)], D(16), D(19), inKwh: true);

        Assert.Equal("Deze week is er nog niets gemeten.", zin);
    }

    [Fact]
    public void Met_cijfers_staat_er_een_totaal_en_de_beste_dag()
    {
        var zin = GrafiekTekst.Samenvatting(
            GrafiekPeriode.Maand,
            [Dag(1, 2.7), Dag(2, 3.8), Dag(5, 16.5)],
            D(1), D(5), inKwh: true);

        Assert.Contains("23 kWh", zin);      // 2,7 + 3,8 + 16,5
        Assert.Contains("16,5 kWh", zin);
        Assert.Contains("5 november", zin);
    }

    /// <summary>In euro's hoort er een euroteken en twee decimalen te staan, niet dezelfde notatie
    /// als bij kilowatturen: "€ 1,2" leest als een afgekapt bedrag.</summary>
    [Fact]
    public void In_euros_staat_er_een_bedrag()
    {
        var zin = GrafiekTekst.Samenvatting(
            GrafiekPeriode.Week, [Dag(16, 1.2), Dag(17, 0.8), Dag(18, 0), Dag(19, 0)],
            D(16), D(19), inKwh: false);

        Assert.Contains("€ 2,00", zin);
        Assert.Contains("€ 1,20", zin);
    }

    /// <summary>
    /// Meer dan drie ontbrekende dagen worden een aantal. Zeven datums achter elkaar is een
    /// opsomming waar niemand doorheen leest, en het getal zegt hetzelfde.
    /// </summary>
    [Fact]
    public void Veel_gaten_worden_een_aantal()
    {
        var zin = GrafiekTekst.Samenvatting(
            GrafiekPeriode.Maand, [Dag(20, 5)], D(14), D(20), inKwh: true);

        Assert.Contains("6 dagen", zin);
    }

    /// <summary>De zin volgt de taal van de app, net als de rest van het scherm.</summary>
    [Theory]
    [InlineData("en", "this week")]
    [InlineData("tr", "Bu hafta")]
    public void De_zin_volgt_de_taal(string taal, string stuk)
    {
        var zin = GrafiekTekst.Samenvatting(
            GrafiekPeriode.Week, [Dag(16, 4), Dag(17, 2), Dag(18, 1), Dag(19, 0)],
            D(16), D(19), inKwh: true, taal: taal);

        Assert.Contains(stuk, zin);
    }
}
