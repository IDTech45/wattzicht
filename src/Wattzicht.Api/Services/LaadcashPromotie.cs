using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Services;

/// <summary>Via welk kanaal een promotie bij iemand terechtkomt.</summary>
public static class PromoChannel
{
    public const string Popup = "popup";
    public const string Tile = "tile";
    public const string Push = "push";
    public const string Email = "email";

    public static readonly string[] Alle = [Popup, Tile, Push, Email];
}

/// <summary>Wat er met een promotie gebeurde.</summary>
public static class PromoAction
{
    public const string Shown = "shown";
    public const string Clicked = "clicked";
    public const string Dismissed = "dismissed";
    public const string Optout = "optout";

    public static readonly string[] Alle = [Shown, Clicked, Dismissed, Optout];
}

/// <summary>De campagnes die we voeren.</summary>
public static class PromoCampaign
{
    public const string Laadcash = "laadcash";
}

/// <summary>
/// Alle teksten van de LaadCash-promotie op één plek, zodat ze aan te passen zijn zonder de code in
/// te duiken. Het bedrag komt er als parameter in; nergens anders staat een tekst met een bedrag.
/// </summary>
public static class LaadcashTeksten
{
    public const string PopupTitel = "Je laadpaal kan geld opleveren";

    public static string PopupTekst(decimal perJaar) =>
        $"Je laadpaal kan ongeveer € {perJaar:0} per jaar opleveren. LaadCash verkoopt je groene "
        + "laadbewijzen (ERE's) aan brandstofbedrijven; jij houdt je stroom en laadt zoals altijd.";

    public const string PopupKnopJa = "Aanmelden bij LaadCash";
    public const string PopupKnopLater = "Later";

    public static string TegelTitel(decimal perJaar) => $"€ {perJaar:0} per jaar niet opgehaald";
    public const string TegelTekst = "Je ERE's wachten. Aanmelden duurt twee minuten.";

    public const string PushTitel = "Denk aan je ERE's";

    public static string PushTekst(double kwhDezeMaand, decimal euroDezeMaand) =>
        $"Je hebt deze maand {kwhDezeMaand:0} kWh geladen. Dat is ongeveer € {euroDezeMaand:0} aan "
        + "ERE's die je nu laat liggen.";

    public const string MailOnderwerp = "Je laadpaal levert nu nog niets op";

    public static string MailTekst(decimal perJaar, double kwhPerJaar) =>
        $"Je laadpaal laadde het afgelopen jaar ongeveer {kwhPerJaar:0} kWh. Met LaadCash was dat "
        + $"ongeveer € {perJaar:0} geweest.\n\n"
        + "Hoe het werkt: je laadt gewoon thuis zoals je nu doet. LaadCash registreert die groene "
        + "kilowatturen als ERE's bij de Nederlandse Emissieautoriteit, verkoopt ze aan "
        + "brandstofbedrijven en stort jouw deel op je rekening. Je stroom blijft van jou.";

    public const string MailJaarOnderwerp = "Wat je laadpaal dit jaar had kunnen opleveren";

    public static string MailJaarTekst(decimal perJaar, double kwhDitJaar) =>
        $"Je hebt dit jaar {kwhDitJaar:0} kWh thuis geladen. Met LaadCash was dat € {perJaar:0} "
        + "geweest.\n\nVanaf 1 januari telt het nieuwe jaar. Wat je dit jaar hebt geladen kan niet "
        + "meer ingeboekt worden, maar vanaf de dag dat je je aanmeldt telt alles mee.";

    public const string Afmelden = "Geen berichten meer hierover";
}

