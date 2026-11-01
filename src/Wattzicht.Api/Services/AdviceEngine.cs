using System.Globalization;
using Wattzicht.Shared;

namespace Wattzicht.Api.Services;

/// <summary>Alles wat de advies-engine over een huishouden moet weten.</summary>
/// <param name="NowUtc">Huidig moment.</param>
/// <param name="Prices">Uurprijzen (markt), mag leeg zijn.</param>
/// <param name="HasPv">Zonnepanelen gekoppeld.</param>
/// <param name="HasBattery">Thuisbatterij gekoppeld.</param>
/// <param name="HasCharger">Laadpaal gekoppeld.</param>
/// <param name="IsDynamic">Dynamisch contract.</param>
/// <param name="Rates">Vast tarief (voor huishoudens zonder dynamisch contract).</param>
/// <param name="AvgDailyChargedKwh">Gemiddeld per dag in de auto geladen (laatste 14 dagen).</param>
/// <param name="AvgDailyExportKwh">Gemiddeld per dag teruggeleverd (laatste 14 dagen).</param>
/// <param name="TypicalBatteryKwh">Hoeveel de batterij op een dag ontlaadt (max van laatste 14 dagen).</param>
/// <param name="SurchargePerKwh">Energiebelasting + opslag incl. btw bovenop de marktprijs.</param>
/// <param name="Forecast">
/// Verwachte zonnestraling per uur. Mag leeg zijn (geen plaats bekend, weerdienst onbereikbaar); dan
/// valt het advies terug op de middaguren, zoals het hiervoor altijd deed.
/// </param>
/// <param name="PvPeakKw">Piekvermogen van de panelen, om straling naar kilowatturen te rekenen.</param>
public sealed record AdviceInput(
    DateTimeOffset NowUtc,
    IReadOnlyList<HourPrice> Prices,
    bool HasPv,
    bool HasBattery,
    bool HasCharger,
    bool IsDynamic,
    TariffRates Rates,
    double AvgDailyChargedKwh,
    double AvgDailyExportKwh,
    double TypicalBatteryKwh,
    decimal SurchargePerKwh = AdviceEngine.DefaultSurchargePerKwh,
    IReadOnlyList<HourlyIrradiance>? Forecast = null,
    double PvPeakKw = 4.0);

/// <summary>
/// Slim sturen v1: rekent uit wanneer je het beste laadt en verbruikt, en wat dat oplevert. Stuurt zelf nog niets aan.
/// <list type="bullet">
/// <item>Dynamisch contract: verschuiven van dure naar goedkope uren (prijsverschil × kWh).</item>
/// <item>Vast contract met zon: verbruik verschuiven naar 12–15 uur. Dat levert pas geld op als de saldering stopt (2027).</item>
/// <item>Negatieve marktprijs en zonnepanelen: waarschuwing om teruglevering te vermijden.</item>
/// </list>
/// </summary>
public static class AdviceEngine
{
    /// <summary>Energiebelasting 2026 + gemiddelde leveranciersopslag, incl. btw (schatting).</summary>
    public const decimal DefaultSurchargePerKwh = 0.13m;

    /// <summary>Aanname als er nog geen laadhistorie is.</summary>
    public const double DefaultChargeKwh = 10;

    /// <summary>Aanname als de batterij nog geen historie heeft.</summary>
    public const double DefaultBatteryKwh = 5;

    /// <summary>Wasmachine/vaatwasser per beurt.</summary>
    public const double ApplianceKwh = 1.0;

    /// <summary>Rendement laden + ontladen van een thuisbatterij.</summary>
    public const double BatteryRoundTrip = 0.9;

    /// <summary>Tips die minder opleveren dan dit tonen we niet (behalve de 2027-tips).</summary>
    public const decimal MinimumSavingEur = 0.05m;

    private const int WindowHours = 3;

