using Wattzicht.Api.Services;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Het zonnige blok van een dag, zoals de melding van de avond ervoor het gebruikt.
///
/// Het verschil met ZonVensterTests: die gaat over het advies, dat altijd iets moet kunnen zeggen en
/// daarom terugvalt op de middaguren. Dit gaat over een melding, en die mag alleen uit als er echt
/// iets te melden valt. Dus staat hier vooral wanneer het antwoord niets is: een grijze dag, een
/// opening in de bewolking van één uur, of een dak dat te klein is om er iets mee te laden.
/// </summary>
public sealed class ZonnigBlokTests
{
    private static readonly DateOnly Morgen = new(2026, 6, 15);

    /// <summary>Straling per uur voor één dag; de lijst is per lokaal uur (zomertijd, UTC+2).</summary>
    private static List<HourlyIrradiance> Dag(params (int Uur, double Watt)[] uren) =>
        uren.Select(u => new HourlyIrradiance(
                DutchTime.StartOfLocalDayUtc(Morgen).AddHours(u.Uur), u.Watt))
            .ToList();

    [Fact]
    public void Een_zonnige_middag_geeft_het_blok_rond_de_piek()
    {
        var blok = ZonnigBlok.VoorDag(Dag(
            (7, 80), (8, 180), (9, 320), (10, 520),
            (11, 700), (12, 800), (13, 780), (14, 600),
            (15, 380), (16, 200), (17, 90)), Morgen, pvPiekKw: 4);

        Assert.NotNull(blok);
        Assert.InRange(blok!.VanUur, 10, 11);
        Assert.InRange(blok.TotUur, 14, 15);
        Assert.True(blok.VerwachtKwh > ZonnigBlok.MinimumKwh);
    }

    [Fact]
    public void Een_grijze_dag_geeft_geen_blok()
    {
        // Een dag die wel een "zonnigste" uur heeft, maar waarop er niets te laden valt. Op zo'n dag
        // hoort de telefoon stil te blijven.
        var blok = ZonnigBlok.VoorDag(Dag(
            (9, 40), (10, 60), (11, 70), (12, 80), (13, 70), (14, 50)), Morgen, pvPiekKw: 4);

        Assert.Null(blok);
    }

    [Fact]
    public void Een_opening_in_de_bewolking_is_geen_blok()
    {
        // Eén fel uur tussen grijze uren. Verbreden lukt wel (de buuruurtjes bestaan), maar dan haalt
        // het blok de drempel niet meer. Dat is de bedoeling: "morgen een uurtje zon" is geen tip.
        var blok = ZonnigBlok.VoorDag(Dag(
            (10, 60), (11, 70), (12, 900), (13, 70), (14, 60)), Morgen, pvPiekKw: 4);

        Assert.Null(blok);
    }

    [Fact]
    public void Het_blok_wordt_nooit_langer_dan_zes_uur()
    {
        var uren = Enumerable.Range(8, 12).Select(u => (u, 700.0)).ToArray();

        var blok = ZonnigBlok.VoorDag(Dag(uren), Morgen, pvPiekKw: 4);

        Assert.NotNull(blok);
        Assert.Equal(ZonnigBlok.MaxUren, blok!.TotUur - blok.VanUur);
    }

    [Fact]
    public void Een_gat_in_de_verwachting_wordt_geen_blok()
    {
        // Twee zonnige uren, dan een gat van drie uur, dan weer twee. Als die aan elkaar geplakt
        // zouden worden noemden we een tijd waarvan we het midden niet kennen.
        var blok = ZonnigBlok.VoorDag(Dag(
            (9, 800), (10, 800), (14, 800), (15, 800)), Morgen, pvPiekKw: 4);

        Assert.Null(blok);
    }

    [Fact]
    public void Een_klein_dak_haalt_de_drempel_niet()
    {
        var uren = Dag((10, 500), (11, 600), (12, 650), (13, 600), (14, 480));

        // Vier kWp levert in dit blok ruim boven de drempel, een halve kWp bij lange na niet.
        Assert.NotNull(ZonnigBlok.VoorDag(uren, Morgen, pvPiekKw: 4));
        Assert.Null(ZonnigBlok.VoorDag(uren, Morgen, pvPiekKw: 0.5));
    }

    [Fact]
    public void Zonder_piekvermogen_zeggen_we_niets()
    {
        var uren = Dag((10, 700), (11, 800), (12, 850), (13, 800));

        Assert.Null(ZonnigBlok.VoorDag(uren, Morgen, pvPiekKw: 0));
    }

    [Fact]
    public void Uren_van_een_andere_dag_tellen_niet_mee()
    {
        // De verwachting loopt twee dagen vooruit. Vraag je om morgen, dan mag vandaag niet meedoen,
        // anders staat er in de melding een tijd die al voorbij is.
        var vandaag = Morgen.AddDays(-1);
        var uren = Dag((10, 700), (11, 800), (12, 850), (13, 800));

        Assert.Null(ZonnigBlok.VoorDag(uren, vandaag, pvPiekKw: 4));
    }
}
