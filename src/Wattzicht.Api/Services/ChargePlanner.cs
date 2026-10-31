using Wattzicht.Shared;

namespace Wattzicht.Api.Services;

/// <summary>Wat de planner moet weten om de beste laaduren te kiezen.</summary>
/// <param name="NowUtc">Nu.</param>
/// <param name="DeadlineUtc">Wanneer de auto vol moet zijn.</param>
/// <param name="NeededKwh">Hoeveel er nog in moet.</param>
/// <param name="ChargerKw">Vermogen van de laadpaal.</param>
/// <param name="Prices">Uurprijzen (markt). Leeg = geen dynamisch contract of bron onbereikbaar.</param>
/// <param name="Forecast">Verwachte zonnestraling per uur.</param>
/// <param name="PvPeakKw">Piekvermogen van de zonnepanelen, voor het omrekenen van straling naar kWh.</param>
/// <param name="IsDynamic">Dynamisch contract: dan telt het prijsverschil per uur.</param>
/// <param name="Rates">Vast tarief, voor wie geen dynamisch contract heeft.</param>
/// <param name="SurchargePerKwh">Energiebelasting en opslag bovenop de marktprijs.</param>
public sealed record ChargePlanInput(
    DateTimeOffset NowUtc,
    DateTimeOffset DeadlineUtc,
    double NeededKwh,
    double ChargerKw,
    IReadOnlyList<HourPrice> Prices,
    IReadOnlyList<HourlyIrradiance> Forecast,
    double PvPeakKw,
    bool IsDynamic,
    TariffRates Rates,
    decimal SurchargePerKwh = AdviceEngine.DefaultSurchargePerKwh);

/// <summary>
/// Wanneer laad je je auto het voordeligst?
///
/// De adviesmotor vertelt wat je vandaag zou kunnen doen. Deze planner beantwoordt de concretere
/// vraag die een bewoner echt heeft: ik moet er morgenvroeg om zeven uur 30 kWh in hebben, wanneer
/// zet ik hem aan het laden? Dat is iets anders dan "het goedkoopste uur", want er spelen twee
/// dingen tegelijk.
///
/// Het eerste is de prijs. Met een dynamisch contract scheelt het echt geld welke uren je pakt.
///
/// Het tweede is de zon, en dat is de reden dat hier een zonverwachting in zit. Een kilowattuur dat
/// je uit je eigen panelen haalt kost je de terugleververgoeding die je misloopt, en dat is bijna
/// altijd minder dan wat je voor stroom van het net betaalt. Zolang er gesaldeerd wordt scheelt dat
/// weinig, maar vanaf 2027 is dat het verschil tussen een paar cent en de volle prijs. Een planner
/// die alleen naar de prijs kijkt zou je auto dan om drie uur 's nachts laten laden terwijl je dak
/// om twee uur 's middags gratis stroom staat weg te geven.
///
/// Rekent alleen, stuurt niets aan. Wat er met het advies gebeurt, bepaalt de bewoner.
/// </summary>
public static class ChargePlanner
{
    /// <summary>Hoeveel kWh een uur vol zon per kWp oplevert. 1000 W/m² is de standaard testwaarde.</summary>
    public const double ReferenceIrradiance = 1000d;

    /// <summary>Verlies tussen paneel en accu: omvormer, kabels, de laadverliezen van de auto zelf.</summary>
    public const double SystemEfficiency = 0.85d;

    /// <summary>Onder deze straling (W/m²) rekenen we niet op zon; dat is schemer.</summary>
    public const double MinUsefulIrradiance = 60d;

    /// <summary>Eén blokje in het plan: een uur waarin geladen wordt.</summary>
    /// <param name="StartUtc">Begin van het uur.</param>
    /// <param name="Kwh">Hoeveel er dat uur in gaat.</param>
    /// <param name="SolarKwh">Daarvan uit eigen zon.</param>
    /// <param name="CostEur">Wat dat uur kost.</param>
    public sealed record Slot(DateTimeOffset StartUtc, double Kwh, double SolarKwh, decimal CostEur);

    /// <summary>De uitkomst: welke uren, wat het kost, en wat je bespaart door niet meteen te laden.</summary>
    /// <param name="Slots">De gekozen uren, op tijd gesorteerd.</param>
    /// <param name="TotalKwh">Hoeveel er in totaal geladen wordt.</param>
    /// <param name="CostEur">Wat het plan kost.</param>
    /// <param name="CostIfStartNowEur">Wat het zou kosten als je nu aan de stekker gaat.</param>
    /// <param name="SavingEur">Het verschil.</param>
    /// <param name="SolarKwh">Hoeveel daarvan van het eigen dak komt.</param>
    /// <param name="Feasible">Of het binnen de tijd past.</param>
    /// <param name="Explanation">Eén zin voor in de app.</param>
    public sealed record Plan(
        IReadOnlyList<Slot> Slots,
        double TotalKwh,
        decimal CostEur,
        decimal CostIfStartNowEur,
        decimal SavingEur,
        double SolarKwh,
        bool Feasible,
        string Explanation);