    /// <summary>
    /// Het aaneengesloten venster van drie uur met de meeste verwachte zon, vanaf nu. Zijn de zonnige
    /// uren van vandaag voorbij of is er geen verwachting, dan wordt het de middag van de eerstvolgende
    /// dag waarop het nog kan.
    /// </summary>
    internal static (DateTimeOffset From, DateTimeOffset To) ZonnigsteVenster(AdviceInput input, DateOnly today)
    {
        var vanaf = input.NowUtc;
        var uren = (input.Forecast ?? [])
            .Where(u => u.StartUtc >= vanaf && u.WattPerM2 > 0)
            .OrderBy(u => u.StartUtc)
            .ToList();

        if (uren.Count >= WindowHours)
        {
            DateTimeOffset? beste = null;
            double besteSom = 0;

            foreach (var start in uren)
            {
                // Alleen echt aaneengesloten uren; een gat in de verwachting mag niet als venster tellen.
                var blok = uren.Where(u => u.StartUtc >= start.StartUtc
                                           && u.StartUtc < start.StartUtc.AddHours(WindowHours)).ToList();
                if (blok.Count < WindowHours) continue;

                var som = blok.Sum(u => u.WattPerM2);
                if (som > besteSom)
                {
                    besteSom = som;
                    beste = start.StartUtc;
                }
            }

            if (beste is { } gevonden)
            {
                return (gevonden, gevonden.AddHours(WindowHours));
            }
        }

        var from = DutchTime.StartOfLocalDayUtc(today).AddHours(12);
        var to = from.AddHours(WindowHours);
        if (input.NowUtc >= to)
        {
            // Zonnige uren van vandaag zijn voorbij: advies voor morgen.
            from = DutchTime.StartOfLocalDayUtc(today.AddDays(1)).AddHours(12);
            to = from.AddHours(WindowHours);
        }

        return (from, to);
    }

    /// <summary>
    /// Hoeveel kilowattuur de panelen in dit venster naar verwachting maken. Straling in watt per
    /// vierkante meter gedeeld door 1000 geeft het aandeel van het piekvermogen; dat is de gangbare
    /// vuistregel en nauwkeurig genoeg voor een tip. Zonder verwachting: nul, dan rekent de beller
    /// verder met het eigen gemiddelde.
    /// </summary>
    internal static double VerwachteKwh(AdviceInput input, DateTimeOffset from, DateTimeOffset to) =>
        (input.Forecast ?? [])
            .Where(u => u.StartUtc >= from && u.StartUtc < to)
            .Sum(u => u.WattPerM2 / 1000.0 * input.PvPeakKw);

    /// <summary>Wat de panelen vandaag naar verwachting in totaal maken.</summary>
    public static double VerwachtVandaagKwh(AdviceInput input, DateOnly today)
    {
        var begin = DutchTime.StartOfLocalDayUtc(today);
        return VerwachteKwh(input, begin, begin.AddDays(1));
    }
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    public static AdviceDto Build(AdviceInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var today = DutchTime.LocalDate(input.NowUtc);
        var currentHour = new DateTimeOffset(input.NowUtc.UtcDateTime.Date.AddHours(input.NowUtc.UtcDateTime.Hour), TimeSpan.Zero);
        var future = input.Prices
            .Where(p => p.StartUtc >= currentHour)
            .OrderBy(p => p.StartUtc)
            .ToList();

        var priceDtos = future
            .Select(p => new HourPriceDto(p.StartUtc, p.MarketEurPerKwh, AllIn(p, input.SurchargePerKwh)))
            .ToList();

        var cheapest = FindWindow(future, WindowHours, cheapest: true, input.SurchargePerKwh);
        var expensive = FindWindow(future, WindowHours, cheapest: false, input.SurchargePerKwh);

        var tips = new List<AdviceTipDto>();
        if (input.IsDynamic && cheapest is not null && expensive is not null)
        {
            AddDynamicTips(input, future, cheapest, expensive, tips);
        }
        else
        {
            AddFixedTips(input, today, tips);
        }

        AddNegativePriceTip(input, future, tips);

        tips = tips
            .OrderByDescending(t => t.SavingEur)
            .ToList();

        return new AdviceDto(
            today,
            input.IsDynamic,
            priceDtos,
            cheapest,
            expensive,
            tips,
            tips.Sum(t => t.SavingEur),
            future.Count > 0 ? "EnergyZero (day-ahead)" : null);
    }

