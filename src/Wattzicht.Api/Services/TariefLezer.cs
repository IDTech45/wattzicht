using System.Globalization;
using System.Text.RegularExpressions;

namespace Wattzicht.Api.Services;

/// <summary>
/// Haalt de tarieven uit een energiecontract of jaarafrekening.
///
/// Waarom dit bestaat: om te laten zien wat iemand bespaart moeten we weten wat hij per kilowattuur
/// betaalt en krijgt. Dat stond in vier invulvelden, over te tikken uit een pdf van zijn
/// leverancier, en precies daar haakt iemand af. Zijn contract heeft die getallen al; het enige wat
/// ontbrak was iemand die ze eruit haalt.
///
/// Dit leest, het slaat niets op. De uitkomst is een voorstel met per bedrag de regel waar het
/// vandaan komt, zodat de bewoner ziet wat wij gelezen hebben voordat hij het overneemt. Dat is
/// geen beleefdheid: elke leverancier schrijft zijn afrekening anders, en een bedrag dat er
/// ongemerkt naast zit maakt elke besparing op elk scherm verkeerd.
/// </summary>
public static class TariefLezer
{
    /// <summary>Eén gevonden bedrag, met de regel eromheen als bewijs.</summary>
    /// <param name="EuroPerKwh">Het bedrag per kilowattuur.</param>
    /// <param name="Regel">De regel uit het document waar het uit komt.</param>
    public sealed record Vondst(decimal EuroPerKwh, string Regel);

    /// <summary>Wat er uit een document kwam.</summary>
    public sealed record Voorstel(
        Vondst? Levering,
        Vondst? Teruglevering,
        Vondst? Terugleverkosten,
        Vondst? Energiebelasting,
        bool PrijsIsAllIn)
    {
        public bool HeeftIets => Levering is not null || Teruglevering is not null || Terugleverkosten is not null;
    }

    /// <summary>Een prijs per kWh ligt hiertussen; daarbuiten is het een ander getal uit de pdf.</summary>
    private const decimal MinimumPrijs = 0.001m;
    private const decimal MaximumPrijs = 2.0m;

    /// <summary>
    /// Woorden die een regel aanwijzen. Per soort meerdere, want elke leverancier noemt het anders:
    /// Eneco schrijft "Levering stroom", Vattenfall "Leveringstarief", Budget "Stroomprijs".
    /// </summary>
    private static readonly string[] LeveringWoorden =
    [
        "leveringstarief", "levering stroom", "levering elektriciteit", "stroomprijs", "kale leveringsprijs",
        "variabel leveringstarief", "enkeltarief", "normaaltarief", "leveringskosten elektriciteit", "prijs per kwh",
    ];

    private static readonly string[] TerugleveringWoorden =
    [
        "terugleververgoeding", "terugleveringsvergoeding", "vergoeding voor teruglevering",
        "vergoeding teruglevering", "terugleverprijs", "teruglevertarief", "vergoeding ingevoede",
    ];

    private static readonly string[] TerugleverkostenWoorden =
    [
        "terugleverkosten", "kosten voor teruglevering", "terugleverheffing", "terugleverbijdrage",
        "kosten teruglevering", "salderingskosten",
    ];

    private static readonly string[] BelastingWoorden =
    [
        "energiebelasting", "energie belasting", "opslag duurzame energie", "ode",
    ];

    /// <summary>Een regel die zegt dat het bedrag al compleet is.</summary>
    private static readonly string[] AllInWoorden =
    [
        "inclusief energiebelasting", "incl. energiebelasting", "all-in", "all in tarief",
        "inclusief belastingen", "incl. btw en energiebelasting", "alles inbegrepen",
    ];

    /// <summary>Daltarief en piektarief naast elkaar: dan nemen we het gemiddelde, en zeggen dat.</summary>
    private static readonly string[] DalWoorden = ["daltarief", "dal tarief", "laag tarief"];
    private static readonly string[] PiekWoorden = ["piektarief", "normaaltarief", "hoog tarief"];