/// <summary>Wat de app moet tonen over LaadCash.</summary>
/// <param name="Show">Of er iets te tonen valt.</param>
/// <param name="ShowPopup">Of de eenmalige popup nog open mag; daarna alleen de tegel.</param>
/// <param name="EstimatedYearlyEur">Het geschatte bedrag per jaar, afgerond op tientallen.</param>
/// <param name="ChargedKwhPerYear">Waarop dat bedrag gebaseerd is.</param>
/// <param name="Title">Titel voor de tegel.</param>
/// <param name="Body">Tekst voor de tegel.</param>
/// <param name="PopupTitle">Titel voor de popup.</param>
/// <param name="PopupBody">Tekst voor de popup.</param>
/// <param name="DeepLink">Waar de knop heen gaat, met woning en e-mailadres erin.</param>
/// <param name="Linked">Of deze woning al bij LaadCash zit.</param>
/// <param name="EarnedThisYearEur">De echte opbrengst, zodra er een koppeling is.</param>
public sealed record LaadcashPromoDto(
    bool Show,
    bool ShowPopup,
    decimal EstimatedYearlyEur,
    double ChargedKwhPerYear,
    string? Title,
    string? Body,
    string? PopupTitle,
    string? PopupBody,
    string? DeepLink,
    bool Linked,
    decimal? EarnedThisYearEur);

/// <summary>
/// De LaadCash-promotie: iedereen met een laadpaal die nog geen LaadCash heeft, hoort te weten wat
/// hij laat liggen.
///
/// Dit is ons eigen product, dus het mag nadrukkelijker dan een gewone aanbieding. Maar niet
/// irritant: één popup bij het koppelen, daarna een tegel die blijft staan, en hoogstens vier
/// pushmeldingen met weken ertussen. Wie "niet meer tonen" kiest, hoort er nooit meer iets over.
///
/// Twee regels die het verschil maken tussen overtuigend en ongeloofwaardig. De eerste: nooit
/// promoten voordat er een laadsessie is uitgelezen. Een bedrag noemen op basis van nul data is
/// gokken, en als dat bedrag later niet klopt gelooft niemand de rest ook nog. De tweede: onder de
/// vijftig euro per jaar geen melding. Dan is het de moeite van een aanmelding niet waard en zou je
/// iemand lastigvallen voor een tientje.
/// </summary>
public sealed class LaadcashPromotie(AppDbContext db, TimeProvider clock, IConfiguration config)
{
    /// <summary>Wat een kilowattuur aan ERE's oplevert voor de klant, na onze commissie.</summary>
    public const decimal EuroPerKwh = 0.118m;

    /// <summary>Onder dit bedrag per jaar promoten we niet actief.</summary>
    public const decimal MinimumPerJaar = 50m;

    /// <summary>Eerste herinnering: een week na het koppelen.</summary>
    public static readonly TimeSpan EerstePush = TimeSpan.FromDays(7);

    /// <summary>Daarna elke maand.</summary>
    public static readonly TimeSpan PushInterval = TimeSpan.FromDays(30);

    /// <summary>Niet vaker dan dit, hoe lang hij ook blijft zitten.</summary>
    public const int MaximumPushes = 4;

    /// <summary>De mail met uitleg: drie dagen na het koppelen.</summary>
    public static readonly TimeSpan EersteMail = TimeSpan.FromDays(3);

    /// <summary>Niet 's nachts pushen.</summary>
    public const int VanafUur = 9;
    public const int TotUur = 20;

    private string Basis => config["Laadcash:RegisterUrl"] ?? "https://laadcash.nl/register";

