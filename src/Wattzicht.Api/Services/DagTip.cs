using Wattzicht.Shared;

namespace Wattzicht.Api.Services;

/// <summary>
/// De tip van vandaag, in één regel, voor op het startscherm.
///
/// De tab "Slim sturen" heeft alles: uurprijzen, vensters, bedragen per tip. Maar wie daar niet op
/// klikt ziet nooit iets, en dat is de meerderheid. Deze kaart staat op het scherm dat iedereen
/// opent, en zegt één ding: wordt het vandaag zonnig, en wat doe je daarmee.
///
/// Bewust een kaart en geen popup die je moet wegklikken. Een popup bij elke opstart is precies
/// waarom mensen een app gaan mijden, en deze tip is het niet waard om iemand mee te onderbreken.
///
/// Er komt alleen iets als er iets te zeggen valt. Een dag zonder zon en zonder prijsverschil levert
/// geen tip op; dan blijft de kaart weg in plaats van iets algemeens te melden.
/// </summary>
public static class DagTip
{
    /// <summary>Boven dit aantal kilowatturen noemen we het een zonnige dag.</summary>
    public const double ZonnigVanafKwh = 8;

    /// <summary>Daaronder, maar hierboven, is er nog genoeg zon om iets te verschuiven.</summary>
    public const double IetsZonVanafKwh = 3;

    /// <summary>Onder dit prijsverschil per kWh is verschuiven de moeite niet.</summary>
    public const decimal MinimumPrijsverschil = 0.05m;

    /// <summary>De tip van vandaag, of <c>null</c> als er niets te melden valt.</summary>
    public static DayTipDto? Bouw(AdviceInput input, DateOnly vandaag, AdviceDto advies)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(advies);

        if (input.HasPv)
        {
            var verwacht = AdviceEngine.VerwachtVandaagKwh(input, vandaag);
            if (verwacht >= IetsZonVanafKwh)
            {
                return ZonTip(input, vandaag, verwacht);
            }
        }

        // Geen zon van betekenis. Met een dynamisch contract valt er dan nog steeds iets te
        // verschuiven; bij een vast contract niet, en dan houden we onze mond.
        if (input.IsDynamic && advies.Cheapest is { } goedkoop && advies.MostExpensive is { } duur)
        {
            var verschil = duur.AverageAllInEur - goedkoop.AverageAllInEur;
            if (verschil >= MinimumPrijsverschil)
            {
                return new DayTipDto(
                    Headline: "Vandaag scheelt het uur waarop je stroom gebruikt",
                    Body: $"Tussen {Tijd(goedkoop.From)} en {Tijd(goedkoop.To)} is stroom "
                          + $"{Bedrag(verschil)} per kWh goedkoper dan rond {Tijd(duur.From)}. "
                          + "Zet de was of de auto dan aan.",
                    ExpectedSolarKwh: 0,
                    Kind: "prijs",
                    From: goedkoop.From,
                    To: goedkoop.To,
                    SavingEur: advies.TotalSavingEur);
            }
        }

        return null;
    }

    private static DayTipDto ZonTip(AdviceInput input, DateOnly vandaag, double verwacht)
    {
        var (from, to) = AdviceEngine.ZonnigsteVenster(input, vandaag);
        var inVenster = AdviceEngine.VerwachteKwh(input, from, to);

        // Wat het scheelt om die stroom zelf te gebruiken in plaats van terug te leveren.
        var winstPerKwh = Math.Max(0m, input.Rates.ImportPricePerKwh - input.Rates.ExportPricePerKwh);
        var waarde = Math.Round((decimal)inVenster * winstPerKwh, 2, MidpointRounding.AwayFromZero);

        var zonnig = verwacht >= ZonnigVanafKwh;
        var kop = zonnig ? "Vandaag wordt het zonnig" : "Vandaag is er wat zon";

        var wat = input.HasCharger
            ? "Zet de auto aan de laadpaal en draai dan de was"
            : input.HasBattery
                ? "Laat je batterij dan vollopen en draai de was"
                : "Draai dan de was of de vaatwasser";

        var staart = SavingsCalculator.IsSalderingActive(vandaag)
            ? "Zolang er gesaldeerd wordt scheelt dat nog weinig, vanaf 2027 het meest."
            : $"Dat scheelt ongeveer {Bedrag(waarde)}.";

        return new DayTipDto(
            Headline: kop,
            Body: $"Je panelen maken naar verwachting {verwacht:0.#} kWh, het meest tussen "
                  + $"{Tijd(from)} en {Tijd(to)}. {wat}. {staart}",
            ExpectedSolarKwh: Math.Round(verwacht, 1),
            Kind: "zon",
            From: from,
            To: to,
            SavingEur: SavingsCalculator.IsSalderingActive(vandaag) ? 0m : waarde);
    }

    private static string Tijd(DateTimeOffset moment) => DutchTime.ToLocal(moment).ToString("HH:mm");

    private static string Bedrag(decimal euro) =>
        euro >= 1m ? $"€ {euro:0.00}".Replace('.', ',') : $"{euro * 100:0} cent";
}
