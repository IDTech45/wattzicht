namespace Wattzicht.Api.Services;

/// <summary>
/// Wat een installateur per maand kost, zoals het op de installateurspagina staat: de eerste vijftig
/// klanten gratis, daarna een vast bedrag per actieve klant per maand.
///
/// Dit staat apart omdat het eerder mis ging op het beheerpaneel: daar stond gewoon alle huishoudens
/// maal twee euro. Dat is op twee punten verkeerd. De eerste vijftig zijn gratis, en een huishouden
/// dat zich aanmeldde maar nooit een apparaat koppelde is geen actieve klant. Triple Energie met
/// één klant zag zo twee euro omzet staan die niemand kon factureren.
/// </summary>
public static class Billing
{
    /// <summary>Gratis klanten per installateur als er niets is ingesteld.</summary>
    public const int StandaardGratis = 50;

    /// <summary>Maandtarief per actieve klant als er niets is ingesteld.</summary>
    public const decimal StandaardPrijs = 2m;

    /// <summary>Hoeveel actieve klanten er boven de gratis drempel uitkomen.</summary>
    /// <param name="actieveKlanten">Huishoudens met minstens één gekoppeld apparaat.</param>
    /// <param name="gratis">De gratis drempel per installateur.</param>
    public static int Facturabel(int actieveKlanten, int gratis) => Math.Max(0, actieveKlanten - Math.Max(0, gratis));

    /// <summary>Het maandbedrag voor één installateur.</summary>
    public static decimal PerMaand(int actieveKlanten, int gratis, decimal prijsPerKlant) =>
        Facturabel(actieveKlanten, gratis) * Math.Max(0m, prijsPerKlant);

    /// <summary>Gratis drempel uit <c>Billing:FreeHouseholdsPerInstaller</c>.</summary>
    public static int GratisUitConfig(IConfiguration config) =>
        int.TryParse(config["Billing:FreeHouseholdsPerInstaller"], out var aantal) && aantal >= 0
            ? aantal
            : StandaardGratis;

    /// <summary>
    /// Maandtarief uit <c>Billing:PricePerHouseholdEur</c>. Staat in configuratie en niet hard in de
    /// code: dit is een prijs die gaat veranderen, en dan wil je geen nieuwe versie hoeven uitrollen om
    /// je eigen omzetoverzicht te laten kloppen.
    ///
    /// Een komma telt als decimaalteken. Een euro-bedrag met een komma typen is normaal, en met
    /// NumberStyles.Any las de invariante cultuur die komma als duizendscheiding: "2,50" werd dan 250
    /// euro per klant, en dat getal stond zonder enige melding op het beheerpaneel.
    /// </summary>
    public static decimal PrijsUitConfig(IConfiguration config)
    {
        var waarde = config["Billing:PricePerHouseholdEur"]?.Trim().Replace(',', '.');

        // Bewust geen duizendscheiding toestaan: een maandtarief per klant heeft die niet nodig, en
        // zonder die toestemming kan een verkeerd gezet scheidingsteken geen factor duizend worden.
        const System.Globalization.NumberStyles stijl =
            System.Globalization.NumberStyles.AllowDecimalPoint
            | System.Globalization.NumberStyles.AllowLeadingSign
            | System.Globalization.NumberStyles.AllowLeadingWhite
            | System.Globalization.NumberStyles.AllowTrailingWhite;

        return decimal.TryParse(waarde, stijl, System.Globalization.CultureInfo.InvariantCulture, out var prijs)
               && prijs >= 0
            ? prijs
            : StandaardPrijs;
    }
}
