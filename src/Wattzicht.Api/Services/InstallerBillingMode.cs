namespace Wattzicht.Api.Services;

/// <summary>
/// Wat een installateur betaalt.
///
/// Triple Energie is de eerste en betaalt niets: die levert in ruil klanten. Dat is geen tijdelijke
/// coulance maar de afspraak, dus het staat als modus op de installateur en niet als korting.
/// </summary>
public static class InstallerBillingMode
{
    /// <summary>Betaalt niets; zijn bewoners krijgen twaalf maanden Plus cadeau.</summary>
    public const string Free = "free";

    /// <summary>Betaalt per actief huishouden per maand.</summary>
    public const string PerHousehold = "per_household";

    public static readonly string[] Alle = [Free, PerHousehold];

    public static bool Bestaat(string? modus) => modus is not null && Alle.Contains(modus);
}