    public static Plan Build(ChargePlanInput input)
    {
        var nodig = Math.Max(0, input.NeededKwh);
        var perUur = Math.Max(0.1, input.ChargerKw);
        var uren = BeschikbareUren(input).ToList();

        if (nodig <= 0 || uren.Count == 0)
        {
            return new Plan([], 0, 0, 0, 0, 0, nodig <= 0,
                nodig <= 0 ? "Er hoeft niets geladen te worden." : "Er is geen tijd meer voor de deadline.");
        }

        // Goedkoopste uren eerst, en bij gelijke prijs het uur dat eerder valt: dan staat de auto
        // eerder vol en heb je ruimte als er iets tegenzit.
        var gekozen = new List<Slot>();
        var rest = nodig;
        foreach (var uur in uren.OrderBy(u => u.PrijsPerKwh).ThenBy(u => u.StartUtc))
        {
            if (rest <= 0.001)
            {
                break;
            }

            var kwh = Math.Min(perUur, rest);
            rest -= kwh;
            var zon = Math.Min(kwh, uur.ZonKwh);
            gekozen.Add(new Slot(uur.StartUtc, Afronden(kwh), Afronden(zon),
                Geld(uur.PrijsPerKwh * (decimal)kwh)));
        }

        var geladen = gekozen.Sum(s => s.Kwh);
        var kosten = gekozen.Sum(s => s.CostEur);
        var nuKosten = KostenAlsJeNuBegint(uren, nodig, perUur);
        var zonTotaal = gekozen.Sum(s => s.SolarKwh);
        var haalbaar = rest <= 0.001;

        return new Plan(
            gekozen.OrderBy(s => s.StartUtc).ToList(),
            Afronden(geladen),
            Geld(kosten),
            Geld(nuKosten),
            Geld(nuKosten - kosten),
            Afronden(zonTotaal),
            haalbaar,
            Uitleg(gekozen, haalbaar, nuKosten - kosten, zonTotaal));
    }

    /// <summary>Elk heel uur tussen nu en de deadline, met de prijs en de verwachte zon erbij.</summary>
    private static IEnumerable<Uur> BeschikbareUren(ChargePlanInput input)
    {
        var start = new DateTimeOffset(input.NowUtc.UtcDateTime, TimeSpan.Zero)
            .AddMinutes(-input.NowUtc.Minute).AddSeconds(-input.NowUtc.Second)
            .AddMilliseconds(-input.NowUtc.Millisecond);

        for (var uur = start; uur < input.DeadlineUtc; uur = uur.AddHours(1))
        {
            var zonKwh = ZonInDitUur(input, uur);

            // Wat een kilowattuur dit uur kost. Zon van eigen dak kost je alleen wat je er anders
            // voor terug had gekregen; de rest koop je in tegen de prijs van dat uur.
            var netPrijs = input.IsDynamic
                ? MarktPrijs(input, uur) + input.SurchargePerKwh
                : input.Rates.ImportPricePerKwh;
            var zonPrijs = input.Rates.ExportPricePerKwh;

            var aandeelZon = zonKwh <= 0 ? 0 : Math.Min(1, zonKwh / Math.Max(0.1, input.ChargerKw));
            var gemengd = ((decimal)aandeelZon * zonPrijs) + ((decimal)(1 - aandeelZon) * netPrijs);

            yield return new Uur(uur, gemengd, zonKwh);
        }
    }

    /// <summary>Wat de panelen dit uur naar verwachting opleveren, in kWh.</summary>
    private static double ZonInDitUur(ChargePlanInput input, DateTimeOffset uur)
    {
        if (input.PvPeakKw <= 0)
        {
            return 0;
        }

        var straling = input.Forecast.FirstOrDefault(f => f.StartUtc == uur)?.WattPerM2 ?? 0;
        if (straling < MinUsefulIrradiance)
        {
            return 0;
        }

        return input.PvPeakKw * (straling / ReferenceIrradiance) * SystemEfficiency;
    }

    private static decimal MarktPrijs(ChargePlanInput input, DateTimeOffset uur)
    {
        var prijs = input.Prices.FirstOrDefault(p => p.StartUtc == uur);
        // Geen prijs bekend voor dit uur (verder weg dan de dag-vooruitmarkt): dan rekenen we met het
        // gemiddelde van wat we wél weten. Beter een schatting dan het uur overslaan.
        return prijs?.MarketEurPerKwh
               ?? (input.Prices.Count > 0 ? input.Prices.Average(p => p.MarketEurPerKwh) : 0m);
    }

    /// <summary>Wat het kost als je de stekker er nu in steekt en doorlaadt tot hij vol is.</summary>
    private static decimal KostenAlsJeNuBegint(IReadOnlyList<Uur> uren, double nodig, double perUur)
    {
        var rest = nodig;
        var totaal = 0m;
        foreach (var uur in uren)
        {
            if (rest <= 0.001)
            {
                break;
            }

            var kwh = Math.Min(perUur, rest);
            rest -= kwh;
            totaal += uur.PrijsPerKwh * (decimal)kwh;
        }

        return totaal;
    }

    private static string Uitleg(IReadOnlyList<Slot> slots, bool haalbaar, decimal besparing, double zon)
    {
        if (slots.Count == 0)
        {
            return "Er is geen tijd meer voor de deadline.";
        }

        var eerste = slots.Min(s => s.StartUtc);
        var tijd = DutchTime.LocalHour(eerste).ToString("00") + ":00";
        var delen = new List<string> { $"Begin om {tijd}" };

        if (zon >= 0.5)
        {
            delen.Add($"dan gaat er ongeveer {zon:0.#} kWh rechtstreeks van je eigen dak in de auto");
        }

        if (besparing >= 0.05m)
        {
            delen.Add($"dat scheelt € {besparing:0.00} ten opzichte van nu beginnen");
        }

        var zin = string.Join(", ", delen) + ".";
        return haalbaar ? zin : zin + " Let op: binnen deze tijd past hij niet helemaal vol.";
    }

    private static double Afronden(double v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private static decimal Geld(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private sealed record Uur(DateTimeOffset StartUtc, decimal PrijsPerKwh, double ZonKwh);
}
