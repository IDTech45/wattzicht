using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;

namespace Wattzicht.Api.Services;

/// <summary>Wat er met een lopend abonnement gebeurde toen er een nieuwe bron bij kwam.</summary>
/// <param name="Subscription">De rij die vanaf nu (of later) geldt.</param>
/// <param name="StartedLater">
/// Of de nieuwe rij pas ingaat als de oude is uitgelopen. Dat gebeurt als iemand al betaald heeft:
/// die periode maken we af, anders betaalt hij voor niets.
/// </param>
/// <param name="AdviseStoreCancel">
/// Of de bewoner erop gewezen moet worden dat hij zijn abonnement bij Apple of Google kan opzeggen.
/// Wij kunnen dat niet voor hem doen; dat kan alleen hij, in de store.
/// </param>
public sealed record SubscriptionChange(Subscription Subscription, bool StartedLater, bool AdviseStoreCancel);

/// <summary>
/// Schrijft abonnementen weg, en bewaakt daarbij de regel waar het om draait: per woning betaalt
/// precies één partij. De database dwingt dat af met een unieke index op de actieve rij; deze klasse
/// zorgt dat je daar niet tegenaan loopt, en dat niemand ergens tussenin zonder Plus komt te zitten.
///
/// Twee gevallen bij een bronwissel:
///
/// Iemand heeft zelf betaald in de store en zijn installateur neemt het over. Dan laten we de
/// betaalde periode gewoon uitlopen en begint de installateursperiode daarna. Hem meteen afsluiten
/// zou betekenen dat hij voor dagen betaalt die hij niet krijgt, en terugbetalen kunnen wij niet: dat
/// geld staat bij Apple of Google. Hij krijgt wel het advies om in de store op te zeggen.
///
/// Iemand zit in zijn proefperiode of heeft Plus via zijn installateur, en koopt zelf. Dan stopt de
/// oude rij nu en begint de nieuwe meteen: er gaat niets verloren, want daar is niet voor betaald.
/// </summary>
public sealed class SubscriptionWriter(AppDbContext db, TimeProvider clock, ILogger<SubscriptionWriter> logger)
{
    /// <summary>
    /// Zet een abonnement klaar voor deze woning.
    /// </summary>
    /// <param name="homeId">De woning.</param>
    /// <param name="source">Waar het vandaan komt; zie <see cref="SubscriptionSource"/>.</param>
    /// <param name="duur">Hoe lang het loopt, of <c>null</c> voor doorlopend (verlengt vanzelf).</param>
    /// <param name="externalId">Het nummer bij de store, als die er is.</param>
    /// <param name="installerId">Bij bron "installer": welke installateur.</param>
    /// <param name="ct">Afbreektoken.</param>
    public async Task<SubscriptionChange> StartAsync(
        Guid homeId,
        string source,
        TimeSpan? duur,
        string? externalId = null,
        Guid? installerId = null,
        CancellationToken ct = default)
    {
        if (!SubscriptionSource.Bestaat(source))
        {
            throw new ArgumentException($"Onbekende abonnementsbron: {source}", nameof(source));
        }

        var now = clock.GetUtcNow();

        // Bestaat deze store-aankoop al? Dan is dit een verlenging en geen nieuw abonnement. Meldingen
        // van Apple en Google komen soms dubbel binnen, dus dit moet hetzelfde antwoord blijven geven.
        if (!string.IsNullOrEmpty(externalId))
        {
            var bestaand = await db.Subscriptions.IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.ExternalId == externalId && s.HouseholdId == homeId, ct);
            if (bestaand is not null)
            {
                bestaand.Status = SubscriptionStatus.Active;
                bestaand.EndsAt = duur is null ? null : now + duur.Value;
                await db.SaveChangesAsync(ct);
                return new SubscriptionChange(bestaand, StartedLater: false, AdviseStoreCancel: false);
            }
        }

