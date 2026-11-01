using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;

namespace Wattzicht.Api.Services;

/// <summary>
/// Welke tarieven gelden er voor deze woning op deze dag.
///
/// Tot en met 31 december 2026 maakt dit weinig uit: met saldering telt elke teruggeleverde
/// kilowattuur voor de volle leveringsprijs, dus iedereen komt op hetzelfde uit. Vanaf 1 januari
/// 2027 niet meer. Dan verschilt het per leverancier wat je terugkrijgt, en rekenen de meeste
/// leveranciers er ook nog terugleverkosten bovenop. Het verschil tussen de ene en de andere
/// leverancier loopt bij een gemiddeld dak op tot honderden euro's per jaar.
///
/// Daar zit meteen het verschil tussen Gratis en Plus. Gratis rekent met één landelijk gemiddelde:
/// dan klopt de orde van grootte en zie je of het de goede kant op gaat. Plus rekent met de
/// leverancier en het contract van deze bewoner, en dat is het getal waarop je een beslissing neemt.
/// Een gemiddelde presenteren alsof het zijn eigen cijfer is zou erger zijn dan niets tonen.
/// </summary>
public sealed class TariefBron(AppDbContext db)
{
    /// <summary>
    /// De tarieven voor een woning op een dag. Gratis krijgt het standaardtarief; Plus krijgt het
    /// tarief van de eigen leverancier als die bekend is, en anders het eigen ingevulde tarief.
    /// </summary>
    public async Task<TariffRates> VoorWoningAsync(
        Guid homeId, DateOnly dag, bool heeftPlus, CancellationToken ct = default)
    {
        var eigen = await db.Tariffs.IgnoreQueryFilters()
            .Where(t => t.HouseholdId == homeId && t.ValidFrom <= dag)
            .OrderByDescending(t => t.ValidFrom)
            .FirstOrDefaultAsync(ct);

        var basis = eigen is null
            ? TariffRates.Default
            : new TariffRates(
                eigen.ImportPricePerKwh,
                eigen.ExportPricePerKwh,
                eigen.PublicChargingPricePerKwh,
                eigen.FeedInCostPerKwh);

        if (!heeftPlus)
        {
            // Gratis: de terugleverkant uit de landelijke standaard, niet uit zijn eigen contract. Dat
            // laatste is precies wat Plus toevoegt, en het is ook het enige getal dat echt per
            // leverancier verschilt.
            var standaard = await StandaardAsync(dag, ct);
            return basis with
            {
                ExportPricePerKwh = standaard.ExportPricePerKwh,
                FeedInCostPerKwh = standaard.FeedInCostPerKwh,
            };
        }

        var woningLeverancier = await db.Households.IgnoreQueryFilters()
            .Where(h => h.Id == homeId)
            .Select(h => h.SupplierName)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(woningLeverancier))
        {
            return basis;
        }

        var leverancier = await VoorLeverancierAsync(woningLeverancier, dag, ct);
        return leverancier is null
            ? basis
            : basis with
            {
                ExportPricePerKwh = leverancier.ExportPricePerKwh,
                FeedInCostPerKwh = leverancier.FeedInCostPerKwh,
            };
    }

    /// <summary>Het tarief van één leverancier op een dag, of <c>null</c> als we die niet kennen.</summary>
    public async Task<SupplierTariff?> VoorLeverancierAsync(
        string leverancier, DateOnly dag, CancellationToken ct = default) =>
        await db.SupplierTariffs.IgnoreQueryFilters()
            .Where(t => t.Supplier == leverancier && t.ValidFrom <= dag)
            .OrderByDescending(t => t.ValidFrom)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Het landelijke gemiddelde dat Gratis krijgt. Staat als gewone rij in dezelfde tabel onder de
    /// naam "standaard", zodat het bijgewerkt kan worden zonder nieuwe versie van de software.
    /// </summary>
    public async Task<TariffRates> StandaardAsync(DateOnly dag, CancellationToken ct = default)
    {
        var rij = await VoorLeverancierAsync(StandaardNaam, dag, ct);
        return rij is null
            ? TariffRates.Default
            : TariffRates.Default with
            {
                ExportPricePerKwh = rij.ExportPricePerKwh,
                FeedInCostPerKwh = rij.FeedInCostPerKwh,
            };
    }

    /// <summary>Onder welke naam het landelijke gemiddelde in de tabel staat.</summary>
    public const string StandaardNaam = "standaard";

    /// <summary>De leveranciers waarvan we een tarief kennen, voor de keuzelijst in de app.</summary>
    public async Task<IReadOnlyList<string>> LeveranciersAsync(CancellationToken ct = default) =>
        await db.SupplierTariffs.IgnoreQueryFilters()
            .Where(t => t.Supplier != StandaardNaam)
            .Select(t => t.Supplier)
            .Distinct()
            .OrderBy(naam => naam)
            .ToListAsync(ct);
}
