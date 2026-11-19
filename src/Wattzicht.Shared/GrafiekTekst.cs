using System.Globalization;
using Wattzicht.Shared.Taal;

namespace Wattzicht.Shared;

/// <summary>Welke periode de grafiek laat zien. Staat hier omdat de teksten eraan hangen.</summary>
public enum GrafiekPeriode
{
    Dag,
    Week,
    Maand,
    Jaar,
}

/// <summary>Eén dag met wat erop gemeten is; genoeg om de grafiek en de zin eronder te maken.</summary>
public readonly record struct GrafiekDag(DateOnly Datum, double Waarde);

/// <summary>
/// De woorden bij de besparingsgrafiek: wat er onder de staven staat, en wat de grafiek je vertelt.
///
/// Dit stond eerder verspreid door de ViewModel en was daardoor niet te testen. De aanleiding om het
/// eruit te halen was een vraag van de eerste gebruiker op 19-11-2026, en die vraag was helemaal
/// terecht: "onderin staat 1 4 7 10 13, wat zijn dat, en hoeveel kWh is één staaf? Wat vertelt dit
/// dashboard mij?" Een rij kale getallen onder een rij blokjes is geen antwoord op een vraag die
/// iemand heeft. Een zin wel.
///
/// De regels die hier gelden:
/// - onder een staaf staat iets waaraan je ziet dat het een dag is (ma, di, wo, of "12 nov");
/// - een periode zonder meting zegt dat met zoveel woorden, en noemt de dagen die ontbreken;
/// - een getal krijgt altijd context: hoeveel, waarvan, en wanneer het het meest was.
/// </summary>
public static class GrafiekTekst
{
    /// <summary>
    /// Het opschrift onder één staaf.
    ///
    /// Bij een week de dagnaam: zeven staven, dus daar past "wo" onder en dat leest als een dag.
    /// Bij een maand het dagnummer, want dertig dagnamen onder elkaar is een muur; het eerste en het
    /// laatste label krijgen de maand erbij zodat het geen losse getallen blijven. Bij twee weken het
    /// dagnummer met de maand op de overgang, want anders telt 30, 31, 1, 2 nergens naartoe.
    /// </summary>
    public static string StaafLabel(DateOnly dag, GrafiekPeriode periode, bool eersteOfLaatste, string? taal = "nl")
    {
        var cultuur = Teksten.Cultuur(taal);
        var datum = new DateTime(dag.Year, dag.Month, dag.Day);

        return periode switch
        {
            GrafiekPeriode.Week => cultuur.DateTimeFormat.GetAbbreviatedDayName(datum.DayOfWeek).TrimEnd('.'),
            GrafiekPeriode.Jaar => cultuur.DateTimeFormat.GetAbbreviatedMonthName(dag.Month).TrimEnd('.'),
            _ when eersteOfLaatste || dag.Day == 1 => datum.ToString("d MMM", cultuur).TrimEnd('.'),
            _ => dag.Day.ToString(cultuur),
        };
    }

    /// <summary>De hele datum, voor wie een staaf aantikt of met een schermlezer leest.</summary>
    public static string VolleDatum(DateOnly dag, GrafiekPeriode periode, string? taal = "nl")
    {
        var cultuur = Teksten.Cultuur(taal);
        var datum = new DateTime(dag.Year, dag.Month, dag.Day);
        return periode == GrafiekPeriode.Jaar
            ? datum.ToString("MMMM yyyy", cultuur)
            : datum.ToString("dddd d MMMM", cultuur);
    }

    /// <summary>
    /// De dagen in het bereik waarvoor we niets hebben.
    ///
    /// Een dag die niet in de lijst staat is iets anders dan een dag met nul: de eerste betekent dat
    /// wij niet gekeken hebben, de tweede dat de zon niet scheen. Dat verschil zien is het halve werk
    /// bij een lege grafiek, en het stond nergens.
    /// </summary>
    public static IReadOnlyList<DateOnly> OntbrekendeDagen(DateOnly van, DateOnly tot, IEnumerable<GrafiekDag> dagen)
    {
        var aanwezig = dagen.Select(d => d.Datum).ToHashSet();
        var mist = new List<DateOnly>();
        for (var d = van; d <= tot; d = d.AddDays(1))
            if (!aanwezig.Contains(d)) mist.Add(d);
        return mist;
    }

