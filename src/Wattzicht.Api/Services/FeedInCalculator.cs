using System.Globalization;
using Wattzicht.Shared;

namespace Wattzicht.Api.Services;

/// <summary>
/// Rekent uit wat terugleveren kost en wat zelf gebruiken scheelt.
/// <para>
/// Zelf gebruiken in plaats van terugleveren scheelt per kWh: leveringstarief − (wat terugleveren oplevert − terugleverkosten).
/// Tijdens de saldering levert terugleveren het leveringstarief op, dus scheelt zelf gebruiken precies de terugleverkosten.
/// Vanaf 2027 levert terugleveren alleen de terugleververgoeding op en scheelt zelf gebruiken veel meer.
/// </para>
/// </summary>
public static class FeedInCalculator
{
    /// <summary>Vanaf dit vermogen terug naar het net tonen we een "nu"-tip.</summary>
    public const double LiveTipThresholdW = 1500;

    /// <summary>Metingen ouder dan dit gebruiken we niet voor een live-tip.</summary>
    public static readonly TimeSpan LiveMaxAge = TimeSpan.FromMinutes(15);

    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    /// <summary>Wat één kWh zelf gebruiken in plaats van terugleveren op <paramref name="date"/> scheelt.</summary>
    public static decimal SelfUseWorthPerKwh(TariffRates rates, DateOnly date)
    {
        var exportValue = SavingsCalculator.IsSalderingActive(date) ? rates.ImportPricePerKwh : rates.ExportPricePerKwh;
        return Math.Round(rates.ImportPricePerKwh - (exportValue - rates.FeedInCostPerKwh), 4);
    }

    /// <summary>Teruglevering in watt uit een live-meting, alleen als die vers is.</summary>
    public static double LiveExportW(LiveSnapshotDto? live, DateTimeOffset now) =>
        live?.UpdatedAt is { } at && now - at <= LiveMaxAge && live.GridPowerW < 0
            ? Math.Round(-live.GridPowerW)
            : 0;

    /// <param name="today">Vandaag (Nederlandse tijd).</param>
    /// <param name="rates">Huidig tarief.</param>
    /// <param name="monthExportKwh">Teruggeleverd deze maand.</param>
    /// <param name="exportKwhLastYear">Som teruglevering over de beschikbare dagen van de laatste 365.</param>
    /// <param name="daysWithData">Aantal dagen waarover die som gaat.</param>
    /// <param name="liveExportW">Huidige teruglevering in watt.</param>
    public static FeedInDto Build(
        DateOnly today,
        TariffRates rates,
        double monthExportKwh,
        double exportKwhLastYear,
        int daysWithData,
        double liveExportW)
    {
        var isEstimate = daysWithData is > 0 and < 365;
        var yearKwh = daysWithData <= 0
            ? 0
            : isEstimate ? exportKwhLastYear / daysWithData * 365 : exportKwhLastYear;
        yearKwh = Math.Round(yearKwh, 0);

        var worthNow = SelfUseWorthPerKwh(rates, today);
        var worth2027 = SelfUseWorthPerKwh(rates, new DateOnly(2027, 1, 1));

        return new FeedInDto(
            rates.FeedInCostPerKwh,
            Math.Round(monthExportKwh, 1),
            Money((decimal)monthExportKwh * rates.FeedInCostPerKwh),
            yearKwh,
            isEstimate,
            Money((decimal)yearKwh * rates.FeedInCostPerKwh),
            worthNow,
            worth2027,
            Money((decimal)yearKwh * worth2027),
            liveExportW);
    }

    /// <summary>
    /// "Nu"-tip als er flink teruggeleverd wordt: eerst de auto (grootste verbruiker), anders een wasbeurt.
    /// <c>null</c> als er niets of te weinig teruggaat.
    /// </summary>
    public static AdviceTipDto? LiveSurplusTip(LiveSnapshotDto? live, DateTimeOffset now, bool hasCharger, TariffRates rates)
    {
        var exportW = LiveExportW(live, now);
        if (exportW < LiveTipThresholdW)
        {
            return null;
        }

        // Scheelt zelf gebruiken nu niets (saldering en geen terugleverkosten ingevuld)? Dan geen tip.
        var worth = SelfUseWorthPerKwh(rates, DutchTime.LocalDate(now));
        if (worth <= 0)
        {
            return null;
        }

        var kw = (exportW / 1000).ToString("0.0", Nl);
        var perHour = Money((decimal)(exportW / 1000) * worth);
        var carIdle = hasCharger && (live?.ChargerPowerW ?? 0) < 100;

        return new AdviceTipDto(
            AdviceTipKind.Export,
            carIdle ? "Nu de auto laden" : "Nu de wasmachine of vaatwasser aan",
            $"Je levert nu {kw} kW terug aan het net",
            now,
            null,
            0m,
            perHour > 0 ? $"Zelf gebruiken scheelt nu ± € {perHour.ToString("0.00", Nl)} per uur" : null);
    }

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
