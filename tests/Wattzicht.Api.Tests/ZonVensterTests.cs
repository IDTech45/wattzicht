using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Het zonnigste blok van de dag.
///
/// De verwachting loopt twee dagen vooruit, en op een grijze dag is het zonnigste blok daarvan vaak
/// morgen. De dagtip zei dan "vandaag wordt het zonnig" en wees een tijd aan die pas de volgende dag
/// komt. Dat is het soort fout waar iemand één keer op afgaat en daarna niet meer op de tip let.
///
/// Dus: zolang vandaag nog genoeg uren heeft, komt het venster van vandaag. Pas 's avonds, als
/// vandaag op is, kijken we vooruit.
/// </summary>
public sealed class ZonVensterTests
{
    // Donderdag 13 november 2026, 09:00 Nederlandse tijd (UTC+1).
    private static readonly DateTimeOffset Nu = new(2026, 11, 13, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Vandaag = new(2026, 11, 13);

    /// <summary>Een grijze dag vandaag, een stralende dag morgen.</summary>
    private static List<HourlyIrradiance> GrijsVandaagZonnigMorgen()
    {
        var uren = new List<HourlyIrradiance>();
        var middernacht = new DateTimeOffset(2026, 11, 12, 23, 0, 0, TimeSpan.Zero);

        for (var u = 0; u < 48; u++)
        {
            var moment = middernacht.AddHours(u);
            var lokaalUur = DutchTime.LocalHour(moment);
            var isDag = lokaalUur is >= 9 and <= 15;
            var morgen = u >= 24;

            uren.Add(new HourlyIrradiance(moment, isDag ? (morgen ? 500 : 120) : 0));
        }

        return uren;
    }

    private static AdviceInput Invoer(List<HourlyIrradiance> verwachting, DateTimeOffset? nu = null) => new(
        nu ?? Nu,
        [],
        HasPv: true,
        HasBattery: false,
        HasCharger: false,
        IsDynamic: false,
        new TariffRates(0.30m, 0.08m),
        AvgDailyChargedKwh: 0,
        AvgDailyExportKwh: 5,
        TypicalBatteryKwh: 0,
        Forecast: verwachting);

    [Fact]
    public void Overdag_komt_het_venster_van_vandaag()
    {
        var (van, _) = AdviceEngine.ZonnigsteVenster(Invoer(GrijsVandaagZonnigMorgen()), Vandaag);

        Assert.Equal(Vandaag, DutchTime.LocalDate(van));
    }

    [Fact]
    public void Is_vandaag_op_dan_pas_kijken_we_naar_morgen()
    {
        // Negen uur 's avonds: vandaag heeft geen zonuren meer.
        var avond = new DateTimeOffset(2026, 11, 13, 20, 0, 0, TimeSpan.Zero);

        var (van, _) = AdviceEngine.ZonnigsteVenster(Invoer(GrijsVandaagZonnigMorgen(), avond), Vandaag);

        Assert.Equal(Vandaag.AddDays(1), DutchTime.LocalDate(van));
    }

    [Fact]
    public void Het_venster_van_vandaag_ligt_rond_het_zonnigste_uur()
    {
        var uren = new List<HourlyIrradiance>();
        var middernacht = new DateTimeOffset(2026, 11, 12, 23, 0, 0, TimeSpan.Zero);

        for (var u = 0; u < 24; u++)
        {
            var moment = middernacht.AddHours(u);
            var lokaalUur = DutchTime.LocalHour(moment);
            // Piek om 13:00 lokaal.
            var straling = lokaalUur is >= 9 and <= 16 ? 400 - Math.Abs(13 - lokaalUur) * 80 : 0;
            uren.Add(new HourlyIrradiance(moment, Math.Max(0, straling)));
        }

        var (van, tot) = AdviceEngine.ZonnigsteVenster(Invoer(uren), Vandaag);

        Assert.InRange(DutchTime.LocalHour(van), 11, 13);
        Assert.Equal(3, (tot - van).TotalHours);
    }
}
