using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;

namespace Wattzicht.Api.Services;

/// <summary>
/// Haalt accounts weg waarvan het e-mailadres nooit is bevestigd.
///
/// Dit is de helft van de bevestiging die echt iets doet. Een bot vult een adres in dat hij niet
/// kan lezen, klikt dus nooit op de link, en liet tot nu toe een woning achter in de database: in
/// november waren 139 van de 145 woningen op die manier ontstaan. Een vinkje "bevestigd" dat
/// nergens gevolgen heeft zou daar niets aan veranderen.
///
/// Wie wel echt is maar zijn mail een week niet leest, verliest hier niets van waarde: zonder
/// bevestiging heeft hij ook nooit iets gekoppeld, en aanmelden kan opnieuw met hetzelfde adres.
/// Dat is de reden dat een woning met een apparaat erin buiten schot blijft, ook zonder
/// bevestiging. Daar zit werk in dat iemand heeft gedaan, en dan weegt de twijfel de andere kant
/// op.
/// </summary>
public static class OnbevestigdOpruimen
{
    /// <summary>
    /// Verwijdert wat te lang onbevestigd staat en geeft terug hoeveel accounts dat waren.
    /// </summary>
    public static async Task<int> UitvoerenAsync(AppDbContext db, DateTimeOffset nu, CancellationToken ct = default)
    {
        var grens = nu - Emailbevestiging.BewaartermijnOnbevestigd;

        var kandidaten = await db.Users.IgnoreQueryFilters()
            .Where(u => u.EmailVerifiedAt == null && u.CreatedAt < grens)
            .Select(u => new { u.Id, u.HouseholdId })
            .ToListAsync(ct);

        if (kandidaten.Count == 0) return 0;

        var weg = 0;
        foreach (var kandidaat in kandidaten)
        {
            if (kandidaat.HouseholdId is { } woningId)
            {
                // Hangt er een apparaat aan, dan is hier iemand echt bezig geweest. Laat staan.
                if (await db.Devices.IgnoreQueryFilters().AnyAsync(d => d.HouseholdId == woningId, ct))
                {
                    continue;
                }

                // Deelt deze woning met een ander, bevestigd account, dan gaat alleen dit ene
                // account weg en blijft de woning van de ander overeind.
                var medebewoners = await db.Users.IgnoreQueryFilters()
                    .CountAsync(u => u.HouseholdId == woningId && u.Id != kandidaat.Id, ct);

                if (medebewoners == 0)
                {
                    await HuishoudenVerwijderen.UitvoerenAsync(db, woningId, ct);
                    weg++;
                    continue;
                }
            }

            var gebruiker = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == kandidaat.Id, ct);
            if (gebruiker is null) continue;

            db.Users.Remove(gebruiker);
            await db.SaveChangesAsync(ct);
            weg++;
        }

        return weg;
    }
}
