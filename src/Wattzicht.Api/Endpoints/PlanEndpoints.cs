using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Endpoints;

/// <summary>
/// Wat Plus kost en of deze woning het heeft.
///
/// De prijzen staan hier en niet in de app. Een prijs in een app-build staat vast tot de volgende
/// release in de store, en die duurt dagen; dan staat er in de app iets anders dan de store afrekent.
/// De app toont wat hier staat en laat de store de echte prijs bevestigen bij het afrekenen.
/// </summary>
public static class PlanEndpoints
{
    public static IEndpointRouteBuilder MapPlanEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/plans", Plannen)
            .AllowAnonymous()
            .WithTags("Plus")
            .WithSummary("De abonnementen met hun prijs en store-product-id's.")
            .Produces<PlansDto>();

        app.MapGet("/homes/{id:guid}/entitlement", EntitlementAsync)
            .RequireAuthorization()
            .WithTags("Plus")
            .WithSummary("Heeft deze woning Plus, waardoor, en tot wanneer.")
            .Produces<EntitlementDto>()
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    private static IResult Plannen(EntitlementService entitlement) =>
        Results.Ok(new PlansDto(
            PlusEnabled: entitlement.PlusEnabled,
            Plans:
            [
                new PlanDto(
                    Key: "free",
                    Name: "Gratis",
                    PriceEur: 0m,
                    Period: "forever",
                    StoreProductId: null,
                    Description: "Koppelen, live energiestroom en dertig dagen historie."),
                new PlanDto(
                    Key: "plus_monthly",
                    Name: "Plus per maand",
                    PriceEur: 1.99m,
                    Period: "month",
                    StoreProductId: "nl.wattzicht.plus.monthly",
                    Description: "Maandelijks opzegbaar in de App Store of Google Play."),
                new PlanDto(
                    Key: "plus_yearly",
                    Name: "Plus per jaar",
                    PriceEur: 21.99m,
                    Period: "year",
                    StoreProductId: "nl.wattzicht.plus.yearly",
                    Description: "Acht procent goedkoper dan twaalf losse maanden."),
            ],
            TrialDays: (int)EntitlementService.TrialDuur.TotalDays));

    private static async Task<IResult> EntitlementAsync(
        Guid id,
        HttpContext http,
        EntitlementService entitlement,
        CancellationToken ct)
    {
        // Alleen over je eigen woning. Een beheerder mag overal bij; die heeft geen eigen woning en
        // moet dit kunnen opzoeken als een klant belt.
        var eigen = http.User.GetHouseholdId();
        var isBeheerder = http.User.IsInRole(nameof(UserRole.Admin));
        if (!isBeheerder && eigen != id)
        {
            return Results.Forbid();
        }

        return Results.Ok(await entitlement.GetForHomeAsync(id, ct));
    }
}
