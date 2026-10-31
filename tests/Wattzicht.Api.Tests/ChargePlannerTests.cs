using Wattzicht.Api.Services;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Wanneer laad je je auto het voordeligst?
///
/// Het makkelijke antwoord is "in het goedkoopste uur", en dat is precies het antwoord dat geld
/// kost. Een kilowattuur van je eigen dak kost je alleen de terugleververgoeding die je misloopt,
/// en dat is vrijwel altijd minder dan stroom van het net. Een planner die alleen naar de uurprijs
/// kijkt, laat de auto om drie uur 's nachts laden terwijl het dak 's middags gratis staat weg te
/// geven. Vanaf 2027, als de saldering weg is, is dat het verschil tussen een paar cent en de volle
/// prijs per kilowattuur.
///
/// Deze tests leggen die afweging vast, en de gevallen waarin er niets te kiezen valt.
/// </summary>
public class ChargePlannerTests
{
    private static readonly DateTimeOffset Middernacht = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Vast tarief: 30 cent van het net, 8 cent terug voor wat je teruglevert.</summary>
    private static readonly TariffRates Vast = new(0.30m, 0.08m);

    private static ChargePlanInput Plan(
        double kwh = 10,
        double laadKw = 10,
        IReadOnlyList<HourPrice>? prijzen = null,
        IReadOnlyList<HourlyIrradiance>? zon = null,
        double pvPiek = 0,
        bool dynamisch = false,
        int urenTot = 24) =>
        new(Middernacht, Middernacht.AddHours(urenTot), kwh, laadKw,
            prijzen ?? [], zon ?? [], pvPiek, dynamisch, Vast);

    /// <summary>Uurprijzen waarbij uur 3 spotgoedkoop is en uur 18 juist duur.</summary>
    private static List<HourPrice> PrijzenMetDalOmDrieUur() =>
        Enumerable.Range(0, 24)
            .Select(u => new HourPrice(Middernacht.AddHours(u), u == 3 ? 0.01m : u == 18 ? 0.40m : 0.15m))
            .ToList();

    /// <summary>Volle zon tussen 11 en 15 uur, daarbuiten donker.</summary>
    private static List<HourlyIrradiance> ZonTussenElfEnDrie() =>
        Enumerable.Range(0, 24)
            .Select(u => new HourlyIrradiance(Middernacht.AddHours(u), u is >= 11 and < 15 ? 800d : 0d))
            .ToList();

    [Fact]
    public void Met_een_dynamisch_contract_kiest_hij_het_goedkoopste_uur()
    {
        var plan = ChargePlanner.Build(Plan(kwh: 8, laadKw: 10, prijzen: PrijzenMetDalOmDrieUur(), dynamisch: true));

        Assert.True(plan.Feasible);
        Assert.Single(plan.Slots);
        Assert.Equal(3, plan.Slots[0].StartUtc.Hour);
    }

    [Fact]
    public void Zonder_dynamisch_contract_wint_de_zon_van_de_klok()
    {
        // Vast tarief: elk uur van het net kost hetzelfde, dus het enige wat scheelt is eigen zon.
        var plan = ChargePlanner.Build(Plan(
            kwh: 8, laadKw: 10, prijzen: PrijzenMetDalOmDrieUur(),
            zon: ZonTussenElfEnDrie(), pvPiek: 6, dynamisch: false));

        Assert.True(plan.Feasible);
        Assert.InRange(plan.Slots[0].StartUtc.Hour, 11, 14);
        Assert.True(plan.SolarKwh > 0, "er hoort zon in het plan te zitten");
    }

    [Fact]
    public void Zonder_zon_en_zonder_prijzen_begint_hij_gewoon_meteen()
    {
        // Valt er niets te kiezen, dan is uitstellen alleen maar risico: dan maar nu.
        var plan = ChargePlanner.Build(Plan(kwh: 5, laadKw: 10));

        Assert.True(plan.Feasible);
        Assert.Equal(Middernacht, plan.Slots[0].StartUtc);
        Assert.Equal(0m, plan.SavingEur);
    }

    [Fact]
    public void De_besparing_is_het_verschil_met_nu_beginnen()
    {
        var plan = ChargePlanner.Build(Plan(kwh: 10, laadKw: 10, prijzen: PrijzenMetDalOmDrieUur(), dynamisch: true));

        // Nu beginnen is uur 0 (15 ct + 13 ct opslag), het plan pakt uur 3 (1 ct + 13 ct).
        Assert.Equal(plan.CostIfStartNowEur - plan.CostEur, plan.SavingEur);
        Assert.True(plan.SavingEur > 0, "uitstellen naar het dal hoort geld op te leveren");
    }

    [Fact]
    public void Past_het_niet_binnen_de_tijd_dan_zegt_hij_dat_eerlijk()
    {
        // 40 kWh met een paal van 10 kW in drie uur: dat gaat niet.
        var plan = ChargePlanner.Build(Plan(kwh: 40, laadKw: 10, urenTot: 3));

        Assert.False(plan.Feasible);
        Assert.Equal(30, plan.TotalKwh);
        Assert.Contains("niet helemaal vol", plan.Explanation);
    }

    [Fact]
    public void Een_deadline_in_het_verleden_levert_een_leeg_plan_op()
    {
        var plan = ChargePlanner.Build(Plan(kwh: 10, urenTot: 0));

        Assert.False(plan.Feasible);
        Assert.Empty(plan.Slots);
    }

