using System.Text.Json;
using Wattzicht.Shared;

namespace Wattzicht.Api.Services;

/// <summary>
/// Wat elk paneel afzonderlijk doet.
///
/// Waarom dit bestaat: een installatie is geen apparaat maar een rij panelen, en de vraag die mensen
/// echt hebben gaat over die rij. Doet er een minder? Zit er iets op? Ligt er een in de schaduw van
/// de schoorsteen? Daar is een totaal in kilowatt geen antwoord op.
///
/// De gegevens komen van de gateway op het thuisnetwerk, die per micro-omvormer meldt wat hij als
/// laatste maakte en wat zijn hoogste ooit was. Dat laatste is de maat: panelen verschillen in
/// ligging en leeftijd, dus een paneel vergelijken met zijn eigen beste dag is eerlijker dan met de
/// buurman op het dak.
/// </summary>
public static class Panelen
{
    /// <summary>
    /// Hieronder noemen we een paneel achterblijvend.
    ///
    /// Zeventig procent van wat de rest op dit moment doet. Geen strengere grens: een paneel aan de
    /// rand van het dak loopt op een winterochtend altijd wat achter, en een app die daar elke dag
    /// over begint leert mensen om meldingen weg te klikken.
    /// </summary>
    public const double Drempel = 0.70;

    /// <summary>Onder dit vermogen zegt vergelijken niets; dan is het gewoon schemer.</summary>
    public const double MinimumWatt = 25;

    /// <summary>De rauwe lijst van de gateway omzetten naar iets waar een scherm mee kan werken.</summary>
    public static PanelenDto Lees(string? json, DateTimeOffset? bijgewerktOp)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new PanelenDto([], 0, null, 0);
        }

        List<Rauw> rauw;
        try
        {
            rauw = JsonSerializer.Deserialize<List<Rauw>>(json) ?? [];
        }
        catch (JsonException)
        {
            return new PanelenDto([], 0, null, 0);
        }

        var schoon = rauw
            .Where(r => !string.IsNullOrWhiteSpace(r.Serial))
            .OrderByDescending(r => r.Watt)
            .ToList();

        if (schoon.Count == 0)
        {
            return new PanelenDto([], 0, bijgewerktOp, 0);
        }

        // De middelste waarde en niet het gemiddelde: met één paneel op nul zakt een gemiddelde mee,
        // en dan valt precies het geval weg dat we willen zien.
        var mediaan = Mediaan(schoon.Select(r => r.Watt).ToList());

        var panelen = new List<PaneelDto>(schoon.Count);
        for (var i = 0; i < schoon.Count; i++)
        {
            var r = schoon[i];
            var achter = mediaan >= MinimumWatt && r.Watt < mediaan * Drempel;

            panelen.Add(new PaneelDto(
                Serial: r.Serial!,
                Nummer: i + 1,
                Watt: Math.Round(r.Watt),
                MaxWatt: Math.Round(r.MaxWatt),
                GemeldOp: r.GemeldOpUtc,
                BlijftAchter: achter));
        }

        return new PanelenDto(
            panelen,
            Math.Round(schoon.Sum(r => r.Watt)),
            bijgewerktOp,
            panelen.Count(p => p.BlijftAchter));
    }

    /// <summary>
    /// De lijst van de gateway opslaan.
    ///
    /// We bewaren alleen wat we nodig hebben en in onze eigen vorm, zodat een veld dat Enphase
    /// morgen anders noemt geen scherm omver haalt.
    /// </summary>
    public static string? UitGateway(JsonElement lijst)
    {
        if (lijst.ValueKind != JsonValueKind.Array) return null;

        var uit = new List<Rauw>();
        foreach (var item in lijst.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            var serial = Tekst(item, "serialNumber") ?? Tekst(item, "serial_number");
            if (string.IsNullOrWhiteSpace(serial)) continue;

            uit.Add(new Rauw
            {
                Serial = serial,
                Watt = Getal(item, "lastReportWatts") ?? 0,
                MaxWatt = Getal(item, "maxReportWatts") ?? 0,
                GemeldOpUtc = Getal(item, "lastReportDate") is { } unix and > 0
                    ? DateTimeOffset.FromUnixTimeSeconds((long)unix)
                    : null,
            });
        }

        return uit.Count == 0 ? null : JsonSerializer.Serialize(uit);
    }

    private static double Mediaan(List<double> waarden)
    {
        var gesorteerd = waarden.OrderBy(w => w).ToList();
        var midden = gesorteerd.Count / 2;
        return gesorteerd.Count % 2 == 1
            ? gesorteerd[midden]
            : (gesorteerd[midden - 1] + gesorteerd[midden]) / 2;
    }

    private static string? Tekst(JsonElement element, string naam) =>
        element.TryGetProperty(naam, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Getal(JsonElement element, string naam) =>
        element.TryGetProperty(naam, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)
            ? d
            : null;

    /// <summary>Wat wij per paneel bewaren.</summary>
    public sealed class Rauw
    {
        public string? Serial { get; set; }
        public double Watt { get; set; }
        public double MaxWatt { get; set; }
        public DateTimeOffset? GemeldOpUtc { get; set; }
    }
}
