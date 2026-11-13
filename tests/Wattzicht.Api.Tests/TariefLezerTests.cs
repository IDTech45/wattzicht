using Wattzicht.Api.Services;

namespace Wattzicht.Api.Tests;

/// <summary>
/// De tarieven uit een energiecontract halen.
///
/// Elke leverancier schrijft zijn afrekening anders, dus staat hier per schrijfwijze een regel:
/// euro's of centen, komma of punt, "per kWh" of "€/kWh", dal en piek naast elkaar.
///
/// En belangrijker dan welk getal we vinden: welk getal we níét mogen verwarren. Een
/// terugleververgoeding die als leveringsprijs wordt overgenomen maakt elke besparing op elk scherm
/// verkeerd, en niemand ziet waar het vandaan komt. Daarom claimen de teruglever-regels zichzelf
/// eerst, en staat dat hier vast.
/// </summary>
public sealed class TariefLezerTests
{
    [Fact]
    public void Een_gewone_afrekening_met_euros_per_kwh()
    {
        var tekst = """
            Overzicht van je tarieven
            Leveringstarief elektriciteit € 0,27 per kWh
            Terugleververgoeding € 0,05 per kWh
            Vastrecht € 6,50 per maand
            """;

        var uit = TariefLezer.Lees(tekst);

        Assert.Equal(0.27m, uit.Levering!.EuroPerKwh);
        Assert.Equal(0.05m, uit.Teruglevering!.EuroPerKwh);
        Assert.Contains("Leveringstarief", uit.Levering.Regel);
    }

    [Fact]
    public void Centen_worden_euros()
    {
        var tekst = """
            Stroomprijs 27,4 cent per kWh
            Terugleververgoeding 5 cent per kWh
            """;

        var uit = TariefLezer.Lees(tekst);

        Assert.Equal(0.274m, uit.Levering!.EuroPerKwh);
        Assert.Equal(0.05m, uit.Teruglevering!.EuroPerKwh);
    }

    [Fact]
    public void Een_prijs_zonder_eenheid_die_als_euro_nergens_op_slaat_is_een_cent()
    {
        // "Leveringstarief 27,40" op een regel zonder het woord cent: niemand betaalt 27 euro per
        // kilowattuur, dus dat zijn centen.
        var uit = TariefLezer.Lees("Leveringstarief 27,40");

        Assert.Equal(0.274m, uit.Levering!.EuroPerKwh);
    }

    [Fact]
    public void Terugleverkosten_staan_apart_van_de_vergoeding()
    {
        var tekst = """
            Terugleververgoeding € 0,04 per kWh
            Terugleverkosten € 0,11 per teruggeleverde kWh
            """;

        var uit = TariefLezer.Lees(tekst);

        Assert.Equal(0.04m, uit.Teruglevering!.EuroPerKwh);
        Assert.Equal(0.11m, uit.Terugleverkosten!.EuroPerKwh);
    }

    [Fact]
    public void Een_terugleververgoeding_wordt_nooit_de_leveringsprijs()
    {
        // Dit is de fout met de grootste gevolgen: 0,05 als leveringsprijs maakt elke besparing op
        // elk scherm verkeerd, en op het scherm zie je alleen een getal dat klopt had kunnen zijn.
        var uit = TariefLezer.Lees("Vergoeding voor teruglevering van stroom: € 0,05 per kWh");

        Assert.Null(uit.Levering);
        Assert.Equal(0.05m, uit.Teruglevering!.EuroPerKwh);
    }

    [Fact]
    public void Dal_en_piek_naast_elkaar_worden_het_gemiddelde()
    {
        // Wij weten niet wanneer dit huis zijn stroom gebruikt, dus is het gemiddelde eerlijker dan
        // een van de twee uitkiezen.
        var tekst = """
            Normaaltarief € 0,30 per kWh
            Daltarief € 0,26 per kWh
            """;

        var uit = TariefLezer.Lees(tekst);

        Assert.Equal(0.28m, uit.Levering!.EuroPerKwh);
        Assert.Contains("Normaaltarief", uit.Levering.Regel);
        Assert.Contains("Daltarief", uit.Levering.Regel);
    }

    [Fact]
    public void Energiebelasting_wordt_apart_gemeld()
    {
        // Staat de belasting los, dan is de leveringsprijs niet wat iemand per kilowattuur betaalt.
        // Dat moet de app kunnen zeggen, dus geven we het apart terug in plaats van stilletjes op
        // te tellen.
        var tekst = """
            Leveringstarief € 0,12 per kWh
            Energiebelasting € 0,13 per kWh
            """;

        var uit = TariefLezer.Lees(tekst);

        Assert.Equal(0.12m, uit.Levering!.EuroPerKwh);
        Assert.Equal(0.13m, uit.Energiebelasting!.EuroPerKwh);
        Assert.False(uit.PrijsIsAllIn);
    }

    [Fact]
    public void Een_all_in_regel_wordt_als_compleet_herkend()
    {
        var uit = TariefLezer.Lees("Leveringstarief inclusief energiebelasting en btw: € 0,29 per kWh");

        Assert.Equal(0.29m, uit.Levering!.EuroPerKwh);
        Assert.True(uit.PrijsIsAllIn);
    }

    [Fact]
    public void Jaartallen_en_kilowatturen_zijn_geen_prijzen()
    {
        var tekst = """
            Jaarafrekening 2026
            Verbruik 3.450 kWh
            Leveringstarief € 0,28 per kWh
            """;

        var uit = TariefLezer.Lees(tekst);

        Assert.Equal(0.28m, uit.Levering!.EuroPerKwh);
    }

    [Fact]
    public void Een_document_zonder_tarieven_levert_niets_op()
    {
        var uit = TariefLezer.Lees("Beste meneer Dogan, hierbij uw jaaroverzicht. Met vriendelijke groet.");

        Assert.False(uit.HeeftIets);
        Assert.Null(uit.Levering);
    }

    [Fact]
    public void Leeg_of_rommel_is_geen_crash()
    {
        Assert.False(TariefLezer.Lees("").HeeftIets);
        Assert.False(TariefLezer.Lees("    \n\n ").HeeftIets);
        Assert.False(TariefLezer.Lees("%PDF-1.4  binaire rommel").HeeftIets);
    }

    [Theory]
    [InlineData("€ 0,2735/kWh", 0.2735)]
    [InlineData("0.2735 EUR per kWh", 0.2735)]
    [InlineData("prijs per kWh 0,31", 0.31)]
    public void Verschillende_schrijfwijzen_van_hetzelfde_bedrag(string regel, double verwacht)
    {
        Assert.Equal((decimal)verwacht, TariefLezer.Bedrag(regel));
    }
}
