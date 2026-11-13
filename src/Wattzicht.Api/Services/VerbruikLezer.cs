using System.Globalization;
using System.Text.RegularExpressions;

namespace Wattzicht.Api.Services;

/// <summary>
/// Haalt de kilowatturen per maand uit een overzicht van de leverancier.
///
/// Waarom dit bestaat: bijna iedereen krijgt maandelijks een mail met wat hij heeft afgenomen en
/// teruggeleverd. Wij weten dat alleen van mensen met een P1-meter of een hybride omvormer; voor de
/// rest is de netkant een gat. Die mail heeft het al, en één pdf uploaden is minder werk dan een
/// meter kopen.
///
/// Wat we bewust niet doen: een maandtotaal over de dagen uitsmeren. Dan zou een grafiek per dag
/// precisie tonen die er niet is. Een maand blijft een maand, naast onze eigen metingen, en zo is
/// ook te zien waar ze uit elkaar lopen.
/// </summary>
public static class VerbruikLezer
{
    /// <summary>Eén maand uit het overzicht.</summary>
    /// <param name="Maand">De eerste dag van die maand.</param>
    /// <param name="AfnameKwh">Van het net gehaald.</param>
    /// <param name="TerugleveringKwh">Aan het net geleverd; null als het er niet bij stond.</param>
    public sealed record Maand(DateOnly Maand_, double AfnameKwh, double? TerugleveringKwh);

    public sealed record Uitkomst(IReadOnlyList<Maand> Maanden, string? Fout)
    {
        public bool Gelukt => Fout is null && Maanden.Count > 0;
    }

    private static readonly string[] Maandnamen =
    [
        "januari", "februari", "maart", "april", "mei", "juni",
        "juli", "augustus", "september", "oktober", "november", "december",
    ];

    private static readonly string[] AfnameWoorden =
    [
        "afname", "verbruik", "afgenomen", "geleverd aan u", "van het net", "stroomverbruik", "elektriciteitsverbruik",
    ];

    private static readonly string[] TerugWoorden =
    [
        "teruggeleverd", "teruglevering", "terug geleverd", "ingevoed", "aan het net",
    ];

    /// <summary>Hoger dan dit is geen maandverbruik van een huishouden maar een jaartotaal of een bedrag.</summary>
    private const double MaximumPerMaand = 5_000;

    public static Uitkomst Lees(string tekst, DateOnly vandaag)
    {
        if (string.IsNullOrWhiteSpace(tekst))
        {
            return new Uitkomst([], "Het document is leeg.");
        }

        var regels = tekst
            .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(r => r.Length is > 0 and < 400)
            .ToList();

        var perMaand = new Dictionary<DateOnly, (double Afname, double? Terug)>();

        // Een tabel zet de woorden in de kopregel en daaronder alleen cijfers. Zien we zo'n kop
        // (allebei de woorden, geen getallen), dan weten we voor de regels daarna wat de eerste en
        // de tweede kolom betekenen. Zonder kop raden we niet.
        var kolomVolgordeBekend = false;

        foreach (var regel in regels)
        {
            if (IsKopregel(regel))
            {
                kolomVolgordeBekend = true;
                continue;
            }

            if (Maandvan(regel, vandaag) is not { } maand) continue;

            var kwh = Kilowatturen(regel);
            if (kwh.Count == 0) continue;

            var heeftAfname = AfnameWoorden.Any(w => regel.Contains(w, StringComparison.OrdinalIgnoreCase));
            var heeftTerug = TerugWoorden.Any(w => regel.Contains(w, StringComparison.OrdinalIgnoreCase));

            // Een regel met beide woorden heeft meestal twee getallen: eerst afname, dan teruglevering.
            double? afname = null;
            double? terug = null;

            if (heeftAfname && heeftTerug && kwh.Count >= 2)
            {
                afname = kwh[0];
                terug = kwh[1];
            }
            else if (heeftTerug && !heeftAfname)
            {
                terug = kwh[0];
            }
            else if (heeftAfname)
            {
                afname = kwh[0];
            }
            else if (kolomVolgordeBekend)
            {
                // Rij onder een kopregel: eerste kolom afname, tweede teruglevering.
                afname = kwh[0];
                if (kwh.Count >= 2) terug = kwh[1];
            }
            else
            {
                continue;
            }

            var bestaand = perMaand.GetValueOrDefault(maand);
            perMaand[maand] = (
                afname ?? bestaand.Afname,
                terug ?? bestaand.Terug);
        }

        var maanden = perMaand
            .Where(p => p.Value.Afname > 0 || p.Value.Terug > 0)
            .OrderBy(p => p.Key)
            .Select(p => new Maand(p.Key, Math.Round(p.Value.Afname, 1), p.Value.Terug is { } t ? Math.Round(t, 1) : null))
            .ToList();

        return maanden.Count == 0
            ? new Uitkomst([], "In dit document staan geen maanden met kilowatturen die wij herkennen.")
            : new Uitkomst(maanden, null);
    }

