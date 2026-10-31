using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Connectors;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Endpoints;

/// <summary>Endpoints voor de bewoner: profiel, dashboard, apparaten, samenvattingen, meldingen en tarief.</summary>
public static class HouseholdEndpoints
{
    public static IEndpointRouteBuilder MapHouseholdEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/me", MeAsync)
            .RequireAuthorization()
            .WithTags("Me")
            .WithSummary("Ingelogde gebruiker met huishouden en white-label instellingen.")
            .Produces<MeDto>();

        var household = app.MapGroup(string.Empty)
            .RequireAuthorization(AuthPolicies.Resident)
            .WithTags("Huishouden");

        household.MapGet("/household", HouseholdAsync)
            .WithSummary("Het eigen huishouden.")
            .Produces<HouseholdDto>();

        household.MapGet("/household/installer-access", GetInstallerAccessAsync)
            .WithSummary("Mag de installateur meekijken? (toestemming van de bewoner)")
            .Produces<InstallerAccessDto>();

        household.MapPut("/household/installer-access", SetInstallerAccessAsync)
            .WithSummary("Zet meekijken door de installateur aan of uit. Uit = direct onzichtbaar in het portal.")
            .Produces<InstallerAccessDto>();

        household.MapGet("/household/access-log", AccessLogAsync)
            .WithSummary("Wanneer de installateur je gegevens bekeek (laatste 50).")
            .Produces<List<AccessLogEntryDto>>();

        household.MapGet("/installer", InstallerAsync)
            .WithSummary("White-label gegevens van de eigen installateur (naam, logo, kleuren, offertelink, contact).")
            .Produces<InstallerBrandingDto>();

        household.MapGet("/dashboard", DashboardAsync)
            .WithSummary("Alles voor het startscherm: live, vandaag, maand, jaar en meldingen.")
            .Produces<DashboardDto>();

        household.MapGet("/dashboard/live", LiveAsync)
            .WithSummary("Alleen de actuele energiestromen (voor snelle verversing).")
            .Produces<LiveSnapshotDto>();

        household.MapGet("/devices", DevicesAsync)
            .WithSummary("Gekoppelde apparaten.")
            .Produces<List<DeviceDto>>();

        household.MapGet("/connectors/enphase/authorize-url", EnphaseAuthorizeUrlAsync)
            .WithSummary("De Enphase-toestemmingspagina waar de bewoner naartoe moet (client-id blijft op de server).")
            .Produces<EnphaseAuthorizeUrlDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        household.MapPost("/devices/connect", ConnectDeviceAsync)
            .WithSummary("Koppelt een apparaat: valideert de credentials bij het merk en slaat ze versleuteld op.")
            .Produces<List<DeviceDto>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        household.MapPost("/devices", ConnectDeviceAsync)
            .WithSummary("Alias van POST /devices/connect.")
            .Produces<List<DeviceDto>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        household.MapDelete("/devices/{id:guid}", DeleteDeviceAsync)
            .WithSummary("Ontkoppelt een apparaat; metingen en meldingen gaan mee weg.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        household.MapGet("/summaries", SummariesAsync)
            .WithSummary("Dagsamenvattingen tussen twee datums (standaard: afgelopen 30 dagen).")
            .Produces<List<DailySummaryDto>>();

        household.MapGet("/dashboard/summaries", SummariesAsync)
            .WithSummary("Alias van GET /summaries.")
            .Produces<List<DailySummaryDto>>();

        household.MapGet("/alerts", AlertsAsync)
            .WithSummary("Meldingen; standaard alleen open meldingen, met ?all=true ook opgeloste.")
            .Produces<List<AlertDto>>();

        household.MapGet("/dashboard/alerts", AlertsAsync)
            .WithSummary("Alias van GET /alerts.")
            .Produces<List<AlertDto>>();

        household.MapGet("/tariff", GetTariffAsync)
            .WithSummary("Huidig tarief.")
            .Produces<TariffDto>();

        household.MapPost("/tariff", SetTariffAsync)
            .WithSummary("Zet een (nieuw) tarief vanaf een datum (standaard vandaag) en herberekent de besparing vanaf die dag.")
            .Produces<TariffDto>()
            .ProducesValidationProblem();

        household.MapPut("/tariff", SetTariffAsync)
            .WithSummary("Alias van POST /tariff.")
            .Produces<TariffDto>()
            .ProducesValidationProblem();

        return app;
    }