    /// <summary>
    /// Wat deze grafiek je vertelt, in één zin.
    ///
    /// Drie gevallen, en ze zijn echt verschillend:
    /// 1. niets gemeten in deze periode, en dan ook nog eens dagen die helemaal ontbreken;
    /// 2. wel gemeten, maar alles nul (winter, sneeuw op de panelen, een dag die net begint);
    /// 3. gewoon cijfers, en dan wil je het totaal plus de beste dag.
    /// </summary>
    public static string Samenvatting(
        GrafiekPeriode periode,
        IReadOnlyList<GrafiekDag> dagen,
        DateOnly van,
        DateOnly tot,
        bool inKwh,
        string? taal = "nl")
    {
        var cultuur = Teksten.Cultuur(taal);
        string T(string sleutel) => Teksten.T(taal, sleutel);

        var totaal = dagen.Sum(d => d.Waarde);
        var mist = OntbrekendeDagen(van, tot, dagen);

        if (dagen.Count == 0 || totaal <= 0)
        {
            // Ontbrekende dagen zijn hier het hele verhaal: dan ligt het aan ons, niet aan de zon.
            if (mist.Count > 0)
                return string.Format(T("grafiek.niets-gemeten-dagen"), DagenLijst(mist, cultuur, T));

            return T(periode switch
            {
                GrafiekPeriode.Dag => "grafiek.niets-vandaag",
                GrafiekPeriode.Week => "grafiek.niets-deze-week",
                GrafiekPeriode.Maand => "grafiek.niets-deze-maand",
                _ => "grafiek.niets-dit-jaar",
            });
        }

        var beste = dagen.OrderByDescending(d => d.Waarde).First();
        var zin = string.Format(
            T(periode switch
            {
                GrafiekPeriode.Dag => "grafiek.totaal-dagen",
                GrafiekPeriode.Week => "grafiek.totaal-week",
                GrafiekPeriode.Maand => "grafiek.totaal-maand",
                _ => "grafiek.totaal-jaar",
            }),
            Bedrag(totaal, inKwh, cultuur),
            Bedrag(beste.Waarde, inKwh, cultuur),
            KorteDatum(beste.Datum, periode, cultuur));

        if (mist.Count > 0)
            zin += " " + string.Format(T("grafiek.dagen-ontbreken"), DagenLijst(mist, cultuur, T));

        return zin;
    }

    /// <summary>Een bedrag of een hoeveelheid, in de notatie van de taal.</summary>
    public static string Bedrag(double waarde, bool inKwh, CultureInfo cultuur) =>
        inKwh
            ? waarde.ToString("0.#", cultuur) + " kWh"
            : "€ " + waarde.ToString("0.00", cultuur);

    private static string KorteDatum(DateOnly dag, GrafiekPeriode periode, CultureInfo cultuur)
    {
        var datum = new DateTime(dag.Year, dag.Month, dag.Day);
        return periode == GrafiekPeriode.Jaar
            ? datum.ToString("MMMM", cultuur)
            : datum.ToString("d MMMM", cultuur);
    }

    /// <summary>
    /// "17 en 18 november", of bij meer dan drie dagen een aantal. Drie datums naast elkaar leest
    /// nog; zeven is een opsomming waar niemand doorheen komt.
    /// </summary>
    private static string DagenLijst(IReadOnlyList<DateOnly> dagen, CultureInfo cultuur, Func<string, string> T)
    {
        if (dagen.Count > 3)
            return string.Format(T("grafiek.aantal-dagen"), dagen.Count);

        var namen = dagen
            .Select(d => new DateTime(d.Year, d.Month, d.Day).ToString("d MMMM", cultuur))
            .ToList();

        if (namen.Count == 1) return namen[0];
        return string.Join(", ", namen.Take(namen.Count - 1)) + " " + T("grafiek.en") + " " + namen[^1];
    }
}
