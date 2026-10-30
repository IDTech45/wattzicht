using System.Text.Json;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests;

/// <summary>Slim sturen: goedkope blokken vinden en de besparing eerlijk uitrekenen.</summary>
public sealed class AdviceEngineTests
{
    // Vrijdag 30 oktober 2026, 09:00 Nederlandse tijd (UTC+1) = 08:00 UTC.
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset MidnightUtc = new(2026, 10, 29, 23, 0, 0, TimeSpan.Zero);

    /// <summary>24 uur: overal 0,10, behalve 12–15 lokaal (−0,02) en 18–21 lokaal (0,30).</summary>
    private static List<HourPrice> Day()
    {
        var list = new List<HourPrice>();
        for (var h = 0; h < 24; h++)
        {
            var price = h is >= 12 and < 15 ? -0.02m : h is >= 18 and < 21 ? 0.30m : 0.10m;
            list.Add(new HourPrice(MidnightUtc.AddHours(h), price));
        }

        return list;
    }

    private static AdviceInput Input(bool dynamic, bool pv = true, bool battery = false, bool charger = true, DateTimeOffset? now = null) => new(
        now ?? Now,
        Day(),
        HasPv: pv,
        HasBattery: battery,
        HasCharger: charger,
        IsDynamic: dynamic,
        new TariffRates(0.30m, 0.08m),
        AvgDailyChargedKwh: 10,
        AvgDailyExportKwh: 8,
        TypicalBatteryKwh: 5,
        SurchargePerKwh: 0.13m);

    [Fact]
    public void Vindt_goedkoopste_en_duurste_blok()
    {
        var cheapest = AdviceEngine.FindWindow(Day(), 3, cheapest: true, 0m)!;
        var expensive = AdviceEngine.FindWindow(Day(), 3, cheapest: false, 0m)!;

        Assert.Equal(MidnightUtc.AddHours(12), cheapest.From);
        Assert.Equal(MidnightUtc.AddHours(15), cheapest.To);
        Assert.Equal(-0.02m, cheapest.AverageAllInEur);
        Assert.Equal(MidnightUtc.AddHours(18), expensive.From);
    }

    [Fact]
    public void Blok_over_een_gat_in_de_prijzen_telt_niet()
    {
        var prices = new List<HourPrice>
        {
            new(MidnightUtc, 0.01m),
            new(MidnightUtc.AddHours(2), 0.01m), // uur 1 ontbreekt
            new(MidnightUtc.AddHours(3), 0.20m),
        };

        Assert.Null(AdviceEngine.FindWindow(prices, 3, cheapest: true, 0m));
    }

    [Fact]
    public void Dynamisch_laadadvies_rekent_avondprijs_min_goedkoopste_blok()
    {
        var advice = AdviceEngine.Build(Input(dynamic: true));

        var tip = Assert.Single(advice.Tips, t => t.Kind == AdviceTipKind.Charger);
        Assert.Equal("Laad de auto 12:00–15:00", tip.Title);

        // Avond 17–22 (6 uur): 1×0,10 + 3×0,30 + 2×0,10 = 1,20 / 6 = 0,20 markt → 0,33 all-in.
        // Goedkoopste blok: −0,02 + 0,13 = 0,11. Verschil 0,22 × 10 kWh = 2,20.
        Assert.Equal(2.20m, tip.SavingEur);
    }

    [Fact]
    public void Batterij_laadt_goedkoop_en_ontlaadt_later_duur()
    {
        var advice = AdviceEngine.Build(Input(dynamic: true, battery: true));

        var tip = Assert.Single(advice.Tips, t => t.Kind == AdviceTipKind.Battery);
        Assert.Equal(MidnightUtc.AddHours(12), tip.From);
        Assert.Equal(MidnightUtc.AddHours(21), tip.To);

        // 5 kWh × (0,43 − 0,11/0,9) = 5 × (0,43 − 0,12222) = 1,5389 → 1,54
        Assert.Equal(1.54m, tip.SavingEur);
    }

    [Fact]
    public void Negatieve_prijs_met_zonnepanelen_geeft_terugleverwaarschuwing()
    {
        var advice = AdviceEngine.Build(Input(dynamic: true));

        var tip = Assert.Single(advice.Tips, t => t.Kind == AdviceTipKind.Export);
        Assert.Equal("Niet terugleveren 12:00–15:00", tip.Title);
        Assert.Equal(0m, tip.SavingEur);
    }

    [Fact]
    public void Vast_contract_in_2026_belooft_geen_geld_maar_noemt_2027()
    {
        var advice = AdviceEngine.Build(Input(dynamic: false));

        var tip = Assert.Single(advice.Tips, t => t.Kind == AdviceTipKind.Charger);
        Assert.Equal(0m, tip.SavingEur);
        // min(10, 8 kWh overschot) × (0,30 − 0,08) = 1,76
        Assert.Equal("Vanaf 2027 € 1,76 per dag", tip.Note);
    }

    [Fact]
    public void Vast_contract_in_2027_levert_wel_geld_op()
    {
        var advice = AdviceEngine.Build(Input(dynamic: false, now: new DateTimeOffset(2027, 4, 1, 7, 0, 0, TimeSpan.Zero)));

        var tip = Assert.Single(advice.Tips, t => t.Kind == AdviceTipKind.Charger);
        Assert.Equal(1.76m, tip.SavingEur);
        Assert.Null(tip.Note);
    }

    [Fact]
    public void Na_de_middag_gaat_vast_advies_over_morgen()
    {
        var evening = new DateTimeOffset(2026, 10, 30, 18, 0, 0, TimeSpan.Zero);
        var advice = AdviceEngine.Build(Input(dynamic: false, now: evening));

        var tip = Assert.Single(advice.Tips, t => t.Kind == AdviceTipKind.Charger);
        Assert.Equal("Laad de auto morgen 12:00–15:00", tip.Title);
    }

    [Fact]
    public void Zonder_apparaten_en_vast_contract_geen_tips()
    {
        var advice = AdviceEngine.Build(Input(dynamic: false, pv: false, charger: false));

        Assert.Empty(advice.Tips);
        Assert.Equal(0m, advice.TotalSavingEur);
    }

    [Fact]
    public void Verleden_uren_vallen_weg()
    {
        var advice = AdviceEngine.Build(Input(dynamic: true));

        Assert.Equal(MidnightUtc.AddHours(9), advice.Prices[0].Start);
        Assert.Equal(15, advice.Prices.Count);
    }

    [Fact]
    public void EnergyZero_kwartierprijzen_worden_uurprijzen()
    {
        const string json = """
            {"Prices":[
              {"price":0.10,"readingDate":"2026-10-29T23:00:00Z"},
              {"price":0.20,"readingDate":"2026-10-29T23:15:00Z"},
              {"price":0.30,"readingDate":"2026-10-29T23:30:00Z"},
              {"price":0.40,"readingDate":"2026-10-29T23:45:00Z"},
              {"price":-0.05,"readingDate":"2026-10-30T00:00:00Z"}
            ],"intervalType":4}
            """;

        using var doc = JsonDocument.Parse(json);
        var prices = EnergyZeroPriceProvider.Parse(doc.RootElement);

        Assert.Equal(2, prices.Count);
        Assert.Equal(0.25m, prices[0].MarketEurPerKwh);
        Assert.Equal(MidnightUtc, prices[0].StartUtc);
        Assert.Equal(-0.05m, prices[1].MarketEurPerKwh);
    }
}