    public static Voorstel Lees(string tekst)
    {
        if (string.IsNullOrWhiteSpace(tekst))
        {
            return new Voorstel(null, null, null, null, false);
        }

        var regels = tekst
            .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(r => r.Length is > 0 and < 400)
            .ToList();

        // Teruglevering eerst: "terugleververgoeding" bevat niet het woord levering, maar
        // "vergoeding voor teruglevering van stroom" wel iets dat op een leveringsregel lijkt.
        // Door die regels eerst te claimen kan een teruglevertarief niet als leveringsprijs eindigen.
        var teruglevering = Zoek(regels, TerugleveringWoorden, out var gebruikt);
        var terugleverkosten = Zoek(regels, TerugleverkostenWoorden, out var gebruikt2, gebruikt);
        var alGebruikt = gebruikt.Concat(gebruikt2).ToHashSet();

        var levering = ZoekLevering(regels, alGebruikt);
        var belasting = Zoek(regels, BelastingWoorden, out _, alGebruikt);

        var allIn = levering is not null
                    && AllInWoorden.Any(w => levering.Regel.Contains(w, StringComparison.OrdinalIgnoreCase));

        return new Voorstel(levering, teruglevering, terugleverkosten, belasting, allIn);
    }

    /// <summary>
    /// De leveringsprijs. Staan er een dal- en een piektarief, dan is het gemiddelde eerlijker dan
    /// een van de twee: wij weten niet wanneer dit huis zijn stroom gebruikt.
    /// </summary>
    private static Vondst? ZoekLevering(List<string> regels, HashSet<string> overslaan)
    {
        var dal = Zoek(regels, DalWoorden, out _, overslaan);
        var piek = Zoek(regels, PiekWoorden, out _, overslaan);

        if (dal is not null && piek is not null)
        {
            var gemiddeld = Math.Round((dal.EuroPerKwh + piek.EuroPerKwh) / 2, 4);
            return new Vondst(gemiddeld, $"{piek.Regel} / {dal.Regel}");
        }

        return Zoek(regels, LeveringWoorden, out _, overslaan) ?? piek ?? dal;
    }

    private static Vondst? Zoek(
        List<string> regels, string[] woorden, out HashSet<string> gebruikt, HashSet<string>? overslaan = null)
    {
        gebruikt = [];

        foreach (var regel in regels)
        {
            if (overslaan is not null && overslaan.Contains(regel)) continue;
            if (!woorden.Any(w => regel.Contains(w, StringComparison.OrdinalIgnoreCase))) continue;

            if (Bedrag(regel) is not { } bedrag) continue;

            gebruikt.Add(regel);
            return new Vondst(bedrag, Kort(regel));
        }

        return null;
    }

    /// <summary>
    /// Het bedrag per kilowattuur uit een regel.
    ///
    /// Zowel "€ 0,27 per kWh" als "0,27 €/kWh" als "27,4 cent per kWh" als "0.2735". Centen worden
    /// herkend aan het woord cent of aan een getal dat als euro's nergens op slaat: niemand betaalt
    /// 27 euro per kilowattuur.
    /// </summary>
    internal static decimal? Bedrag(string regel)
    {
        var inCenten = regel.Contains("cent", StringComparison.OrdinalIgnoreCase);

        foreach (Match m in Regex.Matches(regel, @"-?\d{1,3}(?:[.,]\d{1,5})?"))
        {
            var ruw = m.Value.Replace(',', '.');
            if (!decimal.TryParse(ruw, NumberStyles.Float, CultureInfo.InvariantCulture, out var getal)) continue;

            getal = Math.Abs(getal);
            if (getal == 0) continue;

            // Een jaartal of een hoeveelheid kilowattuur is geen prijs.
            if (getal >= 1000) continue;

            var euro = inCenten || getal > MaximumPrijs ? getal / 100 : getal;
            if (euro is >= MinimumPrijs and <= MaximumPrijs)
            {
                return Math.Round(euro, 4);
            }
        }

        return null;
    }

    private static string Kort(string regel) =>
        regel.Length <= 120 ? regel : regel[..120] + "…";
}
