using Wattzicht.Api.Services;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Het piekvermogen schatten uit de eigen metingen.
///
/// De vraag "hoeveel kWp ligt er op je dak" weet lang niet iedereen te beantwoorden; het staat op een
/// offerte van jaren geleden. Wij weten wat de panelen maakten en hoeveel zon er viel, dus kunnen we
/// het uitrekenen.
///
/// Wat hier vastligt is vooral wanneer we het níét doen: te weinig dagen, te donkere dagen, of een
/// uitkomst die voor een thuisinstallatie nergens op slaat. Een verkeerde schatting is erger dan geen
/// schatting, want hij gaat als getal het adviesscherm op.
/// </summary>
public sealed class PiekSchatterTests
{
    /// <summary>Een installatie van <paramref name="kwp"/> kWp, veertien gewone dagen.</summary>
    private static List<PerformanceDay> Dagen(double kwp, int aantal = 14, double rendement = 0.9)
    {
        var dagen = new List<PerformanceDay>();
        var datum = new DateOnly(2026, 5, 1);

        for (var i = 0; i < aantal; i++)
        {
            // Instraling tussen 3 en 6 kWh/m²: een gewone reeks lentedagen.
            var instraling = 3.0 + i % 4;
            dagen.Add(new PerformanceDay(datum.AddDays(i), kwp * instraling * rendement, instraling));
        }

        return dagen;
    }

    [Fact]
    public void Een_installatie_van_vier_komma_twee_komt_er_ook_uit()
    {
        var schatting = PiekSchatter.Schat(Dagen(4.2));

        Assert.NotNull(schatting);
        Assert.InRange(schatting!.Value, 4.0, 4.4);
    }

    [Fact]
    public void Een_groot_dak_ook()
    {
        var schatting = PiekSchatter.Schat(Dagen(9.8));

        Assert.InRange(schatting!.Value, 9.3, 10.3);
    }

    [Fact]
    public void Een_paar_slechte_dagen_verpesten_de_schatting_niet()
    {
        // Sneeuw, schaduw, een paneel dat net uitviel: zulke dagen horen de schatting niet omlaag te
        // trekken. Daarom kijken we naar een goede dag en niet naar het gemiddelde.
        var dagen = Dagen(5.0);
        for (var i = 0; i < 5; i++)
        {
            dagen[i] = dagen[i] with { PvKwh = dagen[i].PvKwh * 0.3 };
        }

        var schatting = PiekSchatter.Schat(dagen);

        Assert.InRange(schatting!.Value, 4.7, 5.3);
    }

    [Fact]
    public void Te_weinig_dagen_geeft_geen_schatting()
    {
        Assert.Null(PiekSchatter.Schat(Dagen(4.2, aantal: 5)));
        Assert.Null(PiekSchatter.Schat([]));
    }

    [Fact]
    public void Donkere_dagen_tellen_niet_mee()
    {
        // Een reeks decemberdagen met nauwelijks zon: daar valt niets uit af te leiden.
        var donker = Enumerable.Range(0, 20)
            .Select(i => new PerformanceDay(new DateOnly(2026, 12, 1).AddDays(i), 0.4, 0.4))
            .ToList();

        Assert.Null(PiekSchatter.Schat(donker));
    }

    [Fact]
    public void Een_uitkomst_die_nergens_op_slaat_geven_we_niet_terug()
    {
        // Honderd kWh per kWh/m²: dat is geen dak meer. Liever geen schatting dan een getal dat als
        // waarheid het adviesscherm op gaat.
        var raar = Enumerable.Range(0, 14)
            .Select(i => new PerformanceDay(new DateOnly(2026, 5, 1).AddDays(i), 500, 4))
            .ToList();

        Assert.Null(PiekSchatter.Schat(raar));
    }

    [Fact]
    public void Dagen_zonder_instraling_worden_overgeslagen()
    {
        // Dat is het gewone geval zolang de weertaak nog niet langs is geweest.
        var zonder = Dagen(4.2).Select(d => d with { IrradianceKwhM2 = null }).ToList();

        Assert.Null(PiekSchatter.Schat(zonder));
    }
}