    /// <summary>
    /// Wat de app moet tonen. Eén aanroep voor de popup én de tegel: de app hoeft dan geen regels te
    /// kennen over wanneer wat mag, en die regels staan maar op één plek.
    /// </summary>
    public async Task<LaadcashPromoDto> VoorWoningAsync(Guid homeId, CancellationToken ct = default)
    {
        var woning = await db.Households.IgnoreQueryFilters()
            .FirstOrDefaultAsync(h => h.Id == homeId, ct);

        if (woning is null)
        {
            return Niets();
        }

        if (woning.LaadcashLinkedAt is not null)
        {
            // Al klant: niets promoten, alleen laten zien wat het oplevert.
            return new LaadcashPromoDto(
                Show: true, ShowPopup: false, EstimatedYearlyEur: 0, ChargedKwhPerYear: 0,
                Title: "ERE-opbrengst dit jaar", Body: null, PopupTitle: null, PopupBody: null,
                DeepLink: null, Linked: true, EarnedThisYearEur: null);
        }

        if (woning.LaadcashOptOut)
        {
            return Niets();
        }

        var (kwhPerJaar, heeftSessies) = await GeladenPerJaarAsync(homeId, ct);
        if (!heeftSessies)
        {
            // Er is een laadpaal gekoppeld maar nog nooit iets uitgelezen. Een bedrag noemen zou
            // gokken zijn, en een gegokt bedrag dat later niet klopt kost je het vertrouwen.
            return Niets();
        }

        var perJaar = Afgerond((decimal)kwhPerJaar * EuroPerKwh);
        if (perJaar < MinimumPerJaar)
        {
            return Niets();
        }

        var email = await db.Users.IgnoreQueryFilters()
            .Where(u => u.HouseholdId == homeId)
            .OrderBy(u => u.CreatedAt)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(ct);

        var popupAlGehad = await db.PromoEvents
            .AnyAsync(e => e.HouseholdId == homeId
                           && e.Campaign == PromoCampaign.Laadcash
                           && e.Channel == PromoChannel.Popup, ct);

        return new LaadcashPromoDto(
            Show: true,
            ShowPopup: !popupAlGehad,
            EstimatedYearlyEur: perJaar,
            ChargedKwhPerYear: kwhPerJaar,
            Title: LaadcashTeksten.TegelTitel(perJaar),
            Body: LaadcashTeksten.TegelTekst,
            PopupTitle: LaadcashTeksten.PopupTitel,
            PopupBody: LaadcashTeksten.PopupTekst(perJaar),
            DeepLink: DeepLink(homeId, email),
            Linked: false,
            EarnedThisYearEur: null);
    }

    /// <summary>
    /// Mag er nu een pushmelding uit? De regels staan hier en niet in de worker, zodat ze getoetst
    /// kunnen worden zonder achtergrondtaak.
    /// </summary>
    public async Task<bool> MagPushAsync(Guid homeId, DateTimeOffset nu, CancellationToken ct = default)
    {
        var lokaalUur = DutchTime.ToLocal(nu).Hour;
        if (lokaalUur < VanafUur || lokaalUur >= TotUur)
        {
            return false;
        }

        var promo = await VoorWoningAsync(homeId, ct);
        if (!promo.Show || promo.Linked)
        {
            return false;
        }

        var pushes = await db.PromoEvents
            .Where(e => e.HouseholdId == homeId
                        && e.Campaign == PromoCampaign.Laadcash
                        && e.Channel == PromoChannel.Push)
            .OrderByDescending(e => e.SentAt)
            .ToListAsync(ct);

        if (pushes.Count >= MaximumPushes)
        {
            return false;
        }

        // De eerste komt een week na het koppelen van de laadpaal, daarna elke maand.
        if (pushes.Count == 0)
        {
            var gekoppeld = await EersteLaadpaalAsync(homeId, ct);
            return gekoppeld is { } moment && nu - moment >= EerstePush;
        }

        return nu - pushes[0].SentAt >= PushInterval;
    }

    /// <summary>Mag de eerste uitlegmail eruit? Drie dagen na het koppelen, één keer.</summary>
    public async Task<bool> MagMailAsync(Guid homeId, DateTimeOffset nu, CancellationToken ct = default)
    {
        var promo = await VoorWoningAsync(homeId, ct);
        if (!promo.Show || promo.Linked) return false;

        var alGemaild = await db.PromoEvents
            .AnyAsync(e => e.HouseholdId == homeId
                           && e.Campaign == PromoCampaign.Laadcash
                           && e.Channel == PromoChannel.Email, ct);
        if (alGemaild) return false;

        var gekoppeld = await EersteLaadpaalAsync(homeId, ct);
        return gekoppeld is { } moment && nu - moment >= EersteMail;
    }

