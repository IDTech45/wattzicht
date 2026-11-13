using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;

namespace Wattzicht.Api.Services;

/// <summary>
/// De regels rond documenten waar het inlezen niets uit haalde.
///
/// Dit is klantpost op onze server, dus er horen grenzen omheen te staan en die horen op één plek
/// te staan: hoe lang we het houden, hoeveel tekst we bewaren en hoe de naam wordt ingekort. Een
/// bewaartermijn die in een worker verstopt zit, is een bewaartermijn die niemand terugvindt.
/// </summary>
public static class ImportDagboek
{
    /// <summary>
    /// Hoe lang een mislukt document blijft staan.
    ///
    /// Dertig dagen. Lang genoeg om er een keer naar te kijken en het formaat toe te voegen, kort
    /// genoeg dat het geen archief van andermans energienota's wordt.
    /// </summary>
    public static readonly TimeSpan Bewaartermijn = TimeSpan.FromDays(30);

    /// <summary>
    /// Zoveel tekens bewaren we van de uitgelezen tekst.
    ///
    /// Een tarievenlijst of een verbruikstabel staat ruim binnen deze grens. De rest is de brief
    /// eromheen, en die hebben we niet nodig om een formaat te herkennen.
    /// </summary>
    public const int MaximumTekens = 20_000;

    public static string Afkappen(string tekst) =>
        tekst.Length <= MaximumTekens ? tekst : tekst[..MaximumTekens];

    public static string KorteNaam(string? naam) =>
        string.IsNullOrWhiteSpace(naam) ? "zonder naam"
        : naam.Length <= 200 ? naam
        : naam[..200];

    /// <summary>Wat over de bewaartermijn heen is, gaat weg.</summary>
    public static async Task<int> OpruimenAsync(AppDbContext db, DateTimeOffset nu, CancellationToken ct = default)
    {
        var verlopen = await db.FailedImports.IgnoreQueryFilters()
            .Where(x => x.ExpiresAt <= nu)
            .ToListAsync(ct);

        if (verlopen.Count == 0) return 0;

        db.FailedImports.RemoveRange(verlopen);
        await db.SaveChangesAsync(ct);
        return verlopen.Count;
    }
}
