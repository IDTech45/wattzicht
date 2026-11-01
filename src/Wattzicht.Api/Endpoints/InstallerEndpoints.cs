using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Endpoints;

/// <summary>Installateursportal: overzicht van eigen huishoudens en white-label instellingen. Vereist rol Installer.</summary>
public static partial class InstallerEndpoints
{
    /// <summary>Maximale lengte van een logo als data-URL (~500 kB base64).</summary>
    private const int MaxLogoLength = 700_000;

    public static IEndpointRouteBuilder MapInstallerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/installer")
            .RequireAuthorization(AuthPolicies.Installer)
            .WithTags("Installateur");

        group.MapGet("/households", HouseholdsAsync)
            .WithSummary("Alle huishoudens van de installateur met status (platte lijst voor het portal).")
            .Produces<List<InstallerHouseholdDto>>();

        group.MapGet("/overview", OverviewAsync)
            .WithSummary("Zelfde lijst plus tellers en eigen white-label instellingen.")
            .Produces<InstallerOverviewDto>();

        group.MapGet("/households/{id:guid}", DetailAsync)
            .WithSummary("Detail van één huishouden: apparaten, meldingen, live, vandaag/maand en laatste 14 dagen.")
            .Produces<InstallerHouseholdDetailDto>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/households/{id:guid}/refresh", RefreshAsync)
            .WithSummary("Actie op afstand: alle apparaten van deze klant nu opnieuw uitlezen (max. 1x per minuut).")
            .Produces<InstallerActionResult>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status429TooManyRequests);

        group.MapPost("/households/{id:guid}/message", MessageAsync)
            .WithSummary("Actie op afstand: bericht aan de klant, komt als pushmelding binnen in Wattzicht.")
            .Produces<InstallerActionResult>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/billing", BillingAsync)
            .WithSummary("Wat deze maand te factureren valt: actieve huishoudens maal de prijs.")
            .Produces<InstallerBillingDto>();

        group.MapGet("/branding", GetBrandingAsync)
            .WithSummary("Eigen white-label instellingen: naam, logo (data-URL), kleur en offertelink.")
            .Produces<BrandingDto>();

        group.MapPut("/branding", UpdateBrandingAsync)
            .WithSummary("Past naam, logo, kleuren en offertelink aan. Alleen meegegeven velden veranderen.")
            .Produces<BrandingDto>()
            .ProducesValidationProblem();

        return app;
    }

    private static async Task<IResult> HouseholdsAsync(HttpContext http, AppDbContext db, InsightsService insights, TimeProvider clock, CancellationToken ct)
    {
        var installerId = http.User.GetInstallerId();
        return Results.Ok(await LoadHouseholdsAsync(db, insights, installerId, clock.GetUtcNow(), ct));
    }

    private static async Task<IResult> OverviewAsync(HttpContext http, AppDbContext db, InsightsService insights, TimeProvider clock, CancellationToken ct)
    {
        var installerId = http.User.GetInstallerId();
        var installer = await db.Installers.FirstOrDefaultAsync(i => i.Id == installerId, ct);
        if (installer is null)
        {
            return Results.Unauthorized();
        }

        var households = await LoadHouseholdsAsync(db, insights, installerId, clock.GetUtcNow(), ct);

        return Results.Ok(new InstallerOverviewDto(
            HouseholdQueries.ToDto(installer),
            households,
            households.Count(h => h.Status == HouseholdStatus.Online),
            households.Count(h => h.Status == HouseholdStatus.Fault),
            households.Count(h => h.Status == HouseholdStatus.NoData)));
    }

    private static async Task<List<InstallerHouseholdDto>> LoadHouseholdsAsync(
        AppDbContext db, InsightsService insights, Guid installerId, DateTimeOffset now, CancellationToken ct)
    {
        var today = DutchTime.LocalDate(now);

        var rows = await db.Households
            // Dubbel met het globale filter in AppDbContext, bewust: alleen eigen klanten met toestemming.
            .Where(h => h.InstallerId == installerId && h.InstallerAccessGranted)
            .OrderBy(h => h.Name)
            .Select(h => new
            {
                h.Id,
                h.Name,
                h.City,
                h.PostalCode,
                Devices = h.Devices.OrderBy(d => d.CreatedAt).ToList(),
                OpenAlerts = h.Devices.SelectMany(d => d.Alerts).Count(a => a.ResolvedAt == null),
                PvKwhToday = h.DailySummaries.Where(s => s.Date == today).Select(s => (double?)s.PvKwh).FirstOrDefault() ?? 0d
            })
            .ToListAsync(ct);

        var result = new List<InstallerHouseholdDto>(rows.Count);
        foreach (var r in rows)
        {
            var lastSeen = r.Devices.Count == 0 ? null : r.Devices.Max(d => d.LastSeenAt);
            var (performance, opportunities) = await insights.ForInstallerAsync(r.Id, today, ct);
            result.Add(new InstallerHouseholdDto(
                r.Id,
                r.Name,
                r.City,
                r.PostalCode,
                r.Devices.Select(d => HouseholdQueries.ToDto(d, now)).ToList(),
                HouseholdQueries.DetermineStatus(r.Devices.Count, r.OpenAlerts, lastSeen, now),
                r.OpenAlerts,
                Math.Round(r.PvKwhToday, 2),
                lastSeen,
                performance.Status,
                opportunities));
        }

        return result;
    }

    private static async Task<IResult> DetailAsync(
        Guid id,
        HttpContext http,
        AppDbContext db,
        HouseholdQueries queries,
        InsightsService insights,
        TimeProvider clock,
        CancellationToken ct)
    {
        var installerId = http.User.GetInstallerId();
        var household = await db.Households
            .Include(h => h.Installer)
            .FirstOrDefaultAsync(h => h.Id == id && h.InstallerId == installerId && h.InstallerAccessGranted, ct);

        if (household is null)
        {
            // Bestaat niet, is van een andere installateur of de bewoner gaf geen toestemming: altijd 404.
            return Results.NotFound();
        }

        // Inzage vastleggen; de bewoner kan dit zien in de app. Max. één regel per medewerker per 10 minuten.
        var viewerId = http.User.GetUserId();
        var viewedAt = clock.GetUtcNow();
        var recent = viewedAt.AddMinutes(-10);
        if (!await db.InstallerAccessLogs.AnyAsync(l => l.HouseholdId == household.Id && l.UserId == viewerId && l.At > recent, ct))
        {
            db.InstallerAccessLogs.Add(new InstallerAccessLog
            {
                HouseholdId = household.Id,
                InstallerId = installerId,
                UserId = viewerId,
                Action = "view_detail",
                At = viewedAt
            });
            await db.SaveChangesAsync(ct);
        }

        var now = clock.GetUtcNow();
        var today = DutchTime.LocalDate(now);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var email = await db.Users
            .Where(u => u.HouseholdId == id)
            .OrderBy(u => u.CreatedAt)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(ct);

        var devices = await queries.DevicesAsync(id, ct);
        var alerts = await queries.RecentAlertsAsync(id, 30, ct);
        var openAlerts = alerts.Count(a => a.ResolvedAt is null);
        var latest = await queries.LatestReadingsAsync(id, ct);
        var live = latest.Count == 0 ? null : SnapshotBuilder.Build(latest);
        var todaySummary = await queries.AggregateAsync(id, today, today, today, ct);
        var month = await queries.AggregateAsync(id, monthStart, today, monthStart, ct);
        var lastDays = await queries.SummariesAsync(id, today.AddDays(-13), today, ct);
        var lastSeen = devices.Count == 0 ? null : devices.Max(d => d.LastSeenAt);
        var (performance, opportunities) = await insights.ForInstallerAsync(id, today, ct);

        return Results.Ok(new InstallerHouseholdDetailDto(
            household.Id,
            household.Name,
            household.City,
            household.PostalCode,
            email,
            devices,
            HouseholdQueries.DetermineStatus(devices.Count, openAlerts, lastSeen, now),
            openAlerts,
            todaySummary.PvKwh,
            lastSeen,
            live,
            alerts,
            todaySummary,
            month,
            lastDays,
            performance,
            opportunities));
    }

    // Eén "opnieuw uitlezen" per klant per minuut: merk-API's hebben limieten (SolaX: 10 per minuut).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, DateTimeOffset> LastRefresh = new();

    /// <summary>Klant van deze installateur mét toestemming, of null (dan altijd 404).</summary>
    private static Task<Household?> FindOwnHouseholdAsync(AppDbContext db, Guid id, Guid installerId, CancellationToken ct) =>
        db.Households.FirstOrDefaultAsync(h => h.Id == id && h.InstallerId == installerId && h.InstallerAccessGranted, ct);

    private static async Task LogActionAsync(AppDbContext db, HttpContext http, Guid householdId, Guid installerId, string action, DateTimeOffset at, CancellationToken ct)
    {
        db.InstallerAccessLogs.Add(new InstallerAccessLog
        {
            HouseholdId = householdId,
            InstallerId = installerId,
            UserId = http.User.GetUserId(),
            Action = action,
            At = at
        });
        await db.SaveChangesAsync(ct);
    }

    private static async Task<IResult> RefreshAsync(
        Guid id,
        HttpContext http,
        AppDbContext db,
        PollingWorker poller,
        TimeProvider clock,
        CancellationToken ct)
    {
        var installerId = http.User.GetInstallerId();
        if (await FindOwnHouseholdAsync(db, id, installerId, ct) is null)
        {
            return Results.NotFound();
        }

        var now = clock.GetUtcNow();
        // Atomair: bij een dubbele klik wint er precies één.
        var claimed = false;
        LastRefresh.AddOrUpdate(
            id,
            _ => { claimed = true; return now; },
            (_, last) =>
            {
                if (now - last < TimeSpan.FromMinutes(1))
                {
                    return last;
                }

                claimed = true;
                return now;
            });
        if (!claimed)
        {
            return Results.Json(new InstallerActionResult(false, "Net al uitgelezen. Probeer het over een minuut opnieuw."),
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        await LogActionAsync(db, http, id, installerId, "refresh", now, ct);

        // Eigen time-out i.p.v. die van het request: ook als de browser afhaakt, worden de metingen bewaard.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var (ok, fail) = await poller.PollHouseholdAsync(id, timeout.Token);
        var message = fail == 0
            ? $"Opnieuw uitgelezen: {ok} apparaat/apparaten ok."
            : $"Opnieuw uitgelezen: {ok} ok, {fail} mislukt. Zie de foutmelding bij het apparaat.";
        return Results.Ok(new InstallerActionResult(fail == 0, message));
    }

    private static async Task<IResult> MessageAsync(
        Guid id,
        InstallerMessageRequest request,
        HttpContext http,
        AppDbContext db,
        IPushSender push,
        TimeProvider clock,
        CancellationToken ct)
    {
        var text = request.Text?.Trim() ?? string.Empty;
        if (text.Length is 0 or > 300)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["text"] = ["Schrijf een bericht van maximaal 300 tekens."] });
        }

        var installerId = http.User.GetInstallerId();
        if (await FindOwnHouseholdAsync(db, id, installerId, ct) is null)
        {
            return Results.NotFound();
        }

        // Max. 5 berichten per klant per dag, zodat een klant niet overspoeld raakt.
        var now = clock.GetUtcNow();
        var dayStart = DutchTime.StartOfLocalDayUtc(DutchTime.LocalDate(now));
        var sentToday = await db.InstallerAccessLogs.CountAsync(
            l => l.HouseholdId == id && l.Action == "message" && l.At >= dayStart, ct);
        if (sentToday >= 5)
        {
            return Results.Json(new InstallerActionResult(false, "Je hebt deze klant vandaag al 5 berichten gestuurd. Morgen kan het weer."),
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        var installer = await db.Installers.FirstAsync(i => i.Id == installerId, ct);
        await LogActionAsync(db, http, id, installerId, "message", now, ct);

        if (!push.Enabled)
        {
            return Results.Ok(new InstallerActionResult(false, "Pushmeldingen staan nog niet aan op de server."));
        }

        var sent = await push.SendToHouseholdAsync(id, $"Bericht van {installer.Name}", text, null, ct);
        return Results.Ok(sent > 0
            ? new InstallerActionResult(true, "Verstuurd. Je klant krijgt het als melding in Wattzicht.")
            : new InstallerActionResult(false, "Niet verstuurd: deze klant heeft Wattzicht nog niet met meldingen op de telefoon staan."));
    }

    /// <summary>
    /// Het maandoverzicht. Alleen tonen, niet factureren: er is nog geen betaalpartij voor bedrijven,
    /// en zolang Triple Energie de enige installateur is valt er ook niets te innen.
    ///
    /// Actief = een huishouden met een lopend abonnement waarvoor déze installateur de bron is. Een
    /// klant die zelf in de store betaalt telt dus niet mee, want dan betaalt hij al zelf; per woning
    /// betaalt precies één partij.
    /// </summary>
    private static async Task<IResult> BillingAsync(
        HttpContext http, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var installerId = http.User.GetInstallerId();
        var now = clock.GetUtcNow();

        var installer = await db.Installers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.Id == installerId, ct);
        if (installer is null) return Results.NotFound();

        var actief = await db.Subscriptions.IgnoreQueryFilters()
            .CountAsync(s => s.InstallerId == installerId
                             && s.Source == SubscriptionSource.Installer
                             && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Pending)
                             && s.StartsAt <= now
                             && (s.EndsAt == null || s.EndsAt > now), ct);

        var gratis = installer.BillingMode == InstallerBillingMode.Free;
        var bedrag = gratis ? 0m : actief * installer.PricePerHousehold;

        return Results.Ok(new InstallerBillingDto(
            BillingMode: installer.BillingMode,
            PricePerHouseholdEur: installer.PricePerHousehold,
            ActiveHouseholds: actief,
            MonthlyAmountEur: bedrag,
            Note: gratis
                ? "Je betaalt niets. Je klanten krijgen twaalf maanden Plus van jou cadeau."
                : "Dit is wat deze maand te factureren valt. Er gaat nog niets automatisch; we bellen je voordat er iets verandert."));
    }

    private static async Task<IResult> GetBrandingAsync(HttpContext http, AppDbContext db, CancellationToken ct)
    {
        var installerId = http.User.GetInstallerId();
        var installer = await db.Installers.FirstOrDefaultAsync(i => i.Id == installerId, ct);
        return installer is null ? Results.Unauthorized() : Results.Ok(HouseholdQueries.ToBrandingDto(installer));
    }

    private static async Task<IResult> UpdateBrandingAsync(
        UpdateBrandingRequest request,
        HttpContext http,
        AppDbContext db,
        CancellationToken ct)
    {
        var installerId = http.User.GetInstallerId();
        var installer = await db.Installers.FirstOrDefaultAsync(i => i.Id == installerId, ct);
        if (installer is null)
        {
            return Results.Unauthorized();
        }

        var errors = new Dictionary<string, string[]>();

        if (request.Name is not null && string.IsNullOrWhiteSpace(request.Name))
        {
            errors["name"] = ["Naam mag niet leeg zijn."];
        }

        if (request.PrimaryColor is not null && !HexColor().IsMatch(request.PrimaryColor))
        {
            errors["primaryColor"] = ["Gebruik een hex-kleur zoals #0F172A."];
        }

        if (request.AccentColor is not null && !HexColor().IsMatch(request.AccentColor))
        {
            errors["accentColor"] = ["Gebruik een hex-kleur zoals #F59E0B."];
        }

        if (request.LogoDataUrl is { Length: > 0 } logo)
        {
            if (!IsHttpsUrl(logo) && !IsImageDataUrl(logo))
            {
                errors["logoDataUrl"] = ["Logo moet een https-link of een data:image-URL zijn."];
            }
            else if (logo.Length > MaxLogoLength)
            {
                errors["logoDataUrl"] = ["Logo is te groot (maximaal ongeveer 500 kB)."];
            }
        }

        if (request.QuoteUrl is { Length: > 0 } quote && !IsHttpsUrl(quote))
        {
            errors["quoteUrl"] = ["Offertelink moet een https-link zijn."];
        }

        if (request.Email is { Length: > 0 } mail && (!mail.Contains('@') || mail.Length > 320))
        {
            errors["email"] = ["Vul een geldig e-mailadres in."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        if (request.Name is not null)
        {
            installer.Name = request.Name.Trim();
        }

        if (request.PrimaryColor is not null)
        {
            installer.PrimaryColor = request.PrimaryColor.ToUpperInvariant();
        }

        if (request.AccentColor is not null)
        {
            installer.AccentColor = request.AccentColor.ToUpperInvariant();
        }

        // Het portal stuurt het hele Branding-object terug, met logoDataUrl: null om het logo te wissen.
        // Daarom: null in de body = wissen als de client dat veld expliciet meestuurde. Omdat System.Text.Json
        // ontbrekend en null niet onderscheidt, geldt hier: null of leeg = logo weg.
        installer.LogoUrl = string.IsNullOrEmpty(request.LogoDataUrl) ? null : request.LogoDataUrl;

        if (request.QuoteUrl is not null)
        {
            installer.QuoteUrl = request.QuoteUrl.Length == 0 ? null : request.QuoteUrl.Trim();
        }

        if (request.Phone is not null)
        {
            installer.Phone = request.Phone.Length == 0 ? null : request.Phone.Trim();
        }

        if (request.Email is not null)
        {
            installer.ContactEmail = request.Email.Length == 0 ? null : request.Email.Trim().ToLowerInvariant();
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(HouseholdQueries.ToBrandingDto(installer));
    }

    private static bool IsHttpsUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static bool IsImageDataUrl(string value) =>
        value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) && value.Contains(";base64,", StringComparison.Ordinal);

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();
}