    /// <summary>Legt vast dat er iets is getoond, aangeklikt of weggeklikt.</summary>
    public async Task NoteerAsync(Guid homeId, string channel, string action, CancellationToken ct = default)
    {
        db.PromoEvents.Add(new PromoEvent
        {
            Id = Guid.NewGuid(),
            HouseholdId = homeId,
            Campaign = PromoCampaign.Laadcash,
            Channel = channel,
            Action = action,
            SentAt = clock.GetUtcNow(),
        });

        if (action == PromoAction.Optout)
        {
            var woning = await db.Households.IgnoreQueryFilters().FirstOrDefaultAsync(h => h.Id == homeId, ct);
            if (woning is not null) woning.LaadcashOptOut = true;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Hoeveel er in de auto geladen is over het afgelopen jaar. Is er minder dan een jaar aan data,
    /// dan rekenen we door naar een heel jaar; met een ondergrens van dertig dagen, want uit één week
    /// vakantie of één week thuiswerken valt geen jaar te voorspellen.
    /// </summary>
    public async Task<(double KwhPerJaar, bool HeeftSessies)> GeladenPerJaarAsync(
        Guid homeId, CancellationToken ct = default)
    {
        var nu = clock.GetUtcNow();
        var vanaf = DateOnly.FromDateTime(nu.UtcDateTime.AddDays(-365));

        var dagen = await db.DailySummaries.IgnoreQueryFilters()
            .Where(d => d.HouseholdId == homeId && d.Date >= vanaf && d.ChargedKwh > 0)
            .Select(d => new { d.Date, d.ChargedKwh })
            .ToListAsync(ct);

        if (dagen.Count == 0)
        {
            return (0, false);
        }

        var totaal = dagen.Sum(d => d.ChargedKwh);
        var eerste = dagen.Min(d => d.Date);
        var meetdagen = Math.Max(30, (DateOnly.FromDateTime(nu.UtcDateTime).DayNumber - eerste.DayNumber) + 1);

        return (totaal / meetdagen * 365, true);
    }

    /// <summary>Hoeveel er deze kalendermaand geladen is; voor de tekst van de pushmelding.</summary>
    public async Task<double> GeladenDezeMaandAsync(Guid homeId, CancellationToken ct = default)
    {
        var nu = DutchTime.ToLocal(clock.GetUtcNow());
        var eerste = new DateOnly(nu.Year, nu.Month, 1);

        return await db.DailySummaries.IgnoreQueryFilters()
            .Where(d => d.HouseholdId == homeId && d.Date >= eerste)
            .SumAsync(d => d.ChargedKwh, ct);
    }

    /// <summary>Wanneer de eerste laadpaal van deze woning gekoppeld is.</summary>
    public async Task<DateTimeOffset?> EersteLaadpaalAsync(Guid homeId, CancellationToken ct = default) =>
        await db.Devices.IgnoreQueryFilters()
            .Where(d => d.HouseholdId == homeId && d.Type == DeviceType.Charger)
            .OrderBy(d => d.CreatedAt)
            .Select(d => (DateTimeOffset?)d.CreatedAt)
            .FirstOrDefaultAsync(ct);

    /// <summary>De link naar LaadCash, met de woning en het e-mailadres erin zodat het formulier al vol staat.</summary>
    public string DeepLink(Guid homeId, string? email) =>
        $"{Basis}?src=wattzicht&home={homeId}"
        + (string.IsNullOrWhiteSpace(email) ? "" : $"&email={Uri.EscapeDataString(email)}");

    /// <summary>Afronden op tientallen: een bedrag als "€ 237" doet alsof we het preciezer weten dan we doen.</summary>
    public static decimal Afgerond(decimal bedrag) => Math.Round(bedrag / 10m, MidpointRounding.AwayFromZero) * 10m;

    private static LaadcashPromoDto Niets() =>
        new(false, false, 0, 0, null, null, null, null, null, false, null);
}