    private static async Task<IResult> HouseholdAsync(HttpContext http, AppDbContext db, CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var household = await db.Households
            .Include(h => h.Installer)
            .FirstOrDefaultAsync(h => h.Id == householdId, ct);

        return household is null ? Results.NotFound() : Results.Ok(HouseholdQueries.ToDto(household));
    }

    private static async Task<IResult> GetInstallerAccessAsync(HttpContext http, AppDbContext db, CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var household = await db.Households
            .Include(h => h.Installer)
            .FirstOrDefaultAsync(h => h.Id == householdId, ct);

        return household is null
            ? Results.NotFound()
            : Results.Ok(new InstallerAccessDto(household.InstallerAccessGranted, household.InstallerAccessChangedAt, household.Installer.Name));
    }

    private static async Task<IResult> SetInstallerAccessAsync(
        UpdateInstallerAccessRequest request,
        HttpContext http,
        AppDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var household = await db.Households
            .Include(h => h.Installer)
            .FirstOrDefaultAsync(h => h.Id == householdId, ct);

        if (household is null)
        {
            return Results.NotFound();
        }

        if (household.InstallerAccessGranted != request.Granted)
        {
            household.InstallerAccessGranted = request.Granted;
            household.InstallerAccessChangedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }

        return Results.Ok(new InstallerAccessDto(household.InstallerAccessGranted, household.InstallerAccessChangedAt, household.Installer.Name));
    }

    private static async Task<IResult> AccessLogAsync(HttpContext http, AppDbContext db, CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var rows = await db.InstallerAccessLogs
            .Where(l => l.HouseholdId == householdId)
            .OrderByDescending(l => l.At)
            .Take(50)
            .Select(l => new
            {
                l.At,
                l.Action,
                Who = db.Users.Where(u => u.Id == l.UserId).Select(u => u.Email).FirstOrDefault()
            })
            .ToListAsync(ct);

        return Results.Ok(rows.Select(r => new AccessLogEntryDto(r.At, r.Action, r.Who)).ToList());
    }

    private static async Task<IResult> InstallerAsync(HttpContext http, AppDbContext db, CancellationToken ct)
    {
        var installerId = http.User.GetInstallerId();
        var installer = await db.Installers.FirstOrDefaultAsync(i => i.Id == installerId, ct);
        return installer is null ? Results.NotFound() : Results.Ok(HouseholdQueries.ToDto(installer));
    }

    private static async Task<IResult> LiveAsync(HttpContext http, HouseholdQueries queries, CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        return Results.Ok(await queries.LiveAsync(householdId, ct));
    }

    private static async Task<IResult> MeAsync(HttpContext http, AppDbContext db, CancellationToken ct)
    {
        var userId = http.User.GetUserId();
        var user = await db.Users
            .Include(u => u.Installer)
            .Include(u => u.Household!)
                .ThenInclude(h => h.Installer)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(new MeDto(
            user.Id,
            user.Email,
            user.Role,
            user.Household is null ? null : HouseholdQueries.ToDto(user.Household),
            HouseholdQueries.ToDto(user.Installer)));
    }

    private static async Task<IResult> DashboardAsync(HttpContext http, HouseholdQueries queries, CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        return Results.Ok(await queries.DashboardAsync(householdId, ct));
    }

    private static async Task<IResult> DevicesAsync(HttpContext http, HouseholdQueries queries, CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        return Results.Ok(await queries.DevicesAsync(householdId, ct));
    }

    /// <summary>
    /// Terugkeeradressen die wij voor Enphase accepteren. Een open lijst zou een omleidingsgat zijn: wie
    /// zijn eigen adres mee mag geven, kan de toestemmingscode van een klant naar zichzelf laten sturen.
    /// Deze adressen moeten ook in de Enphase developer-app staan, anders weigert Enphase ze zelf al.
    /// </summary>
    private static readonly string[] EnphaseRedirects =
    [
        "https://wattzicht.laadcash.nl/oauth/enphase",
        "https://wattzicht.nl/oauth/enphase",
        "https://www.wattzicht.nl/oauth/enphase",
        "http://localhost:5173/oauth/enphase",
    ];

