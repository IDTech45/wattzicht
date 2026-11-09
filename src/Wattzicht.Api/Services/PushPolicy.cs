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
    /// Een bericht na een laadbeurt mag ook 's avonds laat, maar niet midden in de nacht. Wie om
    /// drie uur klaar is met laden hoort het 's ochtends; het nieuws loopt niet weg.
    /// </summary>
    public const int SessieFromHour = 7;

    /// <summary>En tot elf uur 's avonds; daarna is het geen nieuws meer maar een storing van je rust.</summary>
    public const int SessieUntilHour = 23;

    /// <summary>Mag er nu een bericht over een afgelopen laadbeurt uit?</summary>
    public static bool MaySendChargeSession(Household household, DateTimeOffset now)
    {
        if (!household.NotifyChargeSession) return false;
        var uur = DutchTime.LocalHour(now);
        return uur >= SessieFromHour && uur < SessieUntilHour;
    }

    /// <summary>
    /// Het bericht na een laadbeurt.
    ///
    /// Eerst wat er gebeurde, dan pas wat je er de volgende keer mee kunt. Die volgorde is bewust:
    /// wie net geladen heeft wil weten wat het heeft gedaan, niet meteen een nieuwe les. De
    /// verwachting voor morgen komt er alleen bij als er iets te winnen viel, dus als het grootste
    /// deel van het net kwam terwijl het morgen een zonnige dag wordt.
    /// </summary>
    /// <param name="sessie">De afgelopen laadbeurt.</param>
    /// <param name="zonnigUurMorgen">Het zonnigste uur van morgen (lokaal), of null als we dat niet weten.</param>
    public static (string Title, string Body) ChargeSessionMessage(Laadsessie sessie, int? zonnigUurMorgen)
    {
        var kwh = sessie.Kwh.ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("nl-NL"));

        if (!sessie.HerkomstBekend)
        {
            // Geen P1-meter of CT-klemmen: dan weten we alleen hoeveel erin ging. Een zonaandeel van
            // nul tonen zou betekenen "alles van het net", en dat weten we niet.
            return ("Je auto is klaar", $"Er ging {kwh} kWh in.");
        }

        var zon = sessie.ZonKwh.ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("nl-NL"));
        var aandeel = (int)Math.Round(sessie.ZonAandeel * 100);

        if (aandeel >= 60)
        {
            return ("Geladen op je eigen zon",
                $"Er ging {kwh} kWh in, waarvan {zon} kWh van je eigen dak. Dat is {aandeel} procent.");
        }

        var body = $"Er ging {kwh} kWh in, waarvan {zon} kWh van je eigen dak.";
        if (zonnigUurMorgen is { } uur)
        {
            body += $" Morgen staat de zon rond {uur:00}:00 op zijn hoogst; laden rond dat uur scheelt je het meest.";
        }
        return ("Je auto is klaar", body);
    }

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
