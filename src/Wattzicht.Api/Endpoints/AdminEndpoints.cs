using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Endpoints;

/// <summary>
/// Beheerpaneel van Wattzicht zelf: de hele keten installateur → huishouden → apparaat.
///
/// Een installateur ziet alleen zijn eigen klanten, en dat hoort zo. De beheerder moet er juist
/// dwars doorheen kunnen kijken: wie heeft welke klanten, leveren hun apparaten nog data, en wat
/// levert dat per maand op. Daarom staat bij elke vraag hier <c>IgnoreQueryFilters</c>: de
/// tenantscheiding uit <see cref="AppDbContext"/> blijft overal elders gewoon staan, en je ziet per
/// regel dat hier bewust buiten die grens gekeken wordt.
/// </summary>
public static class AdminEndpoints
{
    /// <summary>Hoeveel regels het toegangslogboek standaard teruggeeft.</summary>
    private const int LogLimiet = 100;

    /// <summary>Een apparaat dat een etmaal niets leverde noemen we stil; de pollworker draait elke 5 minuten.</summary>
    private static readonly TimeSpan StilNa = TimeSpan.FromHours(24);

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin")
            .RequireAuthorization(AuthPolicies.Admin)
            .WithTags("Beheer");

        group.MapGet("/overview", OverviewAsync)
            .WithSummary("Cijfers over alle installateurs samen, plus per installateur.")
            .Produces<AdminOverviewDto>();

        group.MapGet("/installers", InstallersAsync)
            .WithSummary("Alle installateurs met hun aantallen klanten, apparaten en omzet.")
            .Produces<List<AdminInstallerDto>>();

        group.MapGet("/households", HouseholdsAsync)
            .WithSummary("Alle huishoudens over alle installateurs heen; filter op installateur of zoekterm.")
            .Produces<List<AdminHouseholdDto>>();

        group.MapGet("/access-log", AccessLogAsync)
            .WithSummary("Wie keek er bij wie mee: de laatste handelingen van installateurs bij klanten.")
            .Produces<List<AdminAccessLogDto>>();

