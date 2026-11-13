namespace Wattzicht.Api.Services;

/// <summary>
/// Schat het piekvermogen van een installatie uit haar eigen metingen.
///
/// Waarom dit bestaat: we vroegen de bewoner hoeveel kWp er op zijn dak ligt. Dat weet lang niet
/// iedereen, het staat op een offerte van jaren geleden, en het is precies zo'n vraag waar iemand op
/// afhaakt. Terwijl wij het kunnen uitrekenen: we weten wat de panelen per dag maakten en hoeveel zon
/// er die dag op het dak viel.
///
/// De rekensom: opwek = piekvermogen × instraling × rendement. Het rendement van een gewone
/// Nederlandse installatie (omvormer, temperatuur, kabels, een beetje stof) ligt rond de 80%, en de
/// instraling die wij van de weerdienst krijgen is horizontaal gemeten terwijl een schuin dak er meer
/// van opvangt. Die twee samen geven ongeveer 0,9 kWh per kWp per kWh/m².
///
/// We nemen een goede dag en niet het gemiddelde. Een gemiddelde zit vol dagen met sneeuw, schaduw of
/// een paneel dat net uitviel, en dan zou de schatting elke winter lager worden terwijl er niets
/// veranderd is. De negentigste percentiel is "zo goed doet deze installatie het als alles meezit",
/// en dat is wat het typeplaatje ook zegt.
///
/// Het blijft een schatting, en zo staat het ook in de app. Wie het zeker weet tikt zijn eigen getal
/// in en dan gebruiken we dat.
/// </summary>
public static class PiekSchatter
{
    /// <summary>Opwek per kWp per kWh/m² op een goede dag: rendement en de schuinte van het dak samen.</summary>
    public const double KwhPerKwpPerInstraling = 0.9;

    /// <summary>Minder dagen dan dit zegt niets; één toevallige dag maakt dan de schatting.</summary>
    public const int MinimumDagen = 8;

    /// <summary>Onder deze instraling is de dag te donker om iets uit af te leiden.</summary>
    public const double MinimumInstraling = 1.0;

    /// <summary>Grenzen waarbinnen een thuisinstallatie valt; daarbuiten geloven we onze eigen som niet.</summary>
    public const double MinimumKwp = 0.5;
    public const double MaximumKwp = 40.0;

    /// <summary>
    /// Het geschatte piekvermogen in kWp, of <c>null</c> als er te weinig bruikbare dagen zijn.
    /// </summary>
    public static double? Schat(IReadOnlyList<PerformanceDay> dagen)
    {
        var bruikbaar = dagen
            .Where(d => d.IrradianceKwhM2 is >= MinimumInstraling && d.PvKwh > 0)
            .Select(d => d.PvKwh / d.IrradianceKwhM2!.Value)
            .OrderBy(r => r)
            .ToList();

        if (bruikbaar.Count < MinimumDagen) return null;

        var goedeDag = Percentiel(bruikbaar, 0.9);
        var kwp = goedeDag / KwhPerKwpPerInstraling;

        if (kwp is < MinimumKwp or > MaximumKwp) return null;

        // Op een tiende: nauwkeuriger doen we niet alsof.
        return Math.Round(kwp, 1);
    }

    private static double Percentiel(IReadOnlyList<double> oplopend, double deel)
    {
        if (oplopend.Count == 1) return oplopend[0];

        var plek = deel * (oplopend.Count - 1);
        var onder = (int)Math.Floor(plek);
        var boven = Math.Min(onder + 1, oplopend.Count - 1);
        var rest = plek - onder;

        return oplopend[onder] + (oplopend[boven] - oplopend[onder]) * rest;
    }
}
