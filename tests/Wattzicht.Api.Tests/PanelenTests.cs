using System.Text.Json;
using Wattzicht.Api.Services;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Je dak, paneel voor paneel.
///
/// De vraag die mensen over hun installatie hebben gaat niet over een totaal in kilowatt, maar over
/// de rij: doet er een minder dan de rest? Deze test legt vast wanneer we dat durven zeggen. Dat
/// luistert nauw in twee richtingen: te streng en de app zeurt elke winterochtend over het paneel
/// aan de rand van het dak, te laks en een paneel met een storing valt niemand op.
/// </summary>
public sealed class PanelenTests
{
    private static string Gateway(params (string Serial, double Watt, double Max)[] panelen) =>
        Panelen.UitGateway(JsonDocument.Parse(
            "[" + string.Join(",", panelen.Select(p =>
                $$"""{"serialNumber":"{{p.Serial}}","lastReportWatts":{{p.Watt}},"maxReportWatts":{{p.Max}},"lastReportDate":1763054400}"""))
            + "]").RootElement)!;

    [Fact]
    public void De_panelen_komen_binnen_met_de_beste_bovenaan()
    {
        var uit = Panelen.Lees(Gateway(("A", 180, 300), ("B", 240, 300), ("C", 210, 300)), DateTimeOffset.UnixEpoch);

        Assert.Equal(3, uit.Panelen.Count);
        Assert.Equal(240, uit.Panelen[0].Watt);
        Assert.Equal(1, uit.Panelen[0].Nummer);
        Assert.Equal(180, uit.Panelen[2].Watt);
        Assert.Equal(630, uit.TotaalWatt);
    }

    [Fact]
    public void Een_paneel_in_de_schaduw_valt_op()
    {
        // Negen panelen rond de 240 watt en een op 40: dat is geen weer, dat is schaduw, vuil of een
        // storing. Precies het geval waarvoor dit scherm bestaat.
        var rij = Enumerable.Range(1, 9).Select(i => ($"A{i}", 240.0, 300.0)).Append(("B1", 40, 300)).ToArray();

        var uit = Panelen.Lees(Gateway(rij), DateTimeOffset.UnixEpoch);

        Assert.Equal(1, uit.Achterblijvers);
        Assert.True(uit.Panelen.Last().BlijftAchter);
        Assert.False(uit.Panelen.First().BlijftAchter);
    }

    [Fact]
    public void Een_rij_die_gewoon_verschilt_is_geen_storing()
    {
        // Panelen op oost en west lopen uit elkaar; dat is een dak en geen probleem. Boven de
        // drempel zegt de app er niets over.
        var uit = Panelen.Lees(
            Gateway(("A", 240, 300), ("B", 230, 300), ("C", 200, 300), ("D", 185, 300)),
            DateTimeOffset.UnixEpoch);

        Assert.Equal(0, uit.Achterblijvers);
    }

    [Fact]
    public void In_de_schemering_vergelijken_we_niets()
    {
        // Alles bij elkaar twintig watt: dan is het verschil tussen 2 en 8 watt ruis en geen oordeel.
        var uit = Panelen.Lees(Gateway(("A", 8, 300), ("B", 7, 300), ("C", 3, 300), ("D", 2, 300)), null);

        Assert.Equal(0, uit.Achterblijvers);
    }

    [Fact]
    public void Een_gemiddelde_zou_het_juist_missen()
    {
        // Twee van de vier op nul trekt een gemiddelde zo ver omlaag dat de andere twee er "goed"
        // uitzien. Daarom de middelste waarde; die blijft bij de werkende panelen.
        var uit = Panelen.Lees(Gateway(("A", 250, 300), ("B", 245, 300), ("C", 0, 300), ("D", 0, 300)), null);

        Assert.Equal(2, uit.Achterblijvers);
    }

    [Fact]
    public void Rommel_levert_een_leeg_scherm_op_en_geen_crash()
    {
        Assert.Empty(Panelen.Lees(null, null).Panelen);
        Assert.Empty(Panelen.Lees("", null).Panelen);
        Assert.Empty(Panelen.Lees("geen json", null).Panelen);
        Assert.Empty(Panelen.Lees("[]", null).Panelen);
    }

    [Fact]
    public void Een_paneel_zonder_serienummer_slaan_we_over()
    {
        var json = Panelen.UitGateway(JsonDocument.Parse(
            """[{"lastReportWatts":200},{"serialNumber":"A","lastReportWatts":210,"maxReportWatts":300}]""").RootElement);

        var uit = Panelen.Lees(json, null);

        Assert.Single(uit.Panelen);
        Assert.Equal("A", uit.Panelen[0].Serial);
    }

    [Fact]
    public void Iets_dat_geen_lijst_is_bewaren_we_niet()
    {
        Assert.Null(Panelen.UitGateway(JsonDocument.Parse("""{"production":[]}""").RootElement));
    }
}