    private static void AddDynamicTips(
        AdviceInput input,
        List<HourPrice> future,
        PriceWindowDto cheapest,
        PriceWindowDto expensive,
        List<AdviceTipDto> tips)
    {
        var s = input.SurchargePerKwh;
        var average = future.Average(p => AllIn(p, s));

        if (input.HasCharger)
        {
            var kwh = input.AvgDailyChargedKwh >= 1 ? input.AvgDailyChargedKwh : DefaultChargeKwh;

            // Referentie: zonder sturen prikt men de auto 's avonds in (17–23 uur).
            var evening = future.Where(p => DutchTime.LocalHour(p.StartUtc) is >= 17 and <= 22).ToList();
            var reference = evening.Count > 0 ? evening.Average(p => AllIn(p, s)) : average;
            var saving = Money((decimal)kwh * (reference - cheapest.AverageAllInEur));
            if (saving >= MinimumSavingEur)
            {
                tips.Add(new AdviceTipDto(
                    AdviceTipKind.Charger,
                    $"Laad de auto {Span(input.NowUtc, cheapest.From, cheapest.To)}",
                    $"Gemiddeld {Price(cheapest.AverageAllInEur)} per kWh, {kwh.ToString("0", Nl)} kWh",
                    cheapest.From,
                    cheapest.To,
                    saving));
            }
        }

        if (input.HasBattery)
        {
            var kwh = input.TypicalBatteryKwh >= 1 ? input.TypicalBatteryKwh : DefaultBatteryKwh;
            var charge = cheapest;
            var discharge = FindWindow(future.Where(p => p.StartUtc >= charge.To).ToList(), WindowHours, cheapest: false, s)
                ?? expensive;
            var costPerKwh = charge.AverageAllInEur / (decimal)BatteryRoundTrip;
            var saving = Money((decimal)kwh * (discharge.AverageAllInEur - costPerKwh));
            if (discharge.From >= charge.To && saving >= MinimumSavingEur)
            {
                tips.Add(new AdviceTipDto(
                    AdviceTipKind.Battery,
                    $"Batterij laden {Span(input.NowUtc, charge.From, charge.To)}",
                    $"En leeg gebruiken {Span(input.NowUtc, discharge.From, discharge.To)}, als stroom duur is",
                    charge.From,
                    discharge.To,
                    saving));
            }
        }

        var applianceWindow = FindWindow(future, 2, cheapest: true, s);
        if (applianceWindow is not null)
        {
            var saving = Money((decimal)ApplianceKwh * (average - applianceWindow.AverageAllInEur));
            if (saving >= MinimumSavingEur)
            {
                tips.Add(new AdviceTipDto(
                    AdviceTipKind.Appliance,
                    $"Wasmachine of vaatwasser om {Time(input.NowUtc, applianceWindow.From)}",
                    "Dan is stroom het goedkoopst",
                    applianceWindow.From,
                    applianceWindow.To,
                    saving));
            }
        }
    }

    /// <summary>Vast contract: verbruik naar de zonnige uren. Tot en met 2026 scheelt dat niets door de saldering.</summary>
    private static void AddFixedTips(AdviceInput input, DateOnly today, List<AdviceTipDto> tips)
    {
        if (!input.HasPv)
        {
            return;
        }

        // De zonnigste uren van vandaag, uit de echte verwachting. Zonder verwachting de middag, zoals
        // dit hiervoor altijd deed. Dat verschil doet ertoe: met een vast middagvenster zegt de app op
        // een grijze novemberdag precies hetzelfde als op een heldere dag in juni, en dan is het geen
        // advies meer maar een spreuk.
        var (from, to) = ZonnigsteVenster(input, today);

        var saldering = SavingsCalculator.IsSalderingActive(DutchTime.LocalDate(from));
        var gainPerKwh = Math.Max(0m, input.Rates.ImportPricePerKwh - input.Rates.ExportPricePerKwh);

        // Hoeveel er te verdelen valt: wat de verwachting voor dat venster zegt, of anders wat deze
        // woning de afgelopen twee weken gemiddeld terugleverde.
        var verwacht = VerwachteKwh(input, from, to);
        var surplus = verwacht > 0 ? verwacht : Math.Max(0, input.AvgDailyExportKwh);

        if (input.HasCharger)
        {
            var kwh = Math.Min(input.AvgDailyChargedKwh >= 1 ? input.AvgDailyChargedKwh : DefaultChargeKwh, surplus);
            if (kwh >= 0.5)
            {
                var value = Money((decimal)kwh * gainPerKwh);
                tips.Add(new AdviceTipDto(
                    AdviceTipKind.Charger,
                    $"Laad de auto {Span(input.NowUtc, from, to)}",
                    "Op je eigen zonnestroom in plaats van terugleveren",
                    from,
                    to,
                    saldering ? 0m : value,
                    saldering ? $"Vanaf 2027 {Euro(value)} per dag" : null));
            }
        }

        var applianceValue = Money((decimal)Math.Min(ApplianceKwh, surplus) * gainPerKwh);
        if (surplus >= 0.5)
        {
            tips.Add(new AdviceTipDto(
                AdviceTipKind.Appliance,
                $"Wasmachine of vaatwasser om {Time(input.NowUtc, from)}",
                "Draait dan op je eigen dak",
                from,
                to,
                saldering ? 0m : applianceValue,
                saldering ? $"Vanaf 2027 {Euro(applianceValue)} per beurt" : null));
        }
    }

