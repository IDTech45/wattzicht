using System.Globalization;

namespace Wattzicht.Api.Services;

/// <summary>
/// Leest een export uit het portaal van een fabrikant en maakt er dagtotalen van.
///
/// Waarom dit bestaat: wij beginnen te meten op de dag dat iemand koppelt. Wie zijn panelen in 2019
/// heeft laten leggen ziet dus een leeg jaaroverzicht, en dat voelt als een app die niet werkt. Bij
/// Enphase is dat extra zuur: hun eigen portaal heeft die jaren wél, en je kunt ze daar als CSV
/// downloaden. Eén bestand uploaden is dan genoeg.
///
/// De opzet is met opzet soepel. Elk portaal exporteert anders, en dezelfde fabrikant doet het per
/// land weer net iets anders: punt of komma als decimaalteken, puntkomma of komma als scheidingsteken,
/// Wh of kWh, per uur of per dag, een kop in het Engels of in het Nederlands. In plaats van één
/// formaat voorschrijven zoeken we de datumkolom en de energiekolom op hun kop, en tellen we per dag
/// op. Lukt dat niet, dan zeggen we welke koppen we wél zagen: dan weet degene die het bestand heeft
/// meteen wat er anders is, in plaats van "ongeldig bestand".
/// </summary>
public static class HistorieImport
{
    /// <summary>Eén dag uit de export.</summary>
    public sealed record Dag(DateOnly Datum, double Kwh);

    /// <summary>Wat er uit een bestand kwam.</summary>
    /// <param name="Dagen">De dagtotalen, op datum gesorteerd.</param>
    /// <param name="Fout">Leeg als het gelukt is; anders een uitleg voor de gebruiker.</param>
    /// <param name="GezieneKoppen">De kolomkoppen die we tegenkwamen; staat in de foutmelding.</param>
    public sealed record Uitkomst(IReadOnlyList<Dag> Dagen, string? Fout, IReadOnlyList<string> GezieneKoppen)
    {
        public bool Gelukt => Fout is null && Dagen.Count > 0;

        public static Uitkomst Mislukt(string fout, IReadOnlyList<string>? koppen = null) =>
            new([], fout, koppen ?? []);
    }

    /// <summary>Woorden die een datumkolom verraden.</summary>
    private static readonly string[] DatumWoorden = ["date", "datum", "time", "tijd", "dag", "day"];

    /// <summary>Woorden die een kolom met opgewekte energie verraden.</summary>
    private static readonly string[] EnergieWoorden =
        ["energy produced", "produced", "production", "productie", "opwek", "opbrengst", "yield", "generation", "pv"];

    /// <summary>Zonder bovengrens kan iemand met één bestand het halve geheugen opeten.</summary>
    public const int MaximumRegels = 200_000;

    /// <summary>Verder terug dan dit bewaren we niet: zonnepanelen bestaan, maar deze app niet.</summary>
    public static readonly DateOnly VroegsteDatum = new(2000, 1, 1);

    public static Uitkomst Lees(string inhoud, DateOnly vandaag)
    {
        if (string.IsNullOrWhiteSpace(inhoud))
        {
            return Uitkomst.Mislukt("Het bestand is leeg.");
        }

        var regels = inhoud.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (regels.Length > MaximumRegels)
        {
            return Uitkomst.Mislukt($"Dit bestand heeft meer dan {MaximumRegels:N0} regels. Exporteer per jaar.");
        }

        // De kopregel staat niet altijd bovenaan: Enphase zet er soms eerst een paar regels met de
        // naam van het systeem boven. Daarom zoeken we de eerste regel die een datum- én een
        // energiekolom heeft.
        var koppen = new List<string>();
        for (var i = 0; i < Math.Min(regels.Length, 20); i++)
        {
            var scheidingsteken = Scheidingsteken(regels[i]);
            var velden = Splits(regels[i], scheidingsteken);
            var datumKolom = Zoek(velden, DatumWoorden);
            var energieKolom = Zoek(velden, EnergieWoorden);

            foreach (var veld in velden.Where(v => !string.IsNullOrWhiteSpace(v)))
            {
                if (!koppen.Contains(veld)) koppen.Add(veld);
            }

            if (datumKolom < 0 || energieKolom < 0) continue;

            var inKwh = velden[energieKolom].Contains("kwh", StringComparison.OrdinalIgnoreCase);
            return LeesRegels(regels.Skip(i + 1), scheidingsteken, datumKolom, energieKolom, inKwh, vandaag, koppen);
        }

        return Uitkomst.Mislukt(
            "In dit bestand staat geen kolom met een datum en een kolom met opgewekte energie. "
            + "Exporteer bij Enphase onder Systeem, Rapporten de energieproductie per dag.",
            koppen);
    }

