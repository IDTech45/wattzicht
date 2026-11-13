using UglyToad.PdfPig;

namespace Wattzicht.Api.Services;

/// <summary>
/// Haalt de tekst uit een geüpload document, zodat <see cref="TariefLezer"/> er tarieven uit kan
/// halen.
///
/// Twee soorten: een pdf (zoals bijna elke leverancier zijn contract en jaarafrekening stuurt) en
/// platte tekst. Een foto van een contract kan niet; daar is tekstherkenning voor nodig en die
/// hebben we niet, dus zeggen we dat eerlijk in plaats van een leeg resultaat terug te geven.
///
/// Een pdf van buiten is onbekend terrein: kapot, versleuteld, of duizend pagina's. Daarom een
/// paginagrens, een tekengrens en een vangnet om elke fout heen. Mislukt het, dan is dat een nette
/// melding en geen vijfhonderd.
/// </summary>
public static class DocumentTekst
{
    /// <summary>Verder dan dit kijken we niet; tarieven staan op de eerste pagina's.</summary>
    public const int MaximumPaginas = 25;

    /// <summary>Genoeg voor een afrekening, en te weinig om het geheugen mee vol te zetten.</summary>
    public const int MaximumTekens = 400_000;

    public sealed record Uitkomst(string? Tekst, string? Fout);

    public static Uitkomst Lees(Stream stroom, string? bestandsnaam, string? contentType)
    {
        var naam = (bestandsnaam ?? string.Empty).ToLowerInvariant();
        var type = (contentType ?? string.Empty).ToLowerInvariant();

        if (naam.EndsWith(".pdf") || type.Contains("pdf"))
        {
            return LeesPdf(stroom);
        }

        if (naam.EndsWith(".txt") || naam.EndsWith(".csv") || type.StartsWith("text/"))
        {
            using var lezer = new StreamReader(stroom, System.Text.Encoding.UTF8, true);
            var tekst = lezer.ReadToEnd();
            return new Uitkomst(Kort(tekst), null);
        }

        // Een foto is het gewone geval bij iemand die zijn contract op papier heeft. Dat kunnen we
        // niet lezen, en dat is iets anders dan "er staat niets in".
        var isAfbeelding = type.StartsWith("image/")
                           || naam.EndsWith(".jpg") || naam.EndsWith(".jpeg") || naam.EndsWith(".png")
                           || naam.EndsWith(".heic");

        return new Uitkomst(null, isAfbeelding
            ? "Een foto kunnen we niet lezen. Stuur de pdf die je van je leverancier kreeg."
            : "Dit bestandstype kunnen we niet lezen. Een pdf van je leverancier werkt wel.");
    }

    private static Uitkomst LeesPdf(Stream stroom)
    {
        try
        {
            // PdfPig wil een stroom waarin gesprongen kan worden; een upload is dat niet altijd.
            using var geheugen = new MemoryStream();
            stroom.CopyTo(geheugen);
            geheugen.Position = 0;

            using var document = PdfDocument.Open(geheugen);
            var bouwer = new System.Text.StringBuilder();

            foreach (var pagina in document.GetPages().Take(MaximumPaginas))
            {
                // Niet pagina.Text: dat plakt alles aan elkaar, en dan staat het bedrag niet meer op
                // dezelfde regel als het woord dat zegt waar het over gaat. We bouwen de regels zelf
                // op uit de woorden en hun plek op de pagina: zelfde hoogte is dezelfde regel.
                foreach (var regel in Regels(pagina))
                {
                    bouwer.AppendLine(regel);
                }

                if (bouwer.Length > MaximumTekens) break;
            }

            var tekst = bouwer.ToString();
            return string.IsNullOrWhiteSpace(tekst)
                ? new Uitkomst(null, "In deze pdf staat geen tekst, alleen een afbeelding. Vraag je leverancier om een gewone pdf.")
                : new Uitkomst(Kort(tekst), null);
        }
        catch (Exception)
        {
            return new Uitkomst(null, "Deze pdf konden we niet openen. Is hij misschien met een wachtwoord beveiligd?");
        }
    }

    /// <summary>
    /// De woorden van een pagina terugbrengen tot regels.
    ///
    /// Woorden op ongeveer dezelfde hoogte horen bij elkaar; een halve regelhoogte speling vangt het
    /// verschil tussen een hoofdletter en een komma op. Binnen een regel op volgorde van links naar
    /// rechts, zodat "Leveringstarief" en "€ 0,27 per kWh" weer naast elkaar staan.
    /// </summary>
    private static IEnumerable<string> Regels(UglyToad.PdfPig.Content.Page pagina)
    {
        const double speling = 3.0;

        var woorden = pagina.GetWords()
            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .OrderByDescending(w => w.BoundingBox.Bottom)
            .ToList();

        var regel = new List<UglyToad.PdfPig.Content.Word>();
        double? hoogte = null;

        foreach (var woord in woorden)
        {
            var y = woord.BoundingBox.Bottom;
            if (hoogte is { } h && Math.Abs(h - y) > speling)
            {
                yield return Samen(regel);
                regel = [];
            }

            hoogte = y;
            regel.Add(woord);
        }

        if (regel.Count > 0)
        {
            yield return Samen(regel);
        }
    }

    private static string Samen(IEnumerable<UglyToad.PdfPig.Content.Word> woorden) =>
        string.Join(' ', woorden.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text));

    private static string Kort(string tekst) =>
        tekst.Length <= MaximumTekens ? tekst : tekst[..MaximumTekens];
}
