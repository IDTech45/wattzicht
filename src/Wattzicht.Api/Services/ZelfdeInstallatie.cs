using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Services;

/// <summary>
/// Is dit apparaat er al, langs een andere weg?
///
/// Een installatie kan op twee manieren binnenkomen: via de gateway in het huis, die de app op het
/// thuisnetwerk uitleest, en via de cloud van de fabrikant. Die twee geven een ander kenmerk terug.
/// Bij Enphase is dat "enphase-lokaal-192.168.178.59" tegenover het systeemnummer uit Enlighten.
///
/// Op het kenmerk alleen zijn dat dus twee apparaten, en de dagtotalen tellen alle apparaten van een
/// woning bij elkaar op. Wie zijn dak langs beide wegen koppelt zou daardoor het dubbele opgewekt
/// zien van wat er werkelijk op het dak ligt.
///
/// Eén dak is één omvormer. De tweede koppeling is een extra bron, geen extra installatie.
/// </summary>
public static class ZelfdeInstallatie
{
    /// <summary>Een kenmerk van een apparaat dat wij via het thuisnetwerk uitlezen.</summary>
    public static bool Lokaal(string? kenmerk) =>
        kenmerk is not null && kenmerk.Contains("-lokaal-", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Het apparaat dat hier al voor staat, of niets.
    ///
    /// Eerst op kenmerk, want dat is zeker: dan koppelt iemand hetzelfde apparaat opnieuw en is dit
    /// een reparatie. Lukt dat niet, dan telt een apparaat van hetzelfde soort waarvan de ene weg
    /// lokaal is en de andere niet: dat is dezelfde installatie langs de andere kant.
    /// </summary>
    public static Device? Kies(IEnumerable<Device> bestaand, DeviceType soort, string? kenmerk)
    {
        var lijst = bestaand as IReadOnlyList<Device> ?? bestaand.ToList();

        return lijst.FirstOrDefault(d => d.ExternalId == kenmerk && d.Type == soort)
               ?? lijst.FirstOrDefault(d => d.Type == soort && Lokaal(d.ExternalId) != Lokaal(kenmerk));
    }
}
