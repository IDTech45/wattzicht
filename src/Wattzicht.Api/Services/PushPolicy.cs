using Wattzicht.Api.Data;

namespace Wattzicht.Api.Services;

/// <summary>
/// Wanneer sturen we een overschot-melding? Niet te vaak, niet 's nachts, en alleen als de bewoner het wil.
/// </summary>
public static class PushPolicy
{
    /// <summary>Minimaal zoveel tijd tussen twee overschot-meldingen.</summary>
    public static readonly TimeSpan SurplusInterval = TimeSpan.FromHours(3);

    /// <summary>Eerste uur (lokale tijd) waarop we een overschot-melding sturen.</summary>
    public const int SurplusFromHour = 9;

    /// <summary>Vanaf dit uur (lokale tijd) niet meer.</summary>
    public const int SurplusUntilHour = 20;

    public static bool MaySendSurplus(Household household, DateTimeOffset now)
    {
        if (!household.NotifySurplus)
        {
            return false;
        }

        var hour = DutchTime.LocalHour(now);
        if (hour < SurplusFromHour || hour >= SurplusUntilHour)
        {
            return false;
        }

        return household.LastSurplusPushAt is not { } last || now - last >= SurplusInterval;
    }

    /// <summary>Titel en tekst voor nieuw geopende storingen (één melding per pollronde).</summary>
    public static (string Title, string Body) AlertMessage(IReadOnlyList<Alert> opened, IReadOnlyDictionary<Guid, string> deviceNames)
    {
        if (opened.Count == 1)
        {
            var alert = opened[0];
            var name = deviceNames.TryGetValue(alert.DeviceId, out var n) ? n : "Een apparaat";
            return ($"Storing: {name}", alert.Message);
        }

        return ($"{opened.Count} storingen", "Een paar apparaten geven een melding. Open de app voor details.");
    }
}