        var lopend = await db.Subscriptions.IgnoreQueryFilters()
            .Where(s => s.HouseholdId == homeId
                        && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Pending))
            .OrderByDescending(s => s.EndsAt == null)
            .ThenByDescending(s => s.EndsAt)
            .ToListAsync(ct);

        var start = now;
        var laterStarten = false;
        var adviseerOpzeggen = false;

        // Een rij die nog klaarstond maar nooit is begonnen vervalt: er is iets anders afgesproken.
        foreach (var klaar in lopend.Where(s => s.Status == SubscriptionStatus.Pending))
        {
            klaar.Status = SubscriptionStatus.Cancelled;
        }

        var huidig = lopend.FirstOrDefault(s => s.Status == SubscriptionStatus.Active);
        if (huidig is not null)
        {
            var isBetaald = huidig.Source is SubscriptionSource.Apple or SubscriptionSource.Google;

            if (isBetaald && huidig.EndsAt is { } tot && tot > now)
            {
                // Hij heeft tot die datum betaald. Dat geld staat bij Apple of Google en kunnen wij
                // niet terugbetalen, dus die periode maakt hij gewoon af. De oude rij blijft actief en
                // de nieuwe staat klaar; zo is er geen moment zonder Plus en toch nooit dubbel.
                start = tot;
                laterStarten = true;
                adviseerOpzeggen = true;
            }
            else
            {
                huidig.Status = huidig.EndsAt is { } e && e <= now
                    ? SubscriptionStatus.Expired
                    : SubscriptionStatus.Cancelled;
                huidig.EndsAt = huidig.EndsAt is { } eind && eind < now ? huidig.EndsAt : now;
                adviseerOpzeggen = isBetaald;
            }
        }

        var rij = new Subscription
        {
            Id = Guid.NewGuid(),
            HouseholdId = homeId,
            Source = source,
            ExternalId = externalId,
            InstallerId = installerId,
            Status = laterStarten ? SubscriptionStatus.Pending : SubscriptionStatus.Active,
            StartsAt = start,
            EndsAt = duur is null ? null : start + duur.Value,
            CreatedAt = now,
        };

        db.Subscriptions.Add(rij);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Abonnement {Source} voor woning {Home} van {Start:u} tot {Eind} ({Status})",
            source, homeId, start, rij.EndsAt?.ToString("u") ?? "doorlopend", rij.Status);

        return new SubscriptionChange(rij, laterStarten, adviseerOpzeggen);
    }

    /// <summary>
    /// De proefperiode van dertig dagen bij de eerste koppeling. Eén keer per woning: iemand die zijn
    /// apparaat loskoppelt en opnieuw koppelt krijgt geen tweede proefperiode, en iemand die al een
    /// abonnement heeft krijgt hem ook niet (dat zou zijn betaalde periode onderbreken).
    /// </summary>
    public async Task<Subscription?> StartTrialIfFirstAsync(Guid homeId, CancellationToken ct = default)
    {
        var alEens = await db.Subscriptions.IgnoreQueryFilters()
            .AnyAsync(s => s.HouseholdId == homeId, ct);
        if (alEens) return null;

        var change = await StartAsync(homeId, SubscriptionSource.Trial, EntitlementService.TrialDuur, ct: ct);
        return change.Subscription;
    }

    /// <summary>Zet een lopend abonnement stop, bijvoorbeeld als een installateur een klant loslaat.</summary>
    public async Task StopAsync(Guid homeId, string reden, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var lopend = await db.Subscriptions.IgnoreQueryFilters()
            .Where(s => s.HouseholdId == homeId
                        && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Pending))
            .ToListAsync(ct);

        foreach (var rij in lopend)
        {
            rij.Status = SubscriptionStatus.Cancelled;
            rij.EndsAt = rij.EndsAt is { } eind && eind < now ? eind : now;
        }

        if (lopend.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Abonnement van woning {Home} gestopt: {Reden}", homeId, reden);
        }
    }
}