        return app;
    }

    private static async Task<IResult> OverviewAsync(AppDbContext db, IConfiguration config, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var installateurs = await LaadInstallateursAsync(db, config, now, ct);

        var huishoudens = await db.Households.IgnoreQueryFilters().CountAsync(ct);
        var metToegang = await db.Households.IgnoreQueryFilters().CountAsync(h => h.InstallerAccessGranted, ct);
        var apparaten = await db.Devices.IgnoreQueryFilters().CountAsync(ct);
        var metProbleem = await db.Devices.IgnoreQueryFilters().CountAsync(d => d.LastError != null, ct);
        var stil = await db.Devices.IgnoreQueryFilters()
            .CountAsync(d => d.LastSeenAt == null || d.LastSeenAt < now - StilNa, ct);
        var meldingen = await db.Alerts.IgnoreQueryFilters().CountAsync(a => a.ResolvedAt == null, ct);

        var tarief = PrijsPerHuishouden(config);
        return Results.Ok(new AdminOverviewDto(
            installateurs.Count, huishoudens, metToegang, apparaten, metProbleem, stil, meldingen,
            tarief, tarief * huishoudens, installateurs));
    }

    private static async Task<IResult> InstallersAsync(AppDbContext db, IConfiguration config, TimeProvider clock, CancellationToken ct) =>
        Results.Ok(await LaadInstallateursAsync(db, config, clock.GetUtcNow(), ct));

    private static async Task<List<AdminInstallerDto>> LaadInstallateursAsync(
        AppDbContext db, IConfiguration config, DateTimeOffset now, CancellationToken ct)
    {
        var tarief = PrijsPerHuishouden(config);

        // Eén vraag voor alle installateurs: met een telling per installateur erbij blijft dit ook
        // bij honderden installateurs één rondje naar de database.
        var rijen = await db.Installers.IgnoreQueryFilters()
            .OrderBy(i => i.Name)
            .Select(i => new
            {
                i.Id,
                i.Name,
                i.Slug,
                i.CreatedAt,
                Huishoudens = i.Households.Count,
                MetToegang = i.Households.Count(h => h.InstallerAccessGranted),
                Apparaten = i.Households.SelectMany(h => h.Devices).Count(),
                MetProbleem = i.Households.SelectMany(h => h.Devices).Count(d => d.LastError != null),
                LaatsteData = i.Households.SelectMany(h => h.Devices).Max(d => (DateTimeOffset?)d.LastSeenAt)
            })
            .ToListAsync(ct);

        return rijen.Select(r => new AdminInstallerDto(
            r.Id, r.Name, r.Slug, r.Huishoudens, r.MetToegang, r.Apparaten, r.MetProbleem,
            r.LaatsteData, tarief * r.Huishoudens, r.CreatedAt)).ToList();
    }

    private static async Task<IResult> HouseholdsAsync(
        AppDbContext db, TimeProvider clock, CancellationToken ct, Guid? installerId = null, string? search = null)
    {
        var query = db.Households.IgnoreQueryFilters().AsQueryable();
        if (installerId is { } id && id != Guid.Empty)
        {
            query = query.Where(h => h.InstallerId == id);
        }

        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            // Zoeken op naam of plaats; hoofdletterongevoelig, want niemand typt een plaatsnaam precies over.
            query = query.Where(h =>
                EF.Functions.ILike(h.Name, $"%{term}%")
                || (h.City != null && EF.Functions.ILike(h.City, $"%{term}%")));
        }

        var rijen = await query
            .OrderBy(h => h.Installer.Name).ThenBy(h => h.Name)
            .Select(h => new AdminHouseholdDto(
                h.Id,
                h.Name,
                h.City,
                h.InstallerId,
                h.Installer.Name,
                h.Devices.Count,
                h.Devices.Count(d => d.LastError != null),
                h.Devices.SelectMany(d => d.Alerts).Count(a => a.ResolvedAt == null),
                h.Devices.Max(d => (DateTimeOffset?)d.LastSeenAt),
                h.InstallerAccessGranted,
                h.CreatedAt))
            .ToListAsync(ct);

        return Results.Ok(rijen);
    }

    private static async Task<IResult> AccessLogAsync(AppDbContext db, CancellationToken ct, int limit = LogLimiet)
    {
        var aantal = Math.Clamp(limit, 1, 500);

        // De namen erbij: een logboek met alleen id's leest niemand, en dit is juist het overzicht
        // waarmee je een klant kunt uitleggen wie er bij zijn gegevens is geweest.
        var rijen = await db.InstallerAccessLogs.IgnoreQueryFilters()
            .OrderByDescending(l => l.At)
            .Take(aantal)
            .Select(l => new
            {
                l.At,
                l.Action,
                HouseholdName = l.Household.Name,
                InstallerName = db.Installers.IgnoreQueryFilters()
                    .Where(i => i.Id == l.InstallerId).Select(i => i.Name).FirstOrDefault()
            })
            .ToListAsync(ct);

        return Results.Ok(rijen
            .Select(r => new AdminAccessLogDto(r.At, r.InstallerName ?? "onbekend", r.HouseholdName, r.Action))
            .ToList());
    }

    /// <summary>
    /// Het maandtarief per huishouden, instelbaar via <c>Billing:PricePerHouseholdEur</c>.
    ///
    /// Staat op 2 euro, want dat is wat een installateur zijn klant rekent. Het staat hier in de
    /// configuratie en niet hard in de code: dit is een prijs die gaat veranderen, en dan wil je
    /// geen nieuwe versie hoeven uitrollen om je eigen omzetoverzicht te laten kloppen.
    /// </summary>
    private static decimal PrijsPerHuishouden(IConfiguration config) =>
        decimal.TryParse(config["Billing:PricePerHouseholdEur"], System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var prijs) && prijs >= 0
            ? prijs
            : 2m;
}