    [Fact]
    public void Niets_te_laden_is_geen_fout()
    {
        var plan = ChargePlanner.Build(Plan(kwh: 0));

        Assert.True(plan.Feasible);
        Assert.Empty(plan.Slots);
        Assert.Contains("hoeft niets", plan.Explanation);
    }

    [Fact]
    public void Het_laden_wordt_over_meer_uren_verdeeld_als_het_niet_in_een_uur_past()
    {
        var plan = ChargePlanner.Build(Plan(kwh: 25, laadKw: 10, prijzen: PrijzenMetDalOmDrieUur(), dynamisch: true));

        Assert.True(plan.Feasible);
        Assert.Equal(3, plan.Slots.Count);
        Assert.Equal(25, plan.TotalKwh);
        // Op tijd gesorteerd, want zo leest het in de app als een plan en niet als een prijslijst.
        Assert.Equal(plan.Slots.OrderBy(s => s.StartUtc).Select(s => s.StartUtc), plan.Slots.Select(s => s.StartUtc));
    }

    [Fact]
    public void Schemer_telt_niet_als_zon()
    {
        // 40 W/m² is geen opbrengst waar je een laadplan op baseert; dan is het net eerlijker.
        var schemer = Enumerable.Range(0, 24)
            .Select(u => new HourlyIrradiance(Middernacht.AddHours(u), 40d)).ToList();
        var plan = ChargePlanner.Build(Plan(kwh: 5, laadKw: 10, zon: schemer, pvPiek: 6));

        Assert.Equal(0, plan.SolarKwh);
    }

    [Fact]
    public void Zonder_zonnepanelen_rekent_hij_nergens_zon_in()
    {
        var plan = ChargePlanner.Build(Plan(kwh: 5, zon: ZonTussenElfEnDrie(), pvPiek: 0));

        Assert.Equal(0, plan.SolarKwh);
    }

    [Theory]
    [InlineData(1000d, 6d, 5.1d)]  // vol zon: 6 kWp maal 85% rendement
    [InlineData(500d, 6d, 2.55d)]  // half bewolkt
    [InlineData(800d, 3d, 2.04d)]  // kleinere installatie
    public void De_zonopbrengst_per_uur_volgt_straling_maal_piekvermogen(double straling, double pvPiek, double verwacht)
    {
        var zon = new List<HourlyIrradiance> { new(Middernacht, straling) };
        var plan = ChargePlanner.Build(Plan(kwh: 20, laadKw: 20, zon: zon, pvPiek: pvPiek, urenTot: 1));

        Assert.Equal(verwacht, plan.SolarKwh, 2);
    }
}

/// <summary>
/// Wanneer trilt de telefoon van de bewoner?
///
/// De adviezen bestonden al, maar alleen voor wie de app opende. Deze regel brengt het advies naar
/// de bewoner, en dat betekent ook: alleen als het de moeite waard is en op een normaal tijdstip.
/// Een melding die niets oplevert is de snelste manier om alle meldingen uitgezet te krijgen.
/// </summary>
public class SmartMomentTests
{
    private static readonly DateTimeOffset Nu = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    private static ChargePlanner.Plan PlanDatBegintOm(DateTimeOffset start, decimal besparing = 1.00m, double zon = 0)
        => new([new ChargePlanner.Slot(start, 10, zon, 1m)], 10, 1m, 1m + besparing, besparing, zon, true, "test");

    [Fact]
    public void Een_uur_dat_zo_begint_levert_een_melding_op()
    {
        Assert.True(SmartMomentWorker.IsHetBijnaZover(PlanDatBegintOm(Nu.AddMinutes(30)), Nu));
    }

    [Fact]
    public void Een_uur_dat_pas_vanavond_begint_nog_niet()
    {
        // Dan is de melding nutteloos geworden tegen de tijd dat het zover is.
        Assert.False(SmartMomentWorker.IsHetBijnaZover(PlanDatBegintOm(Nu.AddHours(6)), Nu));
    }

    [Fact]
    public void Een_uur_dat_al_bezig_is_levert_niets_op()
    {
        Assert.False(SmartMomentWorker.IsHetBijnaZover(PlanDatBegintOm(Nu.AddMinutes(-10)), Nu));
    }

    [Fact]
    public void Voor_een_paar_cent_storen_we_niemand()
    {
        Assert.False(SmartMomentWorker.IsHetBijnaZover(PlanDatBegintOm(Nu.AddMinutes(30), besparing: 0.03m), Nu));
    }

    [Fact]
    public void Eigen_zon_is_op_zichzelf_al_reden_genoeg()
    {
        // Met een vast tarief levert uitstellen geen euro's op, maar je auto vullen met je eigen dak
        // in plaats van met stroom van het net is precies waar dit product voor bedoeld is.
        Assert.True(SmartMomentWorker.IsHetBijnaZover(
            PlanDatBegintOm(Nu.AddMinutes(30), besparing: 0m, zon: 4), Nu));
    }

    [Fact]
    public void Zonder_plan_geen_melding()
    {
        var leeg = new ChargePlanner.Plan([], 0, 0, 0, 0, 0, false, "");
        Assert.False(SmartMomentWorker.IsHetBijnaZover(leeg, Nu));
    }
}