    private static Uitkomst LeesRegels(
        IEnumerable<string> regels,
        char scheidingsteken,
        int datumKolom,
        int energieKolom,
        bool inKwh,
        DateOnly vandaag,
        IReadOnlyList<string> koppen)
    {
        var perDag = new Dictionary<DateOnly, double>();
        var overgeslagen = 0;

        foreach (var regel in regels)
        {
            var velden = Splits(regel, scheidingsteken);
            if (velden.Length <= Math.Max(datumKolom, energieKolom))
            {
                overgeslagen++;
                continue;
            }

            if (LeesDatum(velden[datumKolom]) is not { } datum || LeesGetal(velden[energieKolom]) is not { } gelezen)
            {
                overgeslagen++;
                continue;
            }

            // Een toekomstige datum of een datum van voor deze eeuw is een leesfout, geen meting.
            if (datum > vandaag || datum < VroegsteDatum)
            {
                overgeslagen++;
                continue;
            }

            // Staat er een eenheid in de cel zelf ("13,5 kWh"), dan is dat wat het is. De kop kan
            // iets anders zeggen of niets, en de cel is specifieker.
            var isKwh = gelezen.IsKwh ?? inKwh;
            var kwh = isKwh ? gelezen.Waarde : gelezen.Waarde / 1000;

            // Negatief bestaat niet bij opwek; dat is een leesfout of een andere kolom.
            if (kwh < 0)
            {
                overgeslagen++;
                continue;
            }

            perDag[datum] = perDag.GetValueOrDefault(datum) + kwh;
        }

        if (perDag.Count == 0)
        {
            return Uitkomst.Mislukt(
                "De kolommen staan erin, maar er is geen enkele regel met een datum en een getal gevonden.",
                koppen);
        }

        var dagen = perDag
            .OrderBy(p => p.Key)
            .Select(p => new Dag(p.Key, Math.Round(p.Value, 4)))
            .ToList();

        return new Uitkomst(dagen, null, koppen);
    }

    /// <summary>
    /// Welke dagen uit de export we overnemen.
    ///
    /// Twee regels, en ze hebben allebei een reden die je achteraf niet meer ziet:
    ///
    /// Een dag die wij zelf al gemeten hebben blijft staan. Onze eigen meting loopt elke vijf
    /// minuten en kent ook de verbruikskant en de netkant; een dagtotaal uit een ander systeem is
    /// alleen opwek. Dat eroverheen zetten maakt de besparing die al was uitgerekend stiller én
    /// onjuist, en niemand zou kunnen zien waar dat vandaan kwam.
    ///
    /// Vandaag slaan we altijd over, ook als we er nog niets van hebben. Die dag is niet klaar: de
    /// export heeft hem tot het moment van downloaden en wij meten vanavond door. Twee halve dagen
    /// bij elkaar optellen geeft een piek die er nooit was.
    /// </summary>
    /// <param name="uitBestand">Wat er in de export stond.</param>
    /// <param name="eigenMetingen">Dagen waarvan wij zelf al een meting hebben.</param>
    /// <param name="vandaag">Vandaag in Nederlandse tijd.</param>
    public static (IReadOnlyList<Dag> Overnemen, int Overgeslagen) Kiezen(
        IReadOnlyList<Dag> uitBestand, IReadOnlySet<DateOnly> eigenMetingen, DateOnly vandaag)
    {
        var overnemen = new List<Dag>();
        var overgeslagen = 0;

        foreach (var dag in uitBestand)
        {
            if (dag.Datum >= vandaag || eigenMetingen.Contains(dag.Datum))
            {
                overgeslagen++;
                continue;
            }

            overnemen.Add(dag);
        }

        return (overnemen, overgeslagen);
    }

    /// <summary>Komma, puntkomma of tab: wat het vaakst voorkomt in de regel.</summary>
    private static char Scheidingsteken(string regel)
    {
        var kandidaten = new[] { ';', ',', '\t' };
        return kandidaten.OrderByDescending(c => regel.Count(x => x == c)).First();
    }

