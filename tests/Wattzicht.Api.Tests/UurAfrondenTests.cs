using Wattzicht.Api.Services;
using Wattzicht.Shared;
using Xunit;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Het slimme laden deed niets, en niemand kon het zien.
///
/// De planner zoekt de prijs van een uur met <c>StartUtc == dat uur</c>. Hij maakte dat uur door de
/// minuten, seconden en milliseconden van "nu" af te trekken, maar een DateTimeOffset telt in ticks
/// van honderd nanoseconden. Er bleef dus 18:00:00.000942 over in plaats van 18:00:00, en die
/// vergelijking faalde altijd. Elk uur viel terug op de gemiddelde prijs, alle uren werden daarmee
/// even duur, en dan is het eerste uur het goedkoopste: de planner adviseerde altijd "nu beginnen".
///
/// Op het scherm was daar niets van te zien. Er stond een plan, met uren en bedragen, en het klopte
/// alleen niet. Dezelfde fout zat in het naast elkaar leggen van de laadpaal en de P1-meter.
/// </summary>
public sealed class UurAfrondenTests
{
    /// <summary>Niet op een hele seconde: juist die staartjes lieten de vergelijking mislukken.</summary>
    private static readonly DateTimeOffset Nu =
        new DateTimeOffset(2026, 11, 22, 18, 37, 12, TimeSpan.Zero).AddTicks(9425);

    private static HourPrice Prijs(int uur, decimal markt) =>
        new(new DateTimeOffset(2026, 11, 22, uur, 0, 0, TimeSpan.Zero), markt);

    private static ChargePlanInput Invoer(bool dynamisch = true) => new(
        NowUtc: Nu,
        DeadlineUtc: new DateTimeOffset(2026, 11, 23, 7, 0, 0, TimeSpan.Zero),
        NeededKwh: 11,
        ChargerKw: 11,
        Prices:
        [
            Prijs(18, 0.40m), Prijs(19, 0.35m), Prijs(20, 0.30m), Prijs(21, 0.25m),
            Prijs(22, 0.20m), Prijs(23, 0.05m),
        ],
        Forecast: [],
        PvPeakKw: 0,
        IsDynamic: dynamisch,
        Rates: new TariffRates(0.30m, 0.08m, 0.59m, 0m));

    [Fact]
    public void De_planner_kiest_het_goedkoopste_uur_en_niet_het_eerste()
    {
        var plan = ChargePlanner.Build(Invoer());

        var gekozen = Assert.Single(plan.Slots);
        Assert.Equal(23, gekozen.StartUtc.Hour);
        Assert.True(plan.SavingEur > 0, "later laden hoort hier geld te schelen");
    }

    /// <summary>
    /// De kern: het uur waarop de planner zoekt moet precies op het hele uur liggen. Anders vindt
    /// hij nooit een prijs en merk je er niets van, want er rolt gewoon een plan uit.
    /// </summary>
    [Fact]
    public void Een_uur_valt_precies_op_het_hele_uur()
    {
        var plan = ChargePlanner.Build(Invoer() with { NeededKwh = 55, ChargerKw = 11 });

        Assert.All(plan.Slots, s => Assert.Equal(0, s.StartUtc.Ticks % TimeSpan.TicksPerHour));
    }

    /// <summary>Zonder dynamisch contract kost elk uur hetzelfde en is nu beginnen juist wél goed.</summary>
    [Fact]
    public void Met_een_vast_contract_begint_hij_gewoon_nu()
    {
        var plan = ChargePlanner.Build(Invoer(dynamisch: false));

        var gekozen = Assert.Single(plan.Slots);
        Assert.Equal(18, gekozen.StartUtc.Hour);
    }
}
