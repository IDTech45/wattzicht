using Wattzicht.Api.Services;
using Xunit;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Wat de besparing doet bij een woning waar alleen de zon gemeten wordt.
///
/// Dit kwam uit de eigen woning van 21-11-2026: 1.136 dagen met in totaal 11.747 kWh opwek, een
/// ingevuld tarief, en toch stond er in het scherm elke dag 0,00 euro. Zelfverbruik en teruglevering
/// komen namelijk uit de meting van de meterkast, en die is er niet bij iemand met alleen een
/// omvormer. De opwek zelf komt wel binnen, uit de teller van het apparaat.
///
/// Zolang er gesaldeerd wordt en de leverancier geen terugleverkosten rekent, hoeft die verdeling ook
/// niet bekend te zijn: een kilowattuur die je terugstuurt is dan precies evenveel waard als een die
/// je zelf gebruikt. Daarna wel, en dan verzinnen we hem niet.
/// </summary>
public class BesparingZonderMeterkastTests
{
    private readonly SavingsCalculator _rekenaar = new();
    private static readonly TariffRates Tarief = new(ImportPricePerKwh: 0.22m, ExportPricePerKwh: 0.06m);

    private static SavingsInput Dag(DateOnly datum, double pv, double zelf = 0, double terug = 0) =>
        new(datum, SelfUseKwh: zelf, ExportKwh: terug, ChargedKwh: 0,
            BatteryDischargedKwh: 0, BatteryChargedFromGridKwh: 0, PvKwh: pv);

    [Fact]
    public void Alleen_een_omvormer_levert_tijdens_saldering_gewoon_een_bedrag_op()
    {
        var uitkomst = _rekenaar.CalculateTotal(Dag(new DateOnly(2026, 6, 15), pv: 20), Tarief);

        Assert.Equal(4.40m, uitkomst); // 20 kWh x 0,22
    }

    /// <summary>Na de saldering scheelt het wél waar de stroom heen ging, dus dan blijft het leeg.</summary>
    [Fact]
    public void Na_de_saldering_blijft_het_nul_tot_er_gemeten_wordt()
    {
        var uitkomst = _rekenaar.CalculateTotal(Dag(new DateOnly(2027, 6, 15), pv: 20), Tarief);

        Assert.Equal(0m, uitkomst);
    }

    /// <summary>
    /// Rekent de leverancier terugleverkosten, dan is teruggeleverde stroom minder waard dan zelf
    /// gebruikte. Dan is de verdeling wél nodig en gokken we niet.
    /// </summary>
    [Fact]
    public void Met_terugleverkosten_gokken_we_de_verdeling_niet()
    {
        var metKosten = new TariffRates(0.22m, 0.06m, FeedInCostPerKwh: 0.05m);

        var uitkomst = _rekenaar.CalculateTotal(Dag(new DateOnly(2026, 6, 15), pv: 20), metKosten);

        Assert.Equal(0m, uitkomst);
    }

    /// <summary>
    /// Wordt de meterkast wel gemeten, dan telt die meting en niet de opwek. Anders zou een woning met
    /// P1-meter er bij een dag vol teruglevering ineens bovenop krijgen.
    /// </summary>
    [Fact]
    public void Een_gemeten_woning_houdt_zijn_eigen_cijfers()
    {
        var gemeten = Dag(new DateOnly(2026, 6, 15), pv: 20, zelf: 5, terug: 15);

        var uitkomst = _rekenaar.CalculateTotal(gemeten, Tarief);

        Assert.Equal(4.40m, uitkomst); // 5 x 0,22 zelf + 15 x 0,22 gesaldeerd, niet nog eens 20
    }

    /// <summary>Alleen teruglevering gemeten en geen zelfverbruik is ook een gemeten dag.</summary>
    [Fact]
    public void Alles_teruggeleverd_telt_een_keer()
    {
        var uitkomst = _rekenaar.CalculateTotal(Dag(new DateOnly(2026, 6, 15), pv: 20, terug: 20), Tarief);

        Assert.Equal(4.40m, uitkomst);
    }

    [Fact]
    public void Een_dag_zonder_zon_levert_niets_op()
    {
        var uitkomst = _rekenaar.CalculateTotal(Dag(new DateOnly(2026, 12, 20), pv: 0), Tarief);

        Assert.Equal(0m, uitkomst);
    }
}
