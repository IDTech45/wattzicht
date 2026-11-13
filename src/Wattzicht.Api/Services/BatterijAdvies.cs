using Wattzicht.Shared;

namespace Wattzicht.Api.Services;

/// <summary>
/// Wat een thuisbatterij bij déze woning zou hebben opgeleverd.
///
/// Waarom uit eigen cijfers en niet uit een rekenvoorbeeld: elke verkoper rekent met een gemiddeld
/// huishouden, en dat bestaat niet. Wij hebben per dag staan hoeveel er is teruggeleverd en hoeveel
/// er later van het net is gehaald. Meer heb je niet nodig: een batterij verplaatst het eerste naar
/// het tweede, tot hij vol is.
///
/// De som per dag: wat je die dag had kunnen opslaan is het kleinste van drie dingen. Wat je
/// teruglerverde (meer kun je niet opslaan), wat je later van het net haalde (meer heb je niet
/// nodig), en wat er in de batterij past. Daar gaat het rendement van laden en ontladen nog af.
///
/// Wat dit bewust niet doet: voorspellen. Het is wat er in de gemeten dagen gebeurd zou zijn,
/// omgerekend naar een jaar. Een jaar met één zomer erin ziet er anders uit dan dertig dagen in mei,
/// en dat staat er ook bij.
/// </summary>
public static class BatterijAdvies
{
    /// <summary>Heen en terug door een thuisbatterij kost ongeveer tien procent.</summary>
    public const double Rendement = 0.9;

    /// <summary>De maten die mensen in huis hangen.</summary>
    public static readonly double[] Maten = [5, 10, 15];

    /// <summary>Minder dagen dan dit zegt te weinig om een jaarbedrag op te plakken.</summary>
    public const int MinimumDagen = 14;

    /// <summary>Eén dag uit de eigen metingen.</summary>
    /// <param name="ExportKwh">Teruggeleverd aan het net.</param>
    /// <param name="ImportKwh">Van het net gehaald.</param>
    public sealed record Dag(double ExportKwh, double ImportKwh);

    /// <summary>Wat één batterijmaat zou doen.</summary>
    /// <param name="Kwh">De maat van de batterij.</param>
    /// <param name="PerJaarEur">Wat het per jaar zou schelen, in het tarief dat nu geldt.</param>
    /// <param name="PerJaarNaSalderingEur">Hetzelfde, maar met de tarieven zoals ze vanaf 2027 gelden.</param>
    /// <param name="VerplaatstPerDagKwh">Hoeveel er gemiddeld per dag door de batterij zou gaan.</param>
    public sealed record Maat(double Kwh, decimal PerJaarEur, decimal PerJaarNaSalderingEur, double VerplaatstPerDagKwh);

    /// <summary>
    /// De uitkomst voor alle maten, of een lege lijst als er te weinig gemeten dagen zijn.
    /// </summary>
    /// <param name="dagen">De gemeten dagen; hoe meer hoe beter.</param>
    /// <param name="rates">Het tarief van deze woning.</param>
    /// <param name="vandaag">Vandaag, om te bepalen of er nog gesaldeerd wordt.</param>
    public static List<Maat> Bereken(IReadOnlyList<Dag> dagen, TariffRates rates, DateOnly vandaag)
    {
        var bruikbaar = dagen.Where(d => d.ExportKwh > 0 || d.ImportKwh > 0).ToList();
        if (bruikbaar.Count < MinimumDagen) return [];

        var nu = FeedInCalculator.SelfUseWorthPerKwh(rates, vandaag);
        var na = FeedInCalculator.SelfUseWorthPerKwh(rates, new DateOnly(2027, 1, 1));

        var uitkomst = new List<Maat>();

        foreach (var maat in Maten)
        {
            var verplaatst = bruikbaar.Sum(d => Verplaatst(d, maat));
            var perDag = verplaatst / bruikbaar.Count;
            var perJaar = perDag * 365;

            uitkomst.Add(new Maat(
                maat,
                Math.Round((decimal)perJaar * nu, 0),
                Math.Round((decimal)perJaar * na, 0),
                Math.Round(perDag, 1)));
        }

        return uitkomst;
    }

    /// <summary>
    /// Wat er op één dag door de batterij zou zijn gegaan.
    ///
    /// Het kleinste van drie: wat eruit ging, wat er later weer in moest, en wat erin past. Daar gaat
    /// het rendement nog af, want wat je opslaat krijg je er niet helemaal uit.
    /// </summary>
    internal static double Verplaatst(Dag dag, double maatKwh) =>
        Math.Min(Math.Min(Math.Max(0, dag.ExportKwh), Math.Max(0, dag.ImportKwh)), maatKwh) * Rendement;
}
