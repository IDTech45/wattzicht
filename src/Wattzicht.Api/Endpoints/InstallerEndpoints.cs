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

        group.MapGet("/branding", GetBrandingAsync)
            .WithSummary("Eigen white-label instellingen: naam, logo (data-URL), kleur en offertelink.")
            .Produces<BrandingDto>();

        group.MapPut("/branding", UpdateBrandingAsync)
            .WithSummary("Past naam, logo, kleuren en offertelink aan. Alleen meegegeven velden veranderen.")
            .Produces<BrandingDto>()
            .ProducesValidationProblem();

        return app;
    }

    private static async Task<IResult> HouseholdsAsync(HttpContext http, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var installerId = http.User.GetInstallerId();
        return Results.Ok(await LoadHouseholdsAsync(db, installerId, clock.GetUtcNow(), ct));
    }

    private static async Task<IResult> OverviewAsync(HttpContext http, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var installerId = http.User.GetInstallerId();
        var installer = await db.Installers.FirstOrDefaultAsync(i => i.Id == installerId, ct);
        if (installer is null)
        {
            return Results.Unauthorized();
        }

        var households = await LoadHouseholdsAsync(db, installerId, clock.GetUtcNow(), ct);

        return Results.Ok(new InstallerOverviewDto(
            HouseholdQueries.ToDto(installer),
            households,
            households.Count(h => h.Status == HouseholdStatus.Online),
            households.Count(h => h.Status == HouseholdStatus.Fault),
            households.Count(h => h.Status == HouseholdStatus.NoData)));
    }

    private static async Task<List<InstallerHouseholdDto>> LoadHouseholdsAsync(AppDbContext db, Guid installerId, DateTimeOffset now, CancellationToken ct)
    {
        var today = DutchTime.LocalDate(now);

        var rows = await db.Households
            .Where(h => h.InstallerId == installerId)
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

        return rows
            .Select(r =>
            {
                var lastSeen = r.Devices.Count == 0 ? null : r.Devices.Max(d => d.LastSeenAt);
                return new InstallerHouseholdDto(
                    r.Id,
                    r.Name,
                    r.City,
                    r.PostalCode,
                    r.Devices.Select(d => HouseholdQueries.ToDto(d, now)).ToList(),
                    HouseholdQueries.DetermineStatus(r.Devices.Count, r.OpenAlerts, lastSeen, now),
                    r.OpenAlerts,
                    Math.Round(r.PvKwhToday, 2),
                    lastSeen);
            })
            .ToList();
    }

    private static async Task<IResult> DetailAsync(
        Guid id,
        HttpContext http,
        AppDbContext db,
        HouseholdQueries queries,
        TimeProvider clock,
        CancellationToken ct)
    {
        var installerId = http.User.GetInstallerId();
        var household = await db.Households
            .Include(h => h.Installer)
            .FirstOrDefaultAsync(h => h.Id == id && h.InstallerId == installerId, ct);

        if (household is null)
        {
            return Results.NotFound();
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
            lastDays));
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
