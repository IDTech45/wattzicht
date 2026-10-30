using System.Text.Json;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests;

/// <summary>Prestatiecheck (opwek tegen het weer), duurzaamheid en kansen per klant.</summary>
public sealed class InsightsTests
{
    private static readonly DateOnly Today = new(2026, 10, 30);
    private static readonly TariffRates Rates = new(0.30m, 0.05m, FeedInCostPerKwh: 0.10m);

    /// <summary>20 dagen waarop de installatie 4 kWh per kWh/m² haalt, daarna de opgegeven laatste dagen.</summary>
    private static List<PerformanceDay> History(params (double Pv, double Irr)[] lastDays)
    {
        var days = new List<PerformanceDay>();
        var start = Today.AddDays(-(20 + lastDays.Length));
        for (var i = 0; i < 20; i++)
        {
            days.Add(new PerformanceDay(start.AddDays(i), 4 * 2.0, 2.0));
        }

        for (var i = 0; i < lastDays.Length; i++)
        {
            days.Add(new PerformanceDay(start.AddDays(20 + i), lastDays[i].Pv, lastDays[i].Irr));
        }

        return days;
    }

    [Fact]
    public void Te_weinig_dagen_is_onbekend()
    {
        var days = History().Take(5).ToList();

        Assert.Equal(PerformanceStatus.Unknown, PerformanceAnalyzer.Analyze(days, Today, 0.30m).Status);
    }

    [Fact]
    public void Normale_opwek_is_ok()
    {
        var result = PerformanceAnalyzer.Analyze(History((8, 2), (12, 3), (6, 1.5)), Today, 0.30m);

        Assert.Equal(PerformanceStatus.Ok, result.Status);
        Assert.Equal(100, result.PercentOfExpected);
    }

    [Fact]
    public void Drie_zonnige_dagen_ver_onder_de_maatstaf_is_laag()
    {
        // Maatstaf 4 kWh per kWh/m². Laatste drie zonnige dagen: 2 per kWh/m² = 50%.
        var result = PerformanceAnalyzer.Analyze(History((4, 2), (6, 3), (4, 2)), Today, 0.30m);

        Assert.Equal(PerformanceStatus.Low, result.Status);
        Assert.Equal(50, result.PercentOfExpected);
        // Gemist in de laatste 7 dagen: (8-4)+(12-6)+(8-4) + 4 normale dagen × 0 = 14 kWh
        Assert.Equal(14, result.LostKwh7Days);
        Assert.Equal(4.20m, result.LostEur7Days);
    }

    [Fact]
    public void Stilgevallen_omvormer_op_zonnige_dagen_is_laag()
    {
        var result = PerformanceAnalyzer.Analyze(History((0, 2), (0, 3), (0, 2)), Today, 0.30m);

        Assert.Equal(PerformanceStatus.Low, result.Status);
        Assert.Equal(0, result.PercentOfExpected);
    }

    [Fact]
    public void Een_slechte_dag_is_nog_geen_probleem()
    {
        var result = PerformanceAnalyzer.Analyze(History((8, 2), (8, 2), (2, 2)), Today, 0.30m);

        Assert.Equal(PerformanceStatus.Ok, result.Status);
    }

    [Fact]
    public void Duurzaamheid_rekent_co2_zelfvoorziening_en_eigen_gebruik()
    {
        var days = new List<InsightDay>
        {
            // pv, selfUse, export, import, charged, batteryDischarged
            new(Today.AddDays(-1), 10, 4, 4, 3, 0, 2),
        };
        var perf = new PerformanceDto(PerformanceStatus.Ok, 100, 0, 0, "ok");

        var s = InsightsCalculator.Sustainability(2026, days, perf, 0.27);

        Assert.Equal(3, s.Co2AvoidedKg);          // 10 × 0,27 = 2,7 → 3
        Assert.Equal(67, s.SelfSufficiencyPercent); // (4+2) / (4+2+3) = 66,7%
        Assert.Equal(60, s.SelfConsumptionPercent); // (10-4) / 10
    }

    [Fact]
    public void Veel_teruglevering_zonder_batterij_is_een_batterijkans_met_bedrag()
    {
        var days = Enumerable.Range(1, 30)
            .Select(i => new InsightDay(Today.AddDays(-i), 20, 6, 10, 2, 0, 0))
            .ToList();
        var ok = new PerformanceDto(PerformanceStatus.Ok, 100, 0, 0, "ok");

        var list = InsightsCalculator.Opportunities(days, hasBattery: false, hasCharger: false, isDynamic: false, Rates, ok);

        var battery = Assert.Single(list, o => o.Kind == OpportunityKind.Battery);
        // min(10, 8) × 365 × 0,9 = 2628 kWh × (0,30 − (0,05 − 0,10)) = 2628 × 0,35 = 919,8 → 920
        Assert.Equal(920m, battery.YearValueEur);
    }

    [Fact]
    public void Laadpaal_en_teruglevering_geeft_slim_laden_en_lage_prestatie_geeft_controle()
    {
        var days = Enumerable.Range(1, 30)
            .Select(i => new InsightDay(Today.AddDays(-i), 15, 5, 3, 4, 6, 0))
            .ToList();
        var low = new PerformanceDto(PerformanceStatus.Low, 55, 12, 3.6m, "laag");

        var list = InsightsCalculator.Opportunities(days, hasBattery: true, hasCharger: true, isDynamic: false, Rates, low);

        Assert.Contains(list, o => o.Kind == OpportunityKind.SmartCharging);
        Assert.Contains(list, o => o.Kind == OpportunityKind.Inspection);
        Assert.Contains(list, o => o.Kind == OpportunityKind.DynamicContract);
        Assert.DoesNotContain(list, o => o.Kind == OpportunityKind.Battery);
    }

    [Fact]
    public void Open_Meteo_straling_wordt_omgerekend_naar_kWh_per_m2()
    {
        const string json = """{"daily":{"time":["2026-10-29","2026-10-30"],"shortwave_radiation_sum":[7.2,null]}}""";
        using var doc = JsonDocument.Parse(json);

        var data = OpenMeteoWeatherProvider.Parse(doc.RootElement);

        Assert.Equal(2.0, data[new DateOnly(2026, 10, 29)]);
        Assert.False(data.ContainsKey(new DateOnly(2026, 10, 30)));
    }
}
