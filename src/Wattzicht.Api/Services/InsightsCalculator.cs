using Wattzicht.Shared;

namespace Wattzicht.Api.Services;

/// <summary>Daggegevens die de duurzaamheid- en kansenberekening nodig hebben.</summary>
public sealed record InsightDay(
    DateOnly Date,
    double PvKwh,
    double SelfUseKwh,
    double ExportKwh,
    double ImportKwh,
    double ChargedKwh,
    double BatteryDischargedKwh);

/// <summary>Duurzaamheid voor de klant en kansen voor de installateur, uit de dagtotalen.</summary>
public static class InsightsCalculator
{
    /// <summary>CO₂ per kWh stroom uit het Nederlandse net (kg). Schatting; instelbaar via Sustainability:GridCo2KgPerKwh.</summary>
    public const double DefaultGridCo2KgPerKwh = 0.27;

    /// <summary>Wat een thuisbatterij per dag maximaal kan verschuiven (aanname bij ~10 kWh capaciteit).</summary>
    public const double BatteryShiftCapKwh = 8;

    public static SustainabilityDto Sustainability(
        int year,
        IReadOnlyList<InsightDay> yearDays,
        PerformanceDto performance,
        double co2KgPerKwh = DefaultGridCo2KgPerKwh)
    {
        var pv = yearDays.Sum(d => d.PvKwh);
        var solarUsed = yearDays.Sum(d => d.SelfUseKwh + d.BatteryDischargedKwh);
        var consumption = solarUsed + yearDays.Sum(d => d.ImportKwh);
        var export = yearDays.Sum(d => d.ExportKwh);

        return new SustainabilityDto(
            year,
            Math.Round(pv, 0),
            Math.Round(pv * co2KgPerKwh, 0),
            consumption > 0 ? (int)Math.Round(Math.Min(1, solarUsed / consumption) * 100) : 0,
            pv > 0 ? (int)Math.Round(Math.Clamp((pv - export) / pv, 0, 1) * 100) : 0,
            performance);
    }

    /// <summary>
    /// Kansen per klant, op basis van de laatste 30 dagen. Bedragen gaan over 2027 en later (na de saldering),
    /// want dan pas telt elke teruggeleverde kWh echt.
    /// </summary>
    public static List<OpportunityDto> Opportunities(
        IReadOnlyList<InsightDay> last30Days,
        bool hasBattery,
        bool hasCharger,
        bool isDynamic,
        TariffRates rates,
        PerformanceDto performance)
    {
        var list = new List<OpportunityDto>();
        var worth2027 = FeedInCalculator.SelfUseWorthPerKwh(rates, new DateOnly(2027, 1, 1));

        if (performance.Status == PerformanceStatus.Low)
        {
            list.Add(new OpportunityDto(
                OpportunityKind.Inspection,
                "Controle of schoonmaak",
                $"Levert {performance.PercentOfExpected}% van wat het weer toelaat; ~{performance.LostKwh7Days:0} kWh gemist deze week.",
                null));
        }

        if (last30Days.Count >= 7)
        {
            var days = last30Days.Count;
            var avgExport = last30Days.Sum(d => d.ExportKwh) / days;
            var avgCharged = last30Days.Sum(d => d.ChargedKwh) / days;

            if (!hasBattery && avgExport >= 4)
            {
                var shift = Math.Min(avgExport, BatteryShiftCapKwh) * 365 * 0.9;
                list.Add(new OpportunityDto(
                    OpportunityKind.Battery,
                    "Thuisbatterij",
                    $"Levert gemiddeld {avgExport:0.0} kWh per dag terug aan het net.",
                    Math.Round((decimal)shift * worth2027, 0)));
            }

            if (hasCharger && avgCharged >= 2 && avgExport >= 2)
            {
                var shift = Math.Min(avgCharged, avgExport) * 365;
                list.Add(new OpportunityDto(
                    OpportunityKind.SmartCharging,
                    "Slim laden op zon",
                    $"Laadt {avgCharged:0.0} kWh per dag en levert {avgExport:0.0} kWh terug: dat kan grotendeels op eigen zon.",
                    Math.Round((decimal)shift * worth2027, 0)));
            }
        }

        if (hasBattery && !isDynamic)
        {
            list.Add(new OpportunityDto(
                OpportunityKind.DynamicContract,
                "Dynamisch contract",
                "Met een batterij en uurprijzen goedkoop laden en duur zelf gebruiken.",
                null));
        }

        return list;
    }
}