    /// <summary>Splitsen met respect voor aanhalingstekens: een datum mag een komma bevatten.</summary>
    private static string[] Splits(string regel, char scheidingsteken)
    {
        var velden = new List<string>();
        var huidig = new System.Text.StringBuilder();
        var inAanhaling = false;

        foreach (var teken in regel)
        {
            if (teken == '"')
            {
                inAanhaling = !inAanhaling;
                continue;
            }

            if (teken == scheidingsteken && !inAanhaling)
            {
                velden.Add(huidig.ToString().Trim());
                huidig.Clear();
                continue;
            }

            huidig.Append(teken);
        }

        velden.Add(huidig.ToString().Trim());
        return [.. velden];
    }

    private static int Zoek(string[] velden, string[] woorden)
    {
        for (var i = 0; i < velden.Length; i++)
        {
            var veld = velden[i].ToLowerInvariant();
            if (woorden.Any(w => veld.Contains(w, StringComparison.Ordinal)))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Een datum uit een exportbestand.
    ///
    /// Eerst de vormen die niet te verwarren zijn (jaar vooraan, of een maandnaam), dan de
    /// Nederlandse dag-maand-jaar, en pas daarna de Amerikaanse. Die volgorde doet ertoe: 03/04/2026
    /// is bij ons 3 april en in een Amerikaanse export 4 maart. Wij exporteren niet zelf, dus kiezen
    /// we voor de gebruiker: een Nederlandse klant downloadt een Nederlands bestand.
    /// </summary>
    private static DateOnly? LeesDatum(string waarde)
    {
        var tekst = waarde.Trim();
        if (tekst.Length == 0) return null;

        // Een tijdstempel: alleen het datumdeel. Ook "2026-01-01 00:00:00 +0100" valt hieronder.
        var ruimte = tekst.IndexOf(' ');
        var datumdeel = ruimte > 0 ? tekst[..ruimte] : tekst;

        string[] vormen =
        [
            "yyyy-MM-dd", "yyyy/MM/dd", "d-M-yyyy", "d/M/yyyy", "d.M.yyyy",
            "dd-MM-yyyy", "dd/MM/yyyy", "d MMM yyyy", "d MMMM yyyy", "MM/dd/yyyy", "M/d/yyyy",
        ];

        foreach (var cultuur in new[] { CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("nl-NL") })
        {
            if (DateOnly.TryParseExact(datumdeel, vormen, cultuur, DateTimeStyles.None, out var datum))
            {
                return datum;
            }
        }

        return null;
    }

    /// <summary>Een getal uit een cel, met de eenheid die er eventueel bij stond.</summary>
    /// <param name="Waarde">Het getal.</param>
    /// <param name="IsKwh">True bij kWh, false bij Wh, null als er geen eenheid in de cel stond.</param>
    private sealed record Getal(double Waarde, bool? IsKwh);

    /// <summary>
    /// Een getal uit een exportbestand: 1234, 1.234,5 of 1,234.5.
    ///
    /// Het laatste scheidingsteken bepaalt wat het decimaalteken is. Dat is de enige regel die
    /// allebei de notaties goed leest zonder dat je weet uit welk land het bestand komt.
    /// </summary>
    private static Getal? LeesGetal(string waarde)
    {
        var tekst = waarde.Trim().Replace(" ", string.Empty);
        if (tekst.Length == 0) return null;

        // Eenheden die soms in de cel staan. Langste eerst, anders snoept "Wh" van "kWh" af.
        bool? isKwh = null;
        foreach (var eenheid in new[] { "kWh", "Wh", "kW", "W" })
        {
            if (tekst.EndsWith(eenheid, StringComparison.OrdinalIgnoreCase))
            {
                isKwh = eenheid.StartsWith('k');
                tekst = tekst[..^eenheid.Length];
                break;
            }
        }

        var laatsteKomma = tekst.LastIndexOf(',');
        var laatstePunt = tekst.LastIndexOf('.');

        if (laatsteKomma >= 0 && laatstePunt >= 0)
        {
            tekst = laatsteKomma > laatstePunt
                ? tekst.Replace(".", string.Empty).Replace(',', '.')   // 1.234,5
                : tekst.Replace(",", string.Empty);                     // 1,234.5
        }
        else if (laatsteKomma >= 0)
        {
            // Eén komma: decimaalteken, tenzij er precies drie cijfers achter staan (1,234 = duizend).
            var achter = tekst.Length - laatsteKomma - 1;
            tekst = achter == 3 ? tekst.Replace(",", string.Empty) : tekst.Replace(',', '.');
        }

        return double.TryParse(tekst, NumberStyles.Float, CultureInfo.InvariantCulture, out var getal)
            ? new Getal(getal, isKwh)
            : null;
    }
}
