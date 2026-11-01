using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;

namespace Wattzicht.Api.Services;

/// <summary>
/// Waar een Plus-abonnement vandaan komt. Per woning betaalt precies één partij, nooit twee.
/// </summary>
public static class SubscriptionSource
{
    /// <summary>Dertig dagen Plus bij de eerste koppeling, één keer per woning, zonder betaalgegevens.</summary>
    public const string Trial = "trial";

    /// <summary>De installateur betaalt (of geeft het gratis weg, zoals Triple Energie).</summary>
    public const string Installer = "installer";

    /// <summary>De bewoner betaalt via Apple In-App Purchase.</summary>
    public const string Apple = "apple";

    /// <summary>De bewoner betaalt via Google Play Billing.</summary>
    public const string Google = "google";

    /// <summary>Met de hand gezet door Wattzicht, bijvoorbeeld bij een coulanceregeling.</summary>
    public const string Manual = "manual";

    public static readonly string[] Alle = [Trial, Installer, Apple, Google, Manual];

    public static bool Bestaat(string? bron) => bron is not null && Alle.Contains(bron);
}

/// <summary>Stand van een abonnementsrij.</summary>
public static class SubscriptionStatus
{
    public const string Active = "active";

    /// <summary>
    /// Afgesproken, maar begint pas later. Nodig bij een bronwissel waarbij de klant al betaald heeft:
    /// die periode loopt uit en de nieuwe staat klaar. Zonder deze stand moet je kiezen tussen twee
    /// actieve rijen (dan betalen er twee partijen) of de klant meteen afsluiten (dan betaalt hij voor
    /// dagen die hij niet krijgt). De unieke index in de database geldt alleen voor "active", dus een
    /// rij die klaarstaat loopt daar niet tegenaan.
    /// </summary>
    public const string Pending = "pending";

    public const string Cancelled = "cancelled";
    public const string Expired = "expired";

    public static readonly string[] Alle = [Active, Pending, Cancelled, Expired];
}

/// <summary>
/// Heeft deze woning Plus, waardoor, en tot wanneer.
/// </summary>
/// <param name="Plus">Of Plus op dit moment geldt.</param>
/// <param name="Source">Waar het vandaan komt; <c>null</c> als de woning gratis is.</param>
/// <param name="Until">Tot wanneer het geldt; <c>null</c> bij een doorlopend abonnement.</param>
/// <param name="Reason">
/// Waarom het antwoord is wat het is, in gewone taal. Bedoeld voor het scherm en voor onszelf bij
/// een vraag van een klant; nooit om in de app een beslissing op te baseren.
/// </param>
public sealed record EntitlementDto(bool Plus, string? Source, DateTimeOffset? Until, string Reason);

/// <summary>
/// De enige plek waar bepaald wordt of een woning Plus heeft.
///
/// De app en de website vragen dit aan de server en rekenen het nooit zelf uit. Een vlag in de app of
/// een bonnetje van de store is niet te vertrouwen: die staan op het toestel van de klant. Daarom
/// gaat elke Plus-controle via <c>GET /api/homes/{id}/entitlement</c>.
///
/// Zolang <c>Plus:Enabled</c> uit staat is iedereen gratis, maar worden de rijen wel bijgehouden. Zo
/// klopt de administratie al op het moment dat Plus op 1 januari 2027 aangaat, en is er geen
/// inhaalslag nodig voor proefperiodes die intussen zijn gaan lopen.
/// </summary>
public sealed class EntitlementService(AppDbContext db, TimeProvider clock, IConfiguration config)
{
    /// <summary>Staat Plus aan? Tot 1 januari 2027 niet; de data wordt intussen wel bijgehouden.</summary>
    public bool PlusEnabled => config.GetValue("Plus:Enabled", false);

    /// <summary>Hoe lang de proefperiode duurt bij de eerste koppeling.</summary>
    public static readonly TimeSpan TrialDuur = TimeSpan.FromDays(30);

    /// <summary>Hoeveel maanden Plus een installateur met <c>billing_mode = free</c> weggeeft.</summary>
    public const int GratisMaandenViaInstallateur = 12;

    /// <summary>
    /// Het antwoord voor één woning. Zonder rij, of met alleen verlopen rijen, is de woning gratis.
    /// Bij meerdere rijen telt die met de laatste einddatum; een doorlopend abonnement (geen einddatum)
    /// wint altijd, want dat loopt per definitie het langst door.
    /// </summary>
    public async Task<EntitlementDto> GetForHomeAsync(Guid homeId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();

        var rij = await ActieveRijenQuery(homeId, now)
            // Doorlopend eerst (EndsAt == null), daarna op de verste einddatum.
            .OrderByDescending(s => s.EndsAt == null)
            .ThenByDescending(s => s.EndsAt)
            .FirstOrDefaultAsync(ct);

        if (rij is null)
        {
            return new EntitlementDto(false, null, null, "Geen lopend abonnement voor deze woning.");
        }

        if (!PlusEnabled)
        {
            // Bewust wél de bron en einddatum teruggeven: het scherm kan dan al laten zien wat er
            // klaarstaat, en wij zien bij een vraag van een klant dat de administratie klopt.
            return new EntitlementDto(false, rij.Source, rij.EndsAt,
                "Plus gaat op 1 januari 2027 open; tot die tijd is alles gratis.");
        }

        return new EntitlementDto(true, rij.Source, rij.EndsAt, Uitleg(rij, now));
    }

    /// <summary>De rij die nu geldt, of <c>null</c>. Gebruikt door de verwerking van store-meldingen.</summary>
    public async Task<Subscription?> ActieveRijAsync(Guid homeId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        return await ActieveRijenQuery(homeId, now)
            .OrderByDescending(s => s.EndsAt == null)
            .ThenByDescending(s => s.EndsAt)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// De rijen die op dit moment gelden. Ook "pending" telt zodra de begindatum voorbij is: zo'n rij
    /// staat klaar omdat er daarvóór nog een betaalde periode liep, en die is dan net afgelopen. De
    /// dagelijkse controle zet hem daarna netjes op "active", maar het antwoord moet ook kloppen op de
    /// uren daartussen; anders is iemand een halve dag zijn Plus kwijt door een achtergrondtaak.
    /// </summary>
    private IQueryable<Subscription> ActieveRijenQuery(Guid homeId, DateTimeOffset now) =>
        db.Subscriptions
            .IgnoreQueryFilters()
            .Where(s => s.HouseholdId == homeId
                        && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Pending)
                        && s.StartsAt <= now
                        && (s.EndsAt == null || s.EndsAt > now));

    private static string Uitleg(Subscription rij, DateTimeOffset now) => rij.Source switch
    {
        SubscriptionSource.Trial => rij.EndsAt is { } tot
            ? $"Proefperiode, nog {Dagen(tot - now)} te gaan."
            : "Proefperiode.",
        SubscriptionSource.Installer => rij.EndsAt is { } tot
            ? $"Plus via je installateur, tot {tot:dd-MM-yyyy}."
            : "Plus via je installateur.",
        SubscriptionSource.Apple => "Plus via je abonnement in de App Store.",
        SubscriptionSource.Google => "Plus via je abonnement in Google Play.",
        SubscriptionSource.Manual => "Plus, door Wattzicht ingesteld.",
        _ => "Plus.",
    };

    private static int Dagen(TimeSpan resterend) => Math.Max(0, (int)Math.Ceiling(resterend.TotalDays));
}
