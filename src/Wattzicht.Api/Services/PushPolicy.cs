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

    /// <summary>Minimaal zoveel tijd tussen twee storingsmeldingen (tegen een apparaat dat steeds aan/uit flappert).</summary>
    public static readonly TimeSpan AlertInterval = TimeSpan.FromHours(2);

    /// <summary>Hoeveel tijd vóór het goedkope uur we de melding sturen; genoeg om de stekker erin te doen.</summary>
    public static readonly TimeSpan SmartMomentLeadTime = TimeSpan.FromMinutes(45);

    /// <summary>Eén laadmoment-melding per dag. Twee is al drammen, en het advies verandert nauwelijks.</summary>
    public static readonly TimeSpan SmartMomentInterval = TimeSpan.FromHours(20);

    /// <summary>Niet 's nachts wakker maken: een melding tussen middernacht en zeven uur helpt niemand.</summary>
    public const int SmartMomentFromHour = 7;

    /// <summary>En 's avonds laat ook niet meer.</summary>
    public const int SmartMomentUntilHour = 23;

    /// <summary>
    /// Mag er nu een melding over het slimme laadmoment uit?
    ///
    /// Let op het verschil met de stille uren: het goedkope uur zelf ligt vaak juist 's nachts, en
    /// dat is prima. Het gaat erom wanneer we de bewoner storen, niet wanneer de auto laadt.
    /// </summary>
    public static bool MaySendSmartMoment(Household household, DateTimeOffset now)
    {
        if (!household.NotifySmartMoment)
        {
            return false;
        }

        var hour = DutchTime.LocalHour(now);
        if (hour < SmartMomentFromHour || hour >= SmartMomentUntilHour)
        {
            return false;
        }

        return household.LastSmartMomentPushAt is not { } last || now - last >= SmartMomentInterval;
    }

    /// <summary>Titel en tekst van de laadmoment-melding.</summary>
    public static (string Title, string Body) SmartMomentMessage(DateTimeOffset startUtc, decimal savingEur, double solarKwh)
    {
        var tijd = DutchTime.LocalHour(startUtc).ToString("00") + ":00";
        var titel = solarKwh >= 0.5
            ? "Straks laden op je eigen zon"
            : "Straks is laden het voordeligst";

        var body = solarKwh >= 0.5
            ? $"Vanaf {tijd} draaien je panelen genoeg om de auto te vullen. Zet hem nu aan de stekker."
            : $"Vanaf {tijd} is stroom het goedkoopst. Zet je auto aan de stekker.";

        if (savingEur >= 0.10m)
        {
            body += $" Dat scheelt ongeveer € {savingEur:0.00}.";
        }

        return (titel, body);
    }

    public static bool MaySendAlert(Household household, DateTimeOffset now) =>
        household.NotifyAlerts
        && (household.LastAlertPushAt is not { } last || now - last >= AlertInterval);

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
