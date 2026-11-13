using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Wat een thuisbatterij bij déze woning zou opleveren.
///
/// Elke verkoper rekent met een gemiddeld huishouden, en dat bestaat niet. Hier wordt gerekend met
/// de dagen die deze woning zelf heeft gemeten, en dat betekent ook: wie niets terugleverde of
/// niets van het net haalde krijgt geen bedrag voorgeschoteld.
/// </summary>
public sealed class BatterijAdviesTests
{
    private static readonly DateOnly Vandaag = new(2026, 11, 13);

    /// <summary>Levering 30 cent, teruglevering 8 cent, geen terugleverkosten.</summary>
    private static readonly TariffRates Tarief = new(0.30m, 0.08m);

    private static List<BatterijAdvies.Dag> Dagen(double export, double import, int aantal = 30) =>
        Enumerable.Range(0, aantal).Select(_ => new BatterijAdvies.Dag(export, import)).ToList();

    [Fact]
    public void Een_dag_verplaatst_het_kleinste_van_drie()
    {
        // Acht terug, zes later weer nodig, batterij van tien: dan gaat er zes doorheen, min verlies.
        var kwh = BatterijAdvies.Verplaatst(new BatterijAdvies.Dag(8, 6), 10);

        Assert.Equal(6 * BatterijAdvies.Rendement, kwh, 3);
    }

    [Fact]
    public void De_batterij_is_de_grens_als_hij_kleiner_is()
    {
        var kwh = BatterijAdvies.Verplaatst(new BatterijAdvies.Dag(12, 12), 5);

        Assert.Equal(5 * BatterijAdvies.Rendement, kwh, 3);
    }

    [Fact]
    public void Zonder_teruglevering_valt_er_niets_te_verplaatsen()
    {
        Assert.Equal(0, BatterijAdvies.Verplaatst(new BatterijAdvies.Dag(0, 20), 10));
    }

    [Fact]
    public void Zonder_afname_ook_niet()
    {
        // Alles wat je terugleverde had je later niet nodig: dan doet een batterij niets voor je.
        Assert.Equal(0, BatterijAdvies.Verplaatst(new BatterijAdvies.Dag(20, 0), 10));
    }

    [Fact]
    public void Drie_maten_met_oplopende_opbrengst()
    {
        var maten = BatterijAdvies.Bereken(Dagen(export: 12, import: 12), Tarief, Vandaag);

        Assert.Equal(3, maten.Count);
        Assert.True(maten[1].PerJaarNaSalderingEur > maten[0].PerJaarNaSalderingEur);
        Assert.True(maten[2].PerJaarNaSalderingEur >= maten[1].PerJaarNaSalderingEur);
    }

    [Fact]
    public void Na_de_saldering_is_het_meer_waard()
    {
        // Zolang er gesaldeerd wordt levert zelf gebruiken bijna niets op; vanaf 2027 wel.
        var maat = BatterijAdvies.Bereken(Dagen(10, 10), Tarief, Vandaag)[0];

        Assert.True(maat.PerJaarNaSalderingEur > maat.PerJaarEur);
    }

    [Fact]
    public void Een_rekensom_die_je_kunt_narekenen()
    {
        // 5 kWh per dag door een batterij van 5, 90% rendement: 4,5 kWh per dag, 1642 per jaar.
        // Vanaf 2027 is elke zelf gebruikte kWh 30 - 8 = 22 cent waard, dus ongeveer 361 euro.
        var maat = BatterijAdvies.Bereken(Dagen(5, 5), Tarief, Vandaag)[0];

        Assert.Equal(4.5, maat.VerplaatstPerDagKwh, 1);
        Assert.InRange(maat.PerJaarNaSalderingEur, 350m, 375m);
    }

    [Fact]
    public void Te_weinig_dagen_levert_geen_advies_op()
    {
        Assert.Empty(BatterijAdvies.Bereken(Dagen(10, 10, aantal: 5), Tarief, Vandaag));
        Assert.Empty(BatterijAdvies.Bereken([], Tarief, Vandaag));
    }

    [Fact]
    public void Dagen_zonder_enig_verkeer_tellen_niet_mee()
    {
        // Een woning die net gekoppeld is heeft rijen met nullen; die horen het gemiddelde niet te
        // verwateren tot een bedrag dat nergens op slaat.
        var dagen = Dagen(10, 10, 20).Concat(Dagen(0, 0, 40)).ToList();

        var maat = BatterijAdvies.Bereken(dagen, Tarief, Vandaag)[0];

        Assert.Equal(4.5, maat.VerplaatstPerDagKwh, 1);
    }
}
