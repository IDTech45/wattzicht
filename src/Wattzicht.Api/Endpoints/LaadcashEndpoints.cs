using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Endpoints;

/// <summary>
/// De LaadCash-promotie en de terugkoppeling vanuit LaadCash.
///
/// Wattzicht en LaadCash zijn twee producten van hetzelfde bedrijf, en elke Wattzicht-gebruiker met
/// een laadpaal hoort LaadCash-klant te zijn. Daarom mag dit nadrukkelijker dan een gewone
/// aanbieding en staat het niet achter Plus: het geldt voor iedereen met een laadpaal.
/// </summary>
public static class LaadcashEndpoints
{
    public static IEndpointRouteBuilder MapLaadcashEndpoints(this IEndpointRouteBuilder app)
    {
        var groep = app.MapGroup("/laadcash").WithTags("LaadCash");

        groep.MapGet("/promo", PromoAsync)
            .RequireAuthorization(AuthPolicies.Resident)
            .WithSummary("Wat de app over LaadCash moet tonen: popup, tegel of de echte opbrengst.")
            .Produces<LaadcashPromoDto>();

        groep.MapPost("/promo/action", ActieAsync)
            .RequireAuthorization(AuthPolicies.Resident)
            .WithSummary("Legt vast dat er iets getoond, aangeklikt of weggeklikt is.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        groep.MapPost("/linked", GekoppeldAsync)
            .WithSummary("LaadCash meldt dat deze woning is aangemeld. Stopt alle promotie.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> PromoAsync(
        HttpContext http, LaadcashPromotie promotie, CancellationToken ct)
    {
        var homeId = http.User.GetHouseholdId()!.Value;
        return Results.Ok(await promotie.VoorWoningAsync(homeId, ct));
    }

    private static async Task<IResult> ActieAsync(
        PromoActionRequest request, HttpContext http, LaadcashPromotie promotie, CancellationToken ct)
    {
        var channel = (request.Channel ?? "").Trim().ToLowerInvariant();
        var action = (request.Action ?? "").Trim().ToLowerInvariant();

        if (!PromoChannel.Alle.Contains(channel) || !PromoAction.Alle.Contains(action))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["channel"] = [$"Kanaal moet een van {string.Join(", ", PromoChannel.Alle)} zijn."],
                ["action"] = [$"Actie moet een van {string.Join(", ", PromoAction.Alle)} zijn."],
            });
        }

        var homeId = http.User.GetHouseholdId()!.Value;
        await promotie.NoteerAsync(homeId, channel, action, ct);
        return Results.NoContent();
    }

    /// <summary>
    /// LaadCash meldt dat een woning zich heeft aangemeld.
    ///
    /// Met een gedeeld geheim en niet met een gebruikerstoken: dit komt van server naar server, er zit
    /// geen mens tussen. Zonder dat geheim in de configuratie staat dit endpoint dicht, want anders
    /// kan iedereen de promotie voor een willekeurige woning uitzetten.
    /// </summary>
    private static async Task<IResult> GekoppeldAsync(
        LaadcashLinkedRequest request,
        HttpContext http,
        AppDbContext db,
        IConfiguration config,
        TimeProvider clock,
        CancellationToken ct)
    {
        var geheim = config["Laadcash:SharedSecret"];
        if (string.IsNullOrWhiteSpace(geheim))
        {
            return Results.Problem(
                title: "De koppeling met LaadCash is op deze server niet ingesteld.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var meegestuurd = http.Request.Headers["X-Laadcash-Secret"].ToString();
        if (!CryptographicEquals(meegestuurd, geheim))
        {
            return Results.Unauthorized();
        }

        var woning = await db.Households.IgnoreQueryFilters()
            .FirstOrDefaultAsync(h => h.Id == request.HomeId, ct);
        if (woning is null)
        {
            // Geen 404: dan kan iemand met dit endpoint uitvogelen welke woning-id's bestaan.
            return Results.NoContent();
        }

        woning.LaadcashLinkedAt ??= request.LinkedAt ?? clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>Vergelijken zonder dat de tijdsduur verraadt hoeveel tekens er klopten.</summary>
    private static bool CryptographicEquals(string a, string b) =>
        a.Length == b.Length
        && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(b));
}
