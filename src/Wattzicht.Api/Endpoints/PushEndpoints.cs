using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Endpoints;

/// <summary>
/// Pushmeldingen: de app meldt hier het toestel aan, en de bewoner kiest welke meldingen hij wil.
/// Het token verandert vanzelf (herinstallatie, app-gegevens gewist), dus de app stuurt het bij elke start opnieuw.
/// </summary>
public static class PushEndpoints
{
    public static IEndpointRouteBuilder MapPushEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/push")
            .RequireAuthorization(AuthPolicies.Resident)
            .WithTags("Pushmeldingen");

        group.MapPost("/devices", RegisterAsync)
            .WithSummary("Dit toestel aanmelden of verversen.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        group.MapDelete("/devices", UnregisterAsync)
            .WithSummary("Dit toestel afmelden, bijvoorbeeld bij uitloggen.")
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/settings", GetSettingsAsync)
            .WithSummary("Welke meldingen de bewoner wil krijgen.")
            .Produces<NotificationSettingsDto>();

        group.MapPut("/settings", SetSettingsAsync)
            .WithSummary("Meldingen aan- of uitzetten.")
            .Produces<NotificationSettingsDto>();

        return app;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterPushDeviceRequest request,
        HttpContext http,
        AppDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var token = request.Token?.Trim() ?? string.Empty;
        if (token.Length is 0 or > 4096)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["token"] = ["Token ontbreekt of is ongeldig."] });
        }

        var platform = request.Platform?.Trim().ToLowerInvariant() is "ios" ? "ios" : "android";
        var now = clock.GetUtcNow();

        // Bewust zonder tenantfilter: hetzelfde toestel kan van huishouden wisselen (tweedehands telefoon,
        // ander account). Dan hoort het token bij het nieuwe huishouden, anders krijgt de vorige bewoner
        // diens meldingen. We lezen of tonen niets van die andere rij; we zetten hem alleen over.
        var existing = await db.PushDevices.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Token == token, ct);
        if (existing is null)
        {
            db.PushDevices.Add(new PushDevice
            {
                Id = Guid.NewGuid(),
                HouseholdId = householdId,
                Token = token,
                Platform = platform,
                CreatedAt = now,
                LastSeenAt = now
            });
        }
        else
        {
            existing.HouseholdId = householdId;
            existing.Platform = platform;
            existing.LastSeenAt = now;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Twee gelijktijdige aanmeldingen met hetzelfde token: de andere won, dat is prima.
        }

        return Results.NoContent();
    }

    private static async Task<IResult> UnregisterAsync(string token, HttpContext http, AppDbContext db, CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var rows = await db.PushDevices
            .Where(d => d.HouseholdId == householdId && d.Token == token.Trim())
            .ToListAsync(ct);

        if (rows.Count > 0)
        {
            db.PushDevices.RemoveRange(rows);
            await db.SaveChangesAsync(ct);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> GetSettingsAsync(HttpContext http, AppDbContext db, CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var household = await db.Households.FirstOrDefaultAsync(h => h.Id == householdId, ct);
        return household is null
            ? Results.NotFound()
            : Results.Ok(new NotificationSettingsDto(household.NotifyAlerts, household.NotifySurplus));
    }

    private static async Task<IResult> SetSettingsAsync(
        NotificationSettingsDto request,
        HttpContext http,
        AppDbContext db,
        CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var household = await db.Households.FirstOrDefaultAsync(h => h.Id == householdId, ct);
        if (household is null)
        {
            return Results.NotFound();
        }

        household.NotifyAlerts = request.Alerts;
        household.NotifySurplus = request.Surplus;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new NotificationSettingsDto(household.NotifyAlerts, household.NotifySurplus));
    }
}