    /// <summary>
    /// Een kopregel van een tabel: allebei de woorden, geen getallen. Daarna weten we wat de kolommen
    /// betekenen zonder dat het woord op elke rij hoeft te staan.
    /// </summary>
    private static bool IsKopregel(string regel) =>
        AfnameWoorden.Any(w => regel.Contains(w, StringComparison.OrdinalIgnoreCase))
        && TerugWoorden.Any(w => regel.Contains(w, StringComparison.OrdinalIgnoreCase))
        && !Regex.IsMatch(regel, @"\d");

    /// <summary>
    /// De maand waar een regel over gaat: "oktober 2026", "okt 2026", "10-2026" of "01-10-2026".
    ///
    /// Zonder jaartal nemen we het lopende jaar, en ligt die maand in de toekomst dan het jaar
    /// ervoor: een overzicht van december dat je in januari krijgt gaat over vorig jaar.
    /// </summary>
    internal static DateOnly? Maandvan(string regel, DateOnly vandaag)
    {
        var laag = regel.ToLowerInvariant();

        for (var i = 0; i < Maandnamen.Length; i++)
        {
            var naam = Maandnamen[i];
            var kort = naam[..3];
            var staatErin = laag.Contains(naam, StringComparison.Ordinal)
                            || Regex.IsMatch(laag, $@"\b{kort}\.?\b");

            if (!staatErin) continue;

            var jaar = Jaar(laag) ?? (i + 1 > vandaag.Month ? vandaag.Year - 1 : vandaag.Year);
            return new DateOnly(jaar, i + 1, 1);
        }

        // Numeriek: 10-2026 of 10/2026.
        var m = Regex.Match(regel, @"\b(0?[1-9]|1[0-2])[-/](20\d{2})\b");
        if (m.Success)
        {
            return new DateOnly(int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value), 1);
        }

        return null;
    }

    private static int? Jaar(string regel)
    {
        var m = Regex.Match(regel, @"\b(20\d{2})\b");
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    /// <summary>De getallen die in kilowatturen staan, op volgorde van voorkomen.</summary>
    private static List<double> Kilowatturen(string regel)
    {
        var gevonden = new List<double>();

        foreach (Match m in Regex.Matches(regel, @"(-?[\d.,]+)\s*k\s?wh", RegexOptions.IgnoreCase))
        {
            if (Getal(m.Groups[1].Value) is { } getal && getal > 0 && getal <= MaximumPerMaand)
            {
                gevonden.Add(getal);
            }
        }

        // Staat de eenheid in een kolomkop in plaats van achter elk getal, dan nemen we de getallen
        // die als maandverbruik kunnen doorgaan. Een jaartal valt daar niet onder: dat zou 2026 kWh
        // zijn en dat ligt boven onze grens voor één maand.
        if (gevonden.Count == 0 && regel.Contains("kwh", StringComparison.OrdinalIgnoreCase))
        {
            foreach (Match m in Regex.Matches(regel, @"\b\d{1,3}(?:[.,]\d{1,3})?\b"))
            {
                if (Getal(m.Value) is { } getal && getal >= 1 && getal <= MaximumPerMaand)
                {
                    gevonden.Add(getal);
                }
            }
        }

        return gevonden;
    }

    private static double? Getal(string ruw)
    {
        var tekst = ruw.Trim();
        var laatsteKomma = tekst.LastIndexOf(',');
        var laatstePunt = tekst.LastIndexOf('.');

        if (laatsteKomma >= 0 && laatstePunt >= 0)
        {
            tekst = laatsteKomma > laatstePunt
                ? tekst.Replace(".", string.Empty).Replace(',', '.')
                : tekst.Replace(",", string.Empty);
        }
        else if (laatsteKomma >= 0)
        {
            var achter = tekst.Length - laatsteKomma - 1;
            tekst = achter == 3 ? tekst.Replace(",", string.Empty) : tekst.Replace(',', '.');
        }
        else if (laatstePunt >= 0 && tekst.Length - laatstePunt - 1 == 3)
        {
            // 1.234 is duizend tweehonderdvierendertig, geen 1,234.
            tekst = tekst.Replace(".", string.Empty);
        }

        return double.TryParse(tekst, NumberStyles.Float, CultureInfo.InvariantCulture, out var getal)
            ? Math.Abs(getal)
            : null;
    }
}
