using Wattzicht.Api.Data;
using Wattzicht.Shared;
using Wattzicht.Shared.Taal;

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
    public static (string Title, string Body) ChargeSessionMessage(
        Laadsessie sessie, int? zonnigUurMorgen, string? taal = null)
    {
        var kwh = sessie.Kwh.ToString("0.#", Teksten.Cultuur(taal));

        if (!sessie.HerkomstBekend)
        {
            // Geen P1-meter of CT-klemmen: dan weten we alleen hoeveel erin ging. Een zonaandeel van
            // nul tonen zou betekenen "alles van het net", en dat weten we niet.
            return (T(taal, "push.auto-klaar"), T(taal, "push.er-ging-kwh-in", kwh));
        }

        var zon = sessie.ZonKwh.ToString("0.#", Teksten.Cultuur(taal));
        var aandeel = (int)Math.Round(sessie.ZonAandeel * 100);

        if (aandeel >= 60)
        {
            return (T(taal, "push.geladen-op-eigen-zon"),
                T(taal, "push.kwh-waarvan-zon-procent", kwh, zon, aandeel));
        }

        var body = T(taal, "push.kwh-waarvan-zon", kwh, zon);
        if (zonnigUurMorgen is { } uur)
        {
            body += " " + T(taal, "push.morgen-zonnigste-uur", uur.ToString("00"));
        }

        return (T(taal, "push.auto-klaar"), body);
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
    public static (string Title, string Body) SmartMomentMessage(
        DateTimeOffset startUtc, decimal savingEur, double solarKwh, string? taal = null)
    {
        var tijd = DutchTime.LocalHour(startUtc).ToString("00") + ":00";
        var zon = solarKwh >= 0.5;

        var titel = T(taal, zon ? "push.straks-op-eigen-zon" : "push.straks-voordeligst");
        var body = T(taal, zon ? "push.vanaf-tijd-zon" : "push.vanaf-tijd-goedkoopst", tijd);

        if (savingEur >= 0.10m)
        {
            body += " " + T(taal, "push.dat-scheelt-ongeveer", savingEur.ToString("0.00", Teksten.Cultuur(taal)));
        }

        return (titel, body);
    }

    /// <summary>Het uur (lokaal) waarop de melding over morgen uitgaat.</summary>
    public const int ZonMorgenUur = 20;

    /// <summary>
    /// Zoveel tijd moet er tussen een laadmoment-melding en de melding over morgen zitten.
    ///
    /// Die twee kunnen op dezelfde avond vallen, en dan staan er twee berichten over laden onder
    /// elkaar die het ook nog oneens zijn: het ene zegt "zet hem er nu in, de stroom is goedkoop",
    /// het andere "wacht tot morgenmiddag". Dan is zwijgen beter dan kiezen.
    /// </summary>
    public static readonly TimeSpan ZonMorgenRust = TimeSpan.FromHours(3);

    /// <summary>
    /// Mag de melding over morgen nu uit?
    ///
    /// Eén keer per dag, en alleen in het uur van acht. Eerder weet je niet of de verwachting voor
    /// morgen nog verschuift, later ligt de helft van de mensen in bed en is het geen avond meer.
    /// </summary>
    public static bool MaySendZonMorgen(Household household, DateTimeOffset now)
    {
        if (!household.NotifyZonMorgen)
        {
            return false;
        }

        if (DutchTime.LocalHour(now) != ZonMorgenUur)
        {
            return false;
        }

        if (household.LastSmartMomentPushAt is { } laadmoment && now - laadmoment < ZonMorgenRust)
        {
            return false;
        }

        // Op de dag zelf vergelijken en niet op een tussenpoos: twee keer in hetzelfde uur van acht
        // op twee opeenvolgende avonden mag, twee keer op één avond niet.
        return household.LastZonMorgenPushAt is not { } last
               || DutchTime.LocalDate(last) < DutchTime.LocalDate(now);
    }

    /// <summary>
    /// De melding over morgen.
    ///
    /// Eerst wanneer, dan wat je ermee doet. De tijden staan als heel uur in de tekst omdat de
    /// verwachting niet nauwkeuriger is dan dat; "tussen 11:12 en 14:48" zou een precisie suggereren
    /// die er niet is.
    /// </summary>
    public static (string Title, string Body) ZonMorgenMessage(ZonBlokDto blok, string? taal = null)
    {
        var titel = T(taal, "push.morgen-zon-titel");
        var tekst = T(taal, "push.morgen-zon-tekst",
            blok.VanUur.ToString("00") + ":00",
            blok.TotUur.ToString("00") + ":00");

        return (titel, tekst);
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
    public static (string Title, string Body) AlertMessage(
        IReadOnlyList<Alert> opened, IReadOnlyDictionary<Guid, string> deviceNames, string? taal = null)
    {
        if (opened.Count == 1)
        {
            var alert = opened[0];
            var name = deviceNames.TryGetValue(alert.DeviceId, out var n) ? n : T(taal, "push.een-apparaat");
            return (T(taal, "push.storing-titel", name), alert.Message);
        }

        return (T(taal, "push.meerdere-storingen", opened.Count), T(taal, "push.meerdere-storingen-tekst"));
    }

    /// <summary>Een tekst uit het taalpakket, in de taal van het huishouden.</summary>
    private static string T(string? taal, string sleutel, params object[] waarden)
    {
        var tekst = Teksten.T(taal, sleutel);
        return waarden.Length == 0 ? tekst : string.Format(tekst, waarden);
    }
}
