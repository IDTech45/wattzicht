namespace Wattzicht.Api.Services;

/// <summary>
/// De abonnementen die een bewoner kan kiezen.
///
/// Let op wat dit wel en niet doet: het legt de keuze vast bij het huishouden, zodat je in het
/// beheerpaneel ziet wie wat koos. Er wordt niets afgeschreven, want er is nog geen betaalpartij
/// aangesloten. Zolang dat zo is, is het een voorkeur en geen verplichting.
/// </summary>
public static class Plans
{
    /// <summary>De installateur betaalt; de bewoner betaalt niets.</summary>
    public const string ViaInstaller = "installer";

    /// <summary>Per maand.</summary>
    public const string Monthly = "monthly";

    /// <summary>Per jaar, goedkoper dan twaalf maanden.</summary>
    public const string Yearly = "yearly";

    public static readonly string[] Alle = [ViaInstaller, Monthly, Yearly];

    /// <summary>Maakt er een geldige waarde van; onbekend of leeg wordt "via de installateur".</summary>
    public static string Normaliseer(string? gekozen)
    {
        var waarde = (gekozen ?? "").Trim().ToLowerInvariant();
        return Alle.Contains(waarde) ? waarde : ViaInstaller;
    }
}