    private static Task<IResult> EnphaseAuthorizeUrlAsync(
        [AsParameters] EnphaseAuthorizeUrlQuery query,
        EnphaseOptions enphase)
    {
        if (string.IsNullOrWhiteSpace(enphase.ClientId))
        {
            return Task.FromResult(Results.Problem(
                title: "Enphase is op de server nog niet ingesteld.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        var redirectUri = string.IsNullOrWhiteSpace(query.RedirectUri) ? EnphaseRedirects[0] : query.RedirectUri.Trim();
        if (!EnphaseRedirects.Contains(redirectUri))
        {
            return Task.FromResult(Results.Problem(
                title: "Dit terugkeeradres staat niet op onze lijst.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        var url = "https://api.enphaseenergy.com/oauth/authorize?response_type=code"
                  + "&client_id=" + Uri.EscapeDataString(enphase.ClientId)
                  + "&redirect_uri=" + Uri.EscapeDataString(redirectUri);

        return Task.FromResult(Results.Ok(new EnphaseAuthorizeUrlDto(url, redirectUri)));
    }

    private static async Task<IResult> ConnectDeviceAsync(
        ConnectDeviceRequest request,
        HttpContext http,
        AppDbContext db,
        ConnectorRegistry registry,
        CredentialProtector protector,
        TimeProvider clock,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var logger = loggerFactory.CreateLogger("Devices");

        var connector = registry.Get(request.Brand);
        if (connector is null)
        {
            return Results.Problem(
                title: $"{request.Brand} kun je nog niet koppelen.",
                detail: $"Ondersteund: {string.Join(", ", registry.SupportedBrands)}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var credentials = new Dictionary<string, string>(
            request.Credentials ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);

        // De app stuurt "ip" voor lokale apparaten; de connectors lezen "host".
        if (!credentials.ContainsKey("host") && credentials.TryGetValue("ip", out var ip))
        {
            credentials["host"] = ip;
        }

        ConnectResult result;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            result = await connector.ConnectAsync(new ConnectRequest(request.Brand, request.Type, credentials), timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Results.Problem(title: "Het koppelen duurde te lang. Probeer het opnieuw.", statusCode: StatusCodes.Status400BadRequest);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Koppelen van {Brand} mislukt", request.Brand);
            return Results.Problem(title: "Koppelen mislukt door een onverwachte fout.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!result.Success)
        {
            return Results.Problem(title: result.Error ?? "Koppelen mislukt.", statusCode: StatusCodes.Status400BadRequest);
        }

        var now = clock.GetUtcNow();
        var existing = await db.Devices
            .Where(d => d.HouseholdId == householdId && d.Brand == request.Brand)
            .Select(d => new { d.ExternalId, d.Type })
            .ToListAsync(ct);

        var created = new List<Device>();
        var single = result.Devices.Count == 1;

        foreach (var found in result.Devices)
        {
            if (existing.Any(e => e.ExternalId == found.ExternalId && e.Type == found.Type))
            {
                continue; // al gekoppeld
            }

            var merged = new Dictionary<string, string>(credentials, StringComparer.OrdinalIgnoreCase);
            if (found.ExtraCredentials is not null)
            {
                foreach (var pair in found.ExtraCredentials)
                {
                    merged[pair.Key] = pair.Value;
                }
            }

            var device = new Device
            {
                Id = Guid.NewGuid(),
                HouseholdId = householdId,
                Type = found.Type,
                Brand = request.Brand,
                Name = single && !string.IsNullOrWhiteSpace(request.Name) ? request.Name.Trim() : found.Name,
                ExternalId = found.ExternalId,
                EncryptedCredentials = protector.Protect(merged),
                CreatedAt = now
            };

            db.Devices.Add(device);
            created.Add(device);
        }

        if (created.Count == 0)
        {
            return Results.Problem(title: "Deze apparaten zijn al gekoppeld.", statusCode: StatusCodes.Status400BadRequest);
        }

        await db.SaveChangesAsync(ct);

        var dtos = created.Select(d => HouseholdQueries.ToDto(d, now)).ToList();
        return Results.Created("/devices", dtos);
    }

    private static async Task<IResult> DeleteDeviceAsync(Guid id, HttpContext http, AppDbContext db, CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == id && d.HouseholdId == householdId, ct);
        if (device is null)
        {
            return Results.NotFound();
        }

        db.Devices.Remove(device);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SummariesAsync(
        HttpContext http,
        HouseholdQueries queries,
        TimeProvider clock,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var today = DutchTime.LocalDate(clock.GetUtcNow());
        var end = to ?? today;
        var start = from ?? end.AddDays(-29);

        if (start > end)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["from"] = ["'from' moet voor 'to' liggen."] });
        }

        if (end.DayNumber - start.DayNumber > 366)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["to"] = ["Maximaal 1 jaar per verzoek."] });
        }

        return Results.Ok(await queries.SummariesAsync(householdId, start, end, ct));
    }

    private static async Task<IResult> AlertsAsync(HttpContext http, AppDbContext db, CancellationToken ct, bool all = false)
    {
        var householdId = http.User.GetHouseholdId()!.Value;

        var query = db.Alerts.Where(a => a.Device.HouseholdId == householdId);
        if (!all)
        {
            query = query.Where(a => a.ResolvedAt == null);
        }

        var alerts = await query
            .OrderByDescending(a => a.Since)
            .Take(200)
            .Select(a => new AlertDto(a.Id, a.DeviceId, a.Device.Name, a.Type, a.Message, a.Since, a.ResolvedAt))
            .ToListAsync(ct);

        return Results.Ok(alerts);
    }

    private static async Task<IResult> GetTariffAsync(HttpContext http, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var today = DutchTime.LocalDate(clock.GetUtcNow());

        var tariff = await db.Tariffs
            .Where(t => t.HouseholdId == householdId && t.ValidFrom <= today)
            .OrderByDescending(t => t.ValidFrom)
            .FirstOrDefaultAsync(ct);

        if (tariff is null)
        {
            var d = TariffRates.Default;
            return Results.Ok(new TariffDto(d.ImportPricePerKwh, d.ExportPricePerKwh, false, null, d.PublicChargingPricePerKwh, d.FeedInCostPerKwh));
        }

        return Results.Ok(ToDto(tariff));
    }

    private static async Task<IResult> SetTariffAsync(
        TariffDto request,
        HttpContext http,
        AppDbContext db,
        SummaryService summaries,
        TimeProvider clock,
        CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var validFrom = request.ValidFrom ?? DutchTime.LocalDate(clock.GetUtcNow());

        var errors = new Dictionary<string, string[]>();
        if (request.ImportTariffEur is < 0 or > 2)
        {
            errors["importTariffEur"] = ["Leveringstarief moet tussen 0 en 2 euro per kWh liggen."];
        }

        if (request.ExportTariffEur is < 0 or > 2)
        {
            errors["exportTariffEur"] = ["Terugleververgoeding moet tussen 0 en 2 euro per kWh liggen."];
        }

        if (request.FeedInCostEur is < 0 or > 1)
        {
            errors["feedInCostEur"] = ["Terugleverkosten moeten tussen 0 en 1 euro per kWh liggen."];
        }

        if (request.PublicChargingTariffEur is < 0 or > 3)
        {
            errors["publicChargingTariffEur"] = ["Publiek laadtarief moet tussen 0 en 3 euro per kWh liggen."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var tariff = await db.Tariffs
            .FirstOrDefaultAsync(t => t.HouseholdId == householdId && t.ValidFrom == validFrom, ct);

        if (tariff is null)
        {
            // Terugleverkosten niet meegestuurd? Neem ze over van het vorige tarief (oudere apps kennen het veld niet).
            var previousFeedIn = await db.Tariffs
                .Where(t => t.HouseholdId == householdId && t.ValidFrom < validFrom)
                .OrderByDescending(t => t.ValidFrom)
                .Select(t => (decimal?)t.FeedInCostPerKwh)
                .FirstOrDefaultAsync(ct);

            tariff = new Tariff
            {
                Id = Guid.NewGuid(),
                HouseholdId = householdId,
                ValidFrom = validFrom,
                FeedInCostPerKwh = previousFeedIn ?? 0m
            };
            db.Tariffs.Add(tariff);
        }

        if (request.FeedInCostEur is { } feedIn)
        {
            tariff.FeedInCostPerKwh = feedIn;
        }

        tariff.ImportPricePerKwh = request.ImportTariffEur;
        tariff.ExportPricePerKwh = request.ExportTariffEur;
        tariff.IsDynamic = request.IsDynamic;
        tariff.PublicChargingPricePerKwh = request.PublicChargingTariffEur is { } pub && pub > 0
            ? pub
            : SavingsCalculator.DefaultPublicChargingPrice;

        // Tarief en herberekende besparing samen opslaan, of allebei niet.
        // Eerst opslaan binnen de transactie: een nieuw tarief komt anders niet terug uit de query in RecalculateSavingsAsync.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.SaveChangesAsync(ct);
            await summaries.RecalculateSavingsAsync(householdId, validFrom, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });

        return Results.Ok(ToDto(tariff));
    }

    private static TariffDto ToDto(Tariff t) => new(
        t.ImportPricePerKwh,
        t.ExportPricePerKwh,
        t.IsDynamic,
        t.ValidFrom,
        t.PublicChargingPricePerKwh,
        t.FeedInCostPerKwh);
}
