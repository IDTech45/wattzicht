using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Services;

/// <summary>
/// Welke installaties aandacht nodig hebben, en waarom.
///
/// Waarom dit bestaat: een installateur deelt deze app uit omdat hij er iets aan heeft, en dat is
/// niet een lijst met klanten die het goed doen. Het is de ene klant wiens panelen sinds vorige week
/// een derde minder leveren, of wiens omvormer er al drie dagen uit ligt. Die stond er wel tussen,
/// maar je moest hem zelf zien te vinden tussen de rest.
///
/// Eén bron voor beide kanten: het portaal toont deze lijst bovenaan, en zodra Wattzicht mail kan
/// versturen gaat precies dezelfde lijst als dagbericht de deur uit.
/// </summary>
public sealed class AandachtService(AppDbContext db, InsightsService insights, TimeProvider clock)
{
    /// <summary>Zo lang stil voordat we het een probleem noemen. Een avondje router herstarten is geen storing.</summary>
    public static readonly TimeSpan StilVanaf = TimeSpan.FromHours(36);

    /// <summary>
    /// De installaties van deze installateur die aandacht nodig hebben, de ergste eerst.
    /// </summary>
    public async Task<List<AandachtDto>> VoorInstallateurAsync(Guid installerId, CancellationToken ct)
    {
        var nu = clock.GetUtcNow();
        var vandaag = DutchTime.LocalDate(nu);

        // Alleen woningen die de installateur mag zien. Zonder toestemming staat hij er niet tussen,
        // ook niet in een lijst met problemen.
        var woningen = await db.Households
            .Where(h => h.InstallerId == installerId && h.InstallerAccessGranted)
            .Select(h => new
            {
                h.Id,
                h.Name,
                h.City,
                Apparaten = h.Devices.Select(d => new { d.Id, d.Name, d.Type, d.LastSeenAt, d.LastError }).ToList(),
            })
            .ToListAsync(ct);

        var lijst = new List<AandachtDto>();

        foreach (var woning in woningen)
        {
            if (woning.Apparaten.Count == 0) continue;

            // 1. Stil: geen enkel apparaat heeft recent iets gestuurd.
            var laatste = woning.Apparaten.Max(a => a.LastSeenAt);
            if (laatste is null || nu - laatste.Value > StilVanaf)
            {
                lijst.Add(new AandachtDto(
                    woning.Id,
                    woning.Name,
                    woning.City,
                    AandachtSoort.Stil,
                    laatste,
                    null,
                    woning.Apparaten.FirstOrDefault(a => a.LastError != null)?.LastError));
                continue;
            }

            // 2. Levert te weinig voor het weer dat er was.
            var prestatie = await insights.PerformanceAsync(woning.Id, vandaag, ct);
            if (prestatie.Status == PerformanceStatus.Low)
            {
                lijst.Add(new AandachtDto(
                    woning.Id,
                    woning.Name,
                    woning.City,
                    AandachtSoort.LeverT,
                    laatste,
                    prestatie.PercentOfExpected,
                    prestatie.Message));
            }
        }

        // Stil staan weegt zwaarder dan minder leveren: dan is er helemaal niets.
        return lijst
            .OrderBy(a => a.Soort == AandachtSoort.Stil ? 0 : 1)
            .ThenBy(a => a.PercentVanVerwacht ?? 0)
            .ToList();
    }
}
