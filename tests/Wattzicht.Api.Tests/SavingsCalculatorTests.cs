using Wattzicht.Api.Services;

namespace Wattzicht.Api.Tests;

/// <summary>Besparingsregels uit ARCHITECTUUR.md, met en zonder saldering.</summary>
public sealed class SavingsCalculatorTests
{
    private readonly SavingsCalculator _calculator = new();
    private static readonly TariffRates Rates = new(ImportPricePerKwh: 0.30m, ExportPricePerKwh: 0.08m);

    [Fact]
    public void Saldering_geldt_tot_en_met_31_december_2026()
    {
        Assert.True(SavingsCalculator.IsSalderingActive(new DateOnly(2026, 12, 31)));
        Assert.False(SavingsCalculator.IsSalderingActive(new DateOnly(2027, 1, 1)));
    }

    [Fact]
    public void Teruglevering_in_2026_wordt_tegen_leveringstarief_gerekend()
    {
        var input = new SavingsInput(new DateOnly(2026, 6, 15), SelfUseKwh: 0, ExportKwh: 10, ChargedKwh: 0, BatteryDischargedKwh: 0, BatteryChargedFromGridKwh: 0);

        var result = _calculator.Calculate(input, Rates);

        Assert.Equal(3.00m, result.ExportEur);
        Assert.Equal(3.00m, result.TotalEur);
    }

    [Fact]
    public void Teruglevering_in_2027_wordt_tegen_terugleververgoeding_gerekend()
    {
        var input = new SavingsInput(new DateOnly(2027, 6, 15), SelfUseKwh: 0, ExportKwh: 10, ChargedKwh: 0, BatteryDischargedKwh: 0, BatteryChargedFromGridKwh: 0);

        var result = _calculator.Calculate(input, Rates);

        Assert.Equal(0.80m, result.ExportEur);
    }

    [Fact]
    public void Zelfverbruik_telt_tegen_leveringstarief()
    {
        var input = new SavingsInput(new DateOnly(2026, 3, 1), SelfUseKwh: 8, ExportKwh: 0, ChargedKwh: 0, BatteryDischargedKwh: 0, BatteryChargedFromGridKwh: 0);

        var result = _calculator.Calculate(input, Rates);

        Assert.Equal(2.40m, result.SelfUseEur);
    }

    [Fact]
    public void Laadpaal_bespaart_verschil_met_publiek_laden()
    {
        // 20 kWh × (0,59 − 0,30) = 5,80
        var input = new SavingsInput(new DateOnly(2026, 3, 1), SelfUseKwh: 0, ExportKwh: 0, ChargedKwh: 20, BatteryDischargedKwh: 0, BatteryChargedFromGridKwh: 0);

        var result = _calculator.Calculate(input, Rates);

        Assert.Equal(5.80m, result.ChargerEur);
    }

    [Fact]
    public void Laadpaal_besparing_wordt_nooit_negatief()
    {
        var dureStroom = new TariffRates(ImportPricePerKwh: 0.70m, ExportPricePerKwh: 0.08m);
        var input = new SavingsInput(new DateOnly(2026, 3, 1), SelfUseKwh: 0, ExportKwh: 0, ChargedKwh: 20, BatteryDischargedKwh: 0, BatteryChargedFromGridKwh: 0);

        var result = _calculator.Calculate(input, dureStroom);

        Assert.Equal(0m, result.ChargerEur);
    }

    [Fact]
    public void Batterij_rekent_ontladen_min_van_net_geladen()
    {
        // 5 kWh ontladen × 0,30 − 2 kWh van net × 0,30 = 0,90
        var input = new SavingsInput(new DateOnly(2026, 3, 1), SelfUseKwh: 0, ExportKwh: 0, ChargedKwh: 0, BatteryDischargedKwh: 5, BatteryChargedFromGridKwh: 2);

        var result = _calculator.Calculate(input, Rates);

        Assert.Equal(0.90m, result.BatteryEur);
    }

    [Fact]
    public void Totaal_is_som_van_onderdelen()
    {
        var input = new SavingsInput(new DateOnly(2026, 3, 1), SelfUseKwh: 8, ExportKwh: 10, ChargedKwh: 20, BatteryDischargedKwh: 5, BatteryChargedFromGridKwh: 2);

        var result = _calculator.Calculate(input, Rates);

        Assert.Equal(result.SelfUseEur + result.ExportEur + result.ChargerEur + result.BatteryEur, result.TotalEur);
        Assert.Equal(12.10m, result.TotalEur);
        Assert.Equal(result.TotalEur, _calculator.CalculateTotal(input, Rates));
    }

    [Fact]
    public void Negatieve_invoer_wordt_als_nul_behandeld()
    {
        var input = new SavingsInput(new DateOnly(2026, 3, 1), SelfUseKwh: -5, ExportKwh: -1, ChargedKwh: -3, BatteryDischargedKwh: -2, BatteryChargedFromGridKwh: -2);

        var result = _calculator.Calculate(input, Rates);

        Assert.Equal(0m, result.TotalEur);
    }

    [Fact]
    public void Terugleverkosten_gaan_van_de_teruglevering_af()
    {
        var rates = new TariffRates(ImportPricePerKwh: 0.30m, ExportPricePerKwh: 0.08m, FeedInCostPerKwh: 0.12m);

        var in2026 = _calculator.Calculate(new SavingsInput(new DateOnly(2026, 6, 15), 0, ExportKwh: 10, 0, 0, 0), rates);
        var in2027 = _calculator.Calculate(new SavingsInput(new DateOnly(2027, 6, 15), 0, ExportKwh: 10, 0, 0, 0), rates);

        Assert.Equal(1.80m, in2026.ExportEur);   // 10 × (0,30 − 0,12)
        Assert.Equal(-0.40m, in2027.ExportEur);  // 10 × (0,08 − 0,12): terugleveren kost dan geld
    }
}
