namespace Wattzicht.Api.Services;

/// <summary>Tarieven die de besparingsberekening nodig heeft (euro per kWh).</summary>
/// <param name="ImportPricePerKwh">Leveringstarief, all-in.</param>
/// <param name="ExportPricePerKwh">Terugleververgoeding (na saldering).</param>
/// <param name="PublicChargingPricePerKwh">Referentietarief publiek laden; standaard 0,59.</param>
/// <param name="FeedInCostPerKwh">Terugleverkosten van de leverancier per teruggeleverde kWh; standaard 0.</param>
public sealed record TariffRates(
    decimal ImportPricePerKwh,
    decimal ExportPricePerKwh,
    decimal PublicChargingPricePerKwh = SavingsCalculator.DefaultPublicChargingPrice,
    decimal FeedInCostPerKwh = 0m)
{
    /// <summary>Redelijke standaard als een huishouden nog geen tarief heeft ingevuld.</summary>
    public static TariffRates Default { get; } = new(0.28m, 0.08m);
}

/// <summary>Energiehoeveelheden over één dag (kWh) waarover de besparing wordt berekend.</summary>
/// <param name="Date">De dag; bepaalt of saldering nog geldt.</param>
/// <param name="SelfUseKwh">Zelf verbruikte zonnestroom (exclusief wat in de batterij ging).</param>
/// <param name="ExportKwh">Teruggeleverd aan het net.</param>
/// <param name="ChargedKwh">Geladen in de auto.</param>
/// <param name="BatteryDischargedKwh">Uit de batterij ontladen.</param>
/// <param name="BatteryChargedFromGridKwh">In de batterij geladen vanaf het net.</param>
public sealed record SavingsInput(
    DateOnly Date,
    double SelfUseKwh,
    double ExportKwh,
    double ChargedKwh,
    double BatteryDischargedKwh,
    double BatteryChargedFromGridKwh);

/// <summary>Uitsplitsing van de besparing per onderdeel, in euro.</summary>
public sealed record SavingsBreakdown(
    decimal SelfUseEur,
    decimal ExportEur,
    decimal ChargerEur,
    decimal BatteryEur)
{
    public decimal TotalEur => SelfUseEur + ExportEur + ChargerEur + BatteryEur;
}

/// <summary>
/// Besparingsregels v0.1 (zie ARCHITECTUUR.md):
/// <list type="bullet">
/// <item>zelfverbruik × leveringstarief;</item>
/// <item>teruglevering × leveringstarief zolang de salderingsregeling geldt (t/m 31-12-2026), daarna × terugleververgoeding;
/// in beide gevallen min de terugleverkosten van de leverancier (kan per saldo negatief worden);</item>
/// <item>laadpaal: geladen kWh × (publiek laadtarief − eigen tarief), nooit negatief;</item>
/// <item>batterij: ontladen kWh × leveringstarief − van net geladen kWh × leveringstarief.</item>
/// </list>
/// </summary>
public sealed class SavingsCalculator
{
    /// <summary>Publiek laadtarief in euro per kWh dat als referentie geldt.</summary>
    public const decimal DefaultPublicChargingPrice = 0.59m;

    /// <summary>Laatste dag waarop teruglevering nog volledig gesaldeerd wordt.</summary>
    public static readonly DateOnly SalderingEndDate = new(2026, 12, 31);

    /// <summary>Of op deze dag de salderingsregeling nog geldt.</summary>
    public static bool IsSalderingActive(DateOnly date) => date <= SalderingEndDate;

    public SavingsBreakdown Calculate(SavingsInput input, TariffRates rates)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(rates);

        var import = rates.ImportPricePerKwh;

        var selfUse = Round((decimal)Math.Max(0, input.SelfUseKwh) * import);

        var exportPrice = IsSalderingActive(input.Date) ? import : rates.ExportPricePerKwh;
        var export = Round((decimal)Math.Max(0, input.ExportKwh) * (exportPrice - rates.FeedInCostPerKwh));

        var chargerMargin = Math.Max(0m, rates.PublicChargingPricePerKwh - import);
        var charger = Round((decimal)Math.Max(0, input.ChargedKwh) * chargerMargin);

        var battery = Round(
            (decimal)Math.Max(0, input.BatteryDischargedKwh) * import
            - (decimal)Math.Max(0, input.BatteryChargedFromGridKwh) * import);

        return new SavingsBreakdown(selfUse, export, charger, battery);
    }

    /// <summary>Gemak: alleen het totaalbedrag.</summary>
    public decimal CalculateTotal(SavingsInput input, TariffRates rates) => Calculate(input, rates).TotalEur;

    private static decimal Round(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
}
