using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Connectors;
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

        group.MapGet("/leads", LeadsAsync)
            .WithSummary("Demo-aanvragen van de website, nieuwste eerst.")
            .Produces<List<AdminLeadDto>>();

        group.MapGet("/access-log", AccessLogAsync)
            .WithSummary("Wie keek er bij wie mee: de laatste handelingen van installateurs bij klanten.")
            .Produces<List<AdminAccessLogDto>>();

        return app;
    }

    private static async Task<IResult> OverviewAsync(
        AppDbContext db, IConfiguration config, ConnectorRegistry registry, TimeProvider clock, CancellationToken ct)
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

        // De trechter. Aanmelden is niet hetzelfde als werken: een huishouden zonder apparaat heeft een
        // account en verder niets, en dat is precies de groep waar iemand voor gebeld moet worden.
        var zonderApparaat = await db.Households.IgnoreQueryFilters().CountAsync(h => !h.Devices.Any(), ct);
        var metDataVandaag = await db.Households.IgnoreQueryFilters()
            .CountAsync(h => h.Devices.Any(d => d.LastSeenAt != null && d.LastSeenAt > now - StilNa), ct);
        var nieuw7 = await db.Households.IgnoreQueryFilters().CountAsync(h => h.CreatedAt > now.AddDays(-7), ct);
        var nieuw30 = await db.Households.IgnoreQueryFilters().CountAsync(h => h.CreatedAt > now.AddDays(-30), ct);
        var leads = await db.Leads.IgnoreQueryFilters().CountAsync(ct);

        var merken = await MerkenAsync(db, registry, ct);

        var tarief = Billing.PrijsUitConfig(config);
        var gratis = Billing.GratisUitConfig(config);

        return Results.Ok(new AdminOverviewDto(
            installateurs.Count, huishoudens, metToegang, apparaten, metProbleem, stil, meldingen,
            tarief,
            // Niet huishoudens maal tarief: de eerste vijftig per installateur zijn gratis en een klant
            // telt pas als hij een apparaat gekoppeld heeft. Anders staat er omzet op het dashboard die
            // niemand kan factureren.
            installateurs.Sum(i => i.MonthlyRevenueEur),
            installateurs,
            gratis,
            installateurs.Sum(i => i.ActiveHouseholds),
            installateurs.Sum(i => i.BillableHouseholds),
            zonderApparaat,
            metDataVandaag,
            nieuw7,
            nieuw30,
            leads,
            merken));
    }

    /// <summary>
    /// Welke merken hangen er echt bij klanten. Dit bepaalt welke koppeling als volgende gebouwd moet
    /// worden: een merk met apparaten die allemaal falen is dringender dan een merk dat nog nergens hangt.
    /// </summary>
    private static async Task<List<AdminBrandDto>> MerkenAsync(
        AppDbContext db, ConnectorRegistry registry, CancellationToken ct)
    {
        var rijen = await db.Devices.IgnoreQueryFilters()
            .GroupBy(d => d.Brand)
            .Select(g => new
            {
                Brand = g.Key,
                Devices = g.Count(),
                WithProblem = g.Count(d => d.LastError != null),
                Households = g.Select(d => d.HouseholdId).Distinct().Count(),
            })
            .ToListAsync(ct);

        return rijen
            .OrderByDescending(r => r.Devices)
            .Select(r => new AdminBrandDto(
                r.Brand.ToString(), r.Devices, r.WithProblem, r.Households, registry.IsSupported(r.Brand)))
            .ToList();
    }

    private static async Task<IResult> LeadsAsync(AppDbContext db, CancellationToken ct)
    {
        var rijen = await db.Leads.IgnoreQueryFilters()
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new
            {
                l.Id, l.Name, l.Company, l.Email, l.Phone, l.CustomerCount, l.Source, l.CreatedAt,
                // Is dit al een klant? Dan hoeft er niet meer gebeld te worden voor een demo.
                IsCustomer = db.Users.IgnoreQueryFilters().Any(u => u.Email == l.Email),
            })
            .ToListAsync(ct);

        return Results.Ok(rijen
            .Select(r => new AdminLeadDto(
                r.Id, r.Name, r.Company, r.Email, r.Phone, r.CustomerCount, r.Source, r.CreatedAt, r.IsCustomer))
            .ToList());
    }

    private static async Task<IResult> InstallersAsync(AppDbContext db, IConfiguration config, TimeProvider clock, CancellationToken ct) =>
        Results.Ok(await LaadInstallateursAsync(db, config, clock.GetUtcNow(), ct));

    private static async Task<List<AdminInstallerDto>> LaadInstallateursAsync(
        AppDbContext db, IConfiguration config, DateTimeOffset now, CancellationToken ct)
    {
        var tarief = Billing.PrijsUitConfig(config);

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
                LaatsteData = i.Households.SelectMany(h => h.Devices).Max(d => (DateTimeOffset?)d.LastSeenAt),
                // Actief = minstens een gekoppeld apparaat. Zo staat het ook op de installateurspagina.
                Actief = i.Households.Count(h => h.Devices.Any()),
                ZonderApparaat = i.Households.Count(h => !h.Devices.Any()),
                Nieuw30 = i.Households.Count(h => h.CreatedAt > now.AddDays(-30))
            })
            .ToListAsync(ct);

        var gratis = Billing.GratisUitConfig(config);

        return rijen.Select(r =>
        {
            var facturabel = Billing.Facturabel(r.Actief, gratis);
            return new AdminInstallerDto(
                r.Id, r.Name, r.Slug, r.Huishoudens, r.MetToegang, r.Apparaten, r.MetProbleem,
                r.LaatsteData, Billing.PerMaand(r.Actief, gratis, tarief), r.CreatedAt,
                r.Actief, facturabel, r.ZonderApparaat, r.Nieuw30);
        }).ToList();
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

}
