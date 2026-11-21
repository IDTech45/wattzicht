using Wattzicht.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Wattzicht.Api.Data;

/// <summary>
/// De woning zonder installateur.
///
/// Een woning hoort in de database altijd bij een installateur, en wie zich aanmeldde zonder code van
/// een installateur kwam daardoor bij de eerste de beste terecht: Triple Energie. Op 21-11-2026 stonden
/// alle 144 woningen op hun naam, terwijl de meesten hen nooit hebben gezien. Geeft zo'n bewoner later
/// inzage, dan verschijnt hij in het portal van een bedrijf dat nooit bij hem over de vloer is geweest.
///
/// Daarom is er nu een installateur die Wattzicht zelf is. Zijn code staat niet goedgekeurd, dus niemand
/// kan hem invullen bij het aanmelden, en er hoort geen inlogaccount bij: er is dus ook geen portal waar
/// deze woningen in opduiken. Voor de bewoner verandert er niets, behalve dat er geen vreemd logo en geen
/// offerteknop van een onbekend bedrijf meer in zijn app staan.
/// </summary>
public static class SysteemInstallateur
{
    public static readonly Guid Id = new("00000000-0000-4000-8000-000000000001");
    public const string Slug = "wattzicht";

    /// <summary>Maakt hem aan als hij er nog niet is. Draait bij elke start en schrijft daarna niets meer.</summary>
    public static async Task<Guid> EnsureAsync(AppDbContext db, TimeProvider clock, CancellationToken ct = default)
    {
        var bestaat = await db.Installers.IgnoreQueryFilters().AnyAsync(i => i.Id == Id, ct);
        if (bestaat) return Id;

        db.Installers.Add(new Installer
        {
            Id = Id,
            Name = "Wattzicht",
            Slug = Slug,
            PrimaryColor = "#0F172A",
            AccentColor = "#2DD4BF",
            // Geen goedkeuring: zo kan niemand deze code invullen bij het aanmelden.
            ApprovedAt = null,
            // Geen offertelink en geen logo: deze bewoner heeft geen installateur, dus hoort er ook
            // geen knop in zijn app te staan die hem naar een offerte van iemand anders stuurt.
            QuoteUrl = null,
            LogoUrl = null,
            BillingMode = InstallerBillingMode.Free,
            CreatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
        return Id;
    }

    /// <summary>
    /// Woningen die nooit een code van een installateur hebben gebruikt, loskoppelen van het bedrijf waar
    /// ze per ongeluk onder hingen.
    ///
    /// Wie zich met een code aanmeldt moet verplicht inzage geven, dus stond bij die woningen de inzage
    /// aan op het moment van aanmelden. Staat de inzage uit én is er sindsdien niets aan veranderd, dan is
    /// er nooit een code gebruikt. Wie zijn inzage later heeft ingetrokken blijft staan waar hij staat:
    /// zijn installateur ziet hem toch niet, en zet hij de inzage ooit weer aan, dan hoort hij bij het
    /// bedrijf dat zijn panelen heeft gelegd.
    /// </summary>
    public static async Task<int> LosseWoningenAsync(AppDbContext db, CancellationToken ct = default)
    {
        var losse = await db.Households.IgnoreQueryFilters()
            .Where(h => h.InstallerId != Id
                        && !h.InstallerAccessGranted
                        && h.InstallerAccessChangedAt != null
                        && h.InstallerAccessChangedAt <= h.CreatedAt.AddMinutes(1))
            .ToListAsync(ct);
        if (losse.Count == 0) return 0;

        foreach (var woning in losse) woning.InstallerId = Id;
        await db.SaveChangesAsync(ct);
        return losse.Count;
    }
}
