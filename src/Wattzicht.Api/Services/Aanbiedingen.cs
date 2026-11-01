using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Services;

/// <summary>Soorten aanbiedingen; tevens het type van een lead.</summary>
public static class OfferKind
{
    /// <summary>Offerte voor een thuisbatterij bij de eigen installateur.</summary>
    public const string Battery = "battery";

    /// <summary>Overstappen naar een dynamisch contract.</summary>
    public const string DynamicContract = "dynamic_contract";

    public static readonly string[] Alle = [Battery, DynamicContract];
}

/// <summary>Eén aanbieding op het startscherm.</summary>
/// <param name="Kind">battery of dynamic_contract.</param>
/// <param name="Title">Kop.</param>
/// <param name="Body">Waarom dit nu aan deze bewoner getoond wordt, met zijn eigen cijfers erin.</param>
/// <param name="ActionLabel">Tekst op de knop.</param>
/// <param name="InstallerName">Naar wie de aanvraag gaat; leeg als er geen installateur is.</param>
public sealed record OfferDto(string Kind, string Title, string Body, string ActionLabel, string? InstallerName);

/// <summary>
/// De aanbiedingen-tegel, alleen voor Gratis.
///
/// Geen advertenties van derden, nooit. Wat hier staat zijn twee dingen waar deze bewoner echt iets
/// aan heeft, en alleen als zijn eigen data dat rechtvaardigt. Een batterij-tegel bij iemand die al
/// een batterij heeft is geen aanbieding maar ruis, en na twee van die tegels kijkt niemand er meer
/// naar.
///
/// Plus-gebruikers zien dit scherm nooit. Dat is het verschil waar ze voor betalen.
/// </summary>
public sealed class Aanbiedingen(AppDbContext db, TimeProvider clock)
{
    /// <summary>Boven dit aandeel teruglevering is een batterij het overwegen waard.</summary>
    public const double TerugleverDrempel = 0.40;

    /// <summary>Over hoeveel dagen we dat aandeel bekijken.</summary>
    public const int Meetdagen = 30;

    /// <summary>Nooit meer dan dit op het scherm; drie kaarten is al veel.</summary>
    public const int MaximumKaarten = 3;

    /// <summary>De aanbiedingen voor deze woning. Leeg bij Plus, en leeg als de data niets rechtvaardigt.</summary>
    public async Task<IReadOnlyList<OfferDto>> VoorWoningAsync(
        Guid homeId, bool heeftPlus, CancellationToken ct = default)
    {
        if (heeftPlus)
        {
            return [];
        }

        var kaarten = new List<OfferDto>();

        var batterij = await BatterijAsync(homeId, ct);
        if (batterij is not null) kaarten.Add(batterij);

        // Het dynamische contract komt er pas bij als de tariefvergelijking in Plus af is. Eerder
        // zouden we iemand laten overstappen zonder te kunnen laten zien wat het hem oplevert.

        return kaarten.Take(MaximumKaarten).ToList();
    }

    /// <summary>
    /// Levert deze woning veel terug en heeft hij geen batterij? Dan is een batterij het overwegen
    /// waard, en vanaf 2027 des te meer: dan levert teruggeleverde stroom bijna niets meer op.
    /// </summary>
    private async Task<OfferDto?> BatterijAsync(Guid homeId, CancellationToken ct)
    {
        var heeftBatterij = await db.Devices.IgnoreQueryFilters()
            .AnyAsync(d => d.HouseholdId == homeId && d.Type == DeviceType.Battery, ct);
        if (heeftBatterij) return null;

        var vanaf = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddDays(-Meetdagen));
        var dagen = await db.DailySummaries.IgnoreQueryFilters()
            .Where(d => d.HouseholdId == homeId && d.Date >= vanaf)
            .Select(d => new { d.PvKwh, d.ExportKwh })
            .ToListAsync(ct);

        var opwek = dagen.Sum(d => d.PvKwh);
        var terug = dagen.Sum(d => d.ExportKwh);

        // Zonder opwek valt er niets te zeggen; een percentage van nul is geen reden.
        if (opwek <= 0) return null;

        var aandeel = terug / opwek;
        if (aandeel < TerugleverDrempel) return null;

        var installateur = await db.Households.IgnoreQueryFilters()
            .Where(h => h.Id == homeId)
            .Select(h => h.Installer.Name)
            .FirstOrDefaultAsync(ct);

        return new OfferDto(
            Kind: OfferKind.Battery,
            Title: "Je levert veel terug",
            Body: $"Van je zonnestroom ging de afgelopen maand {aandeel * 100:0}% terug naar het net. "
                  + "Met een thuisbatterij gebruik je dat zelf, en vanaf 2027 scheelt dat het meest.",
            ActionLabel: installateur is null ? "Vraag een offerte" : $"Vraag een offerte bij {installateur}",
            InstallerName: installateur);
    }

    /// <summary>
    /// Legt een aanvraag vast en stuurt hem naar de installateur van deze woning. Is er geen
    /// installateur, dan komt hij bij Dogantech terecht; dan is er in elk geval iemand die belt.
    /// </summary>
    public async Task<Lead> AanvraagAsync(Guid homeId, string kind, CancellationToken ct = default)
    {
        var woning = await db.Households.IgnoreQueryFilters()
            .Where(h => h.Id == homeId)
            .Select(h => new { h.Name, h.City, InstallerNaam = h.Installer.Name, h.InstallerId })
            .FirstAsync(ct);

        var email = await db.Users.IgnoreQueryFilters()
            .Where(u => u.HouseholdId == homeId)
            .OrderBy(u => u.CreatedAt)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(ct);

        var lead = new Lead
        {
            Id = Guid.NewGuid(),
            Name = woning.Name,
            Company = woning.InstallerNaam ?? "Dogantech",
            Email = email ?? "",
            Source = $"app:{kind}:{homeId}",
            CreatedAt = clock.GetUtcNow(),
        };

        db.Leads.Add(lead);
        await db.SaveChangesAsync(ct);
        return lead;
    }
}
