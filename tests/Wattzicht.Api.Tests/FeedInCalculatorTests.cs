using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests;

/// <summary>Terugleverkosten en wat zelf gebruiken scheelt, voor en na het einde van de saldering.</summary>
public sealed class FeedInCalculatorTests
{
    private static readonly TariffRates Rates = new(ImportPricePerKwh: 0.30m, ExportPricePerKwh: 0.05m, FeedInCostPerKwh: 0.12m);
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 11, 0, 0, TimeSpan.Zero);

    private static LiveSnapshotDto Live(double gridW, double chargerW = 0, DateTimeOffset? at = null) =>
        new(PvPowerW: 4000, LoadPowerW: 800, GridPowerW: gridW, BatteryPowerW: 0, BatterySoc: null, ChargerPowerW: chargerW, UpdatedAt: at ?? Now);

    [Fact]
    public void Tijdens_saldering_scheelt_zelf_gebruiken_precies_de_terugleverkosten()
    {
        Assert.Equal(0.12m, FeedInCalculator.SelfUseWorthPerKwh(Rates, new DateOnly(2026, 10, 30)));
    }

    [Fact]
    public void Vanaf_2027_scheelt_zelf_gebruiken_veel_meer()
    {
        // 0,30 − (0,05 − 0,12) = 0,37
        Assert.Equal(0.37m, FeedInCalculator.SelfUseWorthPerKwh(Rates, new DateOnly(2027, 1, 1)));
    }

    [Fact]
    public void Jaar_wordt_doorgerekend_bij_minder_dan_een_jaar_data()
    {
        var dto = FeedInCalculator.Build(new DateOnly(2026, 10, 30), Rates, monthExportKwh: 100, exportKwhLastYear: 600, daysWithData: 73, liveExportW: 0);

        Assert.True(dto.YearIsEstimate);
        Assert.Equal(3000, dto.YearExportKwh);       // 600 / 73 × 365
        Assert.Equal(12.00m, dto.MonthCostEur);      // 100 × 0,12
        Assert.Equal(360.00m, dto.YearCostEur);      // 3000 × 0,12
        Assert.Equal(1110.00m, dto.YearValue2027Eur); // 3000 × 0,37
    }

    [Fact]
    public void Geen_data_geeft_nullen()
    {
        var dto = FeedInCalculator.Build(new DateOnly(2026, 10, 30), Rates, 0, 0, 0, 0);

        Assert.Equal(0, dto.YearExportKwh);
        Assert.False(dto.YearIsEstimate);
        Assert.Equal(0m, dto.YearCostEur);
    }

    [Fact]
    public void Live_tip_bij_flink_terugleveren_kiest_de_auto_als_die_niet_laadt()
    {
        var tip = FeedInCalculator.LiveSurplusTip(Live(-3200), Now, hasCharger: true, Rates)!;

        Assert.Equal(AdviceTipKind.Export, tip.Kind);
        Assert.Equal("Nu de auto laden", tip.Title);
        Assert.Equal("Je levert nu 3,2 kW terug aan het net", tip.Detail);
        Assert.Equal("Zelf gebruiken scheelt nu ± € 0,38 per uur", tip.Note); // 3,2 × 0,12
    }

    [Fact]
    public void Live_tip_zonder_laadpaal_of_als_auto_al_laadt_is_wasmachine()
    {
        Assert.Equal("Nu de wasmachine of vaatwasser aan", FeedInCalculator.LiveSurplusTip(Live(-2000), Now, hasCharger: false, Rates)!.Title);
        Assert.Equal("Nu de wasmachine of vaatwasser aan", FeedInCalculator.LiveSurplusTip(Live(-2000, chargerW: 7000), Now, hasCharger: true, Rates)!.Title);
    }

    [Fact]
    public void Geen_live_tip_bij_weinig_teruglevering_afname_of_oude_meting()
    {
        Assert.Null(FeedInCalculator.LiveSurplusTip(Live(-900), Now, true, Rates));
        Assert.Null(FeedInCalculator.LiveSurplusTip(Live(1200), Now, true, Rates));
        Assert.Null(FeedInCalculator.LiveSurplusTip(Live(-3000, at: Now.AddMinutes(-30)), Now, true, Rates));
        Assert.Null(FeedInCalculator.LiveSurplusTip(null, Now, true, Rates));
    }
}