    private static void AddNegativePriceTip(AdviceInput input, List<HourPrice> future, List<AdviceTipDto> tips)
    {
        if (!input.HasPv)
        {
            return;
        }

        var negative = future.Where(p => p.MarketEurPerKwh < 0).ToList();
        if (negative.Count == 0)
        {
            return;
        }

        // Eerste aaneengesloten blok negatieve uren.
        var from = negative[0].StartUtc;
        var to = from.AddHours(1);
        foreach (var p in negative.Skip(1))
        {
            if (p.StartUtc != to)
            {
                break;
            }

            to = to.AddHours(1);
        }

        tips.Add(new AdviceTipDto(
            AdviceTipKind.Export,
            $"Niet terugleveren {Span(input.NowUtc, from, to)}",
            "De prijs is dan negatief: verbruik of laad zelf zoveel mogelijk",
            from,
            to,
            0m,
            input.IsDynamic ? "Met een dynamisch contract kost terugleveren dan geld" : null));
    }

    /// <summary>Goedkoopste of duurste aaneengesloten blok van <paramref name="hours"/> uur.</summary>
    public static PriceWindowDto? FindWindow(IReadOnlyList<HourPrice> prices, int hours, bool cheapest, decimal surcharge)
    {
        PriceWindowDto? best = null;
        for (var i = 0; i + hours <= prices.Count; i++)
        {
            var block = prices.Skip(i).Take(hours).ToList();
            var consecutive = true;
            for (var j = 1; j < block.Count; j++)
            {
                if (block[j].StartUtc != block[j - 1].StartUtc.AddHours(1))
                {
                    consecutive = false;
                    break;
                }
            }

            if (!consecutive)
            {
                continue;
            }

            var avg = Math.Round(block.Average(p => AllIn(p, surcharge)), 4);
            if (best is null || (cheapest ? avg < best.AverageAllInEur : avg > best.AverageAllInEur))
            {
                best = new PriceWindowDto(block[0].StartUtc, block[^1].StartUtc.AddHours(1), avg);
            }
        }

        return best;
    }

    private static decimal AllIn(HourPrice p, decimal surcharge) => Math.Round(p.MarketEurPerKwh + surcharge, 4);

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string Euro(decimal value) => "€ " + value.ToString("0.00", Nl);

    private static string Price(decimal value) => ((int)Math.Round(value * 100)).ToString(Nl) + " ct";

    private static string Time(DateTimeOffset nowUtc, DateTimeOffset utc)
    {
        var local = DutchTime.ToLocal(utc);
        var prefix = DutchTime.LocalDate(utc) == DutchTime.LocalDate(nowUtc) ? string.Empty : "morgen ";
        return prefix + local.ToString("HH:mm", Nl);
    }

    private static string Span(DateTimeOffset nowUtc, DateTimeOffset from, DateTimeOffset to) =>
        $"{Time(nowUtc, from)}–{DutchTime.ToLocal(to):HH:mm}";
}
