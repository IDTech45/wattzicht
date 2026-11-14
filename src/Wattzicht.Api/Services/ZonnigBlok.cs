using Wattzicht.Shared;

namespace Wattzicht.Api.Services;

/// <summary>
/// Het blok uren waarin de zon op een dag echt doorzet.
///
/// Hier zit het verschil met <see cref="AdviceEngine.ZonnigsteVenster"/>: die zoekt het beste venster
/// van de komende twee dagen en valt terug op de middaguren als er geen verwachting is, want hij moet
/// altijd een advies kunnen geven. Dit is het omgekeerde. Een melding de avond ervoor mag alleen uit
/// als er echt iets te melden valt, dus levert deze liever niets dan een gok.
///
/// Waarom de grens relatief is en niet een vast aantal watt: 300 W/m² is in november een stralende
/// middag en in juni een grijze. Een vaste grens zou de melding een half jaar nooit sturen en het
/// andere half jaar elke dag. De drempel die wel absoluut is, staat aan het eind: wat de panelen van
/// dít dak in dat blok maken. Dat is het getal waar de bewoner iets aan heeft, want daar laadt hij
/// zijn auto mee.
/// </summary>
public static class ZonnigBlok
{
    /// <summary>Korter dan dit is geen blok maar een opening in de bewolking.</summary>
    public const int MinUren = 3;

    /// <summary>Langer melden heeft geen zin: "tussen 9 en 18" is geen tip maar een weerbericht.</summary>
    public const int MaxUren = 6;

    /// <summary>
    /// Onder deze opbrengst zwijgen we. Drie kilowattuur is ruwweg twintig kilometer rijden; daar
    /// verzet iemand zijn dag voor, voor één kilowattuur niet.
    /// </summary>
    public const double MinimumKwh = 3.0;

    /// <summary>
    /// Het zonnige blok van één dag, of <c>null</c> als die dag er geen heeft.
    /// </summary>
    /// <param name="verwachting">Straling per uur, zoals de weerdienst hem geeft (mag langer zijn dan één dag).</param>
    /// <param name="dag">De dag waar we naar kijken, in Nederlandse tijd.</param>
    /// <param name="pvPiekKw">Piekvermogen van de panelen in kWp.</param>
    public static ZonBlokDto? VoorDag(
        IReadOnlyList<HourlyIrradiance> verwachting, DateOnly dag, double pvPiekKw)
    {
        if (pvPiekKw <= 0)
        {
            return null;
        }

        var uren = verwachting
            .Where(u => u.WattPerM2 > 0 && DutchTime.LocalDate(u.StartUtc) == dag)
            .OrderBy(u => u.StartUtc)
            .ToList();

        if (uren.Count < MinUren)
        {
            return null;
        }

        // Het hele blok moet boven de grens blijven. Een fel uur tussen grijze uren aanvullen tot
        // drie zou "morgen zon tussen 11 en 14" opleveren terwijl er twee van die drie uren niets
        // gebeurt, en op zo'n melding gaat iemand één keer af.
        var grens = uren.Max(u => u.WattPerM2) * 0.5;
        var kern = LangsteReeks(uren, grens);
        if (kern.Count < MinUren)
        {
            return null;
        }

        if (kern.Count > MaxUren)
        {
            kern = BesteVenster(kern, MaxUren);
        }

        var kwh = kern.Sum(u => u.WattPerM2 / 1000.0 * pvPiekKw);
        if (kwh < MinimumKwh)
        {
            return null;
        }

        var van = DutchTime.LocalHour(kern[0].StartUtc);
        var tot = DutchTime.LocalHour(kern[^1].StartUtc.AddHours(1));

        // Een blok dat tot middernacht loopt bestaat niet met zon erin, maar als de verwachting ooit
        // zo aankomt is 0 uur als eindtijd onleesbaar; dan staat er 24.
        return new ZonBlokDto(van, tot == 0 ? 24 : tot, Math.Round(kwh, 1));
    }

    /// <summary>
    /// De langste rij aaneengesloten uren die boven de grens blijft.
    ///
    /// Aaneengesloten is hier letterlijk: de uren moeten precies een uur na elkaar beginnen. Een gat
    /// in de verwachting mag geen blok worden, want dan noemen we een tijd waarvan we het midden niet
    /// kennen.
    /// </summary>
    private static List<HourlyIrradiance> LangsteReeks(List<HourlyIrradiance> uren, double grens)
    {
        List<HourlyIrradiance> beste = [];
        List<HourlyIrradiance> huidig = [];

        foreach (var uur in uren)
        {
            var sluitAan = huidig.Count > 0 && huidig[^1].StartUtc.AddHours(1) == uur.StartUtc;
            if (uur.WattPerM2 < grens)
            {
                huidig = [];
                continue;
            }

            huidig = sluitAan ? [.. huidig, uur] : [uur];
            if (huidig.Sum(u => u.WattPerM2) > beste.Sum(u => u.WattPerM2))
            {
                beste = huidig;
            }
        }

        return beste;
    }

    /// <summary>Het zonnigste venster van <paramref name="lengte"/> uren binnen een langer blok.</summary>
    private static List<HourlyIrradiance> BesteVenster(List<HourlyIrradiance> blok, int lengte)
    {
        var beste = blok.Take(lengte).ToList();
        for (var i = 1; i + lengte <= blok.Count; i++)
        {
            var venster = blok.Skip(i).Take(lengte).ToList();
            if (venster.Sum(u => u.WattPerM2) > beste.Sum(u => u.WattPerM2))
            {
                beste = venster;
            }
        }

        return beste;
    }
}
