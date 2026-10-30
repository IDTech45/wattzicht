using Wattzicht.Shared;

namespace Wattzicht.Api.Services;

/// <summary>Eén dag voor de prestatiecheck.</summary>
public sealed record PerformanceDay(DateOnly Date, double PvKwh, double? IrradianceKwhM2);

/// <summary>
/// Prestatiecheck zonder dat we het vermogen van de panelen hoeven te weten: de installatie is haar eigen maatstaf.
/// <list type="number">
/// <item>Per dag: opwek ÷ zonnestraling (kWh per kWh/m²).</item>
/// <item>Maatstaf: het 90e percentiel van die verhouding over de afgelopen weken ("zo goed doet hij het op een goede dag").</item>
/// <item>Laag: de laatste 3 zonnige dagen allemaal onder 70% van de maatstaf. Dan is er iets: vuil, schaduw of een defect.</item>
/// </list>
/// </summary>
public static class PerformanceAnalyzer
{
    public const int MinimumDays = 10;
    public const double MinIrradianceForRatio = 1.0;
    public const double SunnyDayIrradiance = 1.5;
    public const double LowThreshold = 0.70;

    public static PerformanceDto Analyze(IReadOnlyList<PerformanceDay> days, DateOnly today, decimal valuePerKwh)
    {
        var usable = days
            .Where(d => d.Date < today && d.IrradianceKwhM2 is >= MinIrradianceForRatio && d.PvKwh >= 0.1)
            .OrderBy(d => d.Date)
            .ToList();

        if (usable.Count < MinimumDays)
        {
            return new PerformanceDto(PerformanceStatus.Unknown, null, 0, 0m,
                "Nog te weinig dagen met zon en metingen voor een prestatiecheck.");
        }

        var ratios = usable.Select(d => d.PvKwh / d.IrradianceKwhM2!.Value).OrderBy(r => r).ToList();
        var baseline = Percentile(ratios, 0.9);

        // Recente zonnige dagen óók met (bijna) nul opwek: juist een stilgevallen omvormer moet "laag" geven.
        var recent = days
            .Where(d => d.Date < today && d.IrradianceKwhM2 is >= SunnyDayIrradiance)
            .OrderBy(d => d.Date)
            .TakeLast(3)
            .ToList();
        if (recent.Count < 3 || baseline <= 0)
        {
            return new PerformanceDto(PerformanceStatus.Unknown, null, 0, 0m,
                "Nog niet genoeg zonnige dagen om te vergelijken.");
        }

        var recentRatios = recent.Select(d => d.PvKwh / d.IrradianceKwhM2!.Value).ToList();
        var percent = (int)Math.Round(recentRatios.Average() / baseline * 100);

        var weekStart = today.AddDays(-7);
        var lost = days
            .Where(d => d.Date >= weekStart && d.Date < today && d.IrradianceKwhM2 is >= MinIrradianceForRatio)
            .Sum(d => Math.Max(0, baseline * d.IrradianceKwhM2!.Value - d.PvKwh));
        lost = Math.Round(lost, 1);

        var low = recentRatios.All(r => r < LowThreshold * baseline);
        return low
            ? new PerformanceDto(PerformanceStatus.Low, percent, lost, Math.Round((decimal)lost * valuePerKwh, 2),
                $"De laatste zonnige dagen leverden de panelen maar {percent}% van wat ze bij dit weer normaal doen. Mogelijk vuil, schaduw of een defect.")
            : new PerformanceDto(PerformanceStatus.Ok, Math.Min(percent, 100), lost, Math.Round((decimal)lost * valuePerKwh, 2),
                "De panelen leveren wat het weer toelaat.");
    }

    private static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }

        var index = (sorted.Count - 1) * p;
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        return lower == upper ? sorted[lower] : sorted[lower] + (sorted[upper] - sorted[lower]) * (index - lower);
    }
}
