using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Endpoints;

/// <summary>Anoniem formulier op de website voor installateurs die mee willen doen.</summary>
public static class LeadEndpoints
{
    /// <summary>Naam van de rate-limit policy voor anonieme formulieren (geregistreerd in Program.cs).</summary>
    public const string RateLimitPolicy = "leads";

    public static IEndpointRouteBuilder MapLeadEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/leads", CreateAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicy)
            .WithTags("Website")
            .WithSummary("Slaat een aanvraag van een installateur op.")
            .Produces(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        return app;
    }

    private static async Task<IResult> CreateAsync(
        LeadRequest request,
        AppDbContext db,
        TimeProvider clock,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200)
        {
            errors["name"] = ["Vul je naam in."];
        }

        if (string.IsNullOrWhiteSpace(request.Company) || request.Company.Length > 200)
        {
            errors["company"] = ["Vul je bedrijfsnaam in."];
        }

        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!email.Contains('@') || email.Length > 320)
        {
            errors["email"] = ["Vul een geldig e-mailadres in."];
        }

        if (request.Phone is { Length: > 40 })
        {
            errors["phone"] = ["Telefoonnummer is te lang."];
        }

        if (request.CustomerCount is < 0 or > 1_000_000)
        {
            errors["customerCount"] = ["Ongeldig aantal klanten."];
        }

        if (request.Source is { Length: > 100 })
        {
            errors["source"] = ["Ongeldige waarde."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var lead = new Lead
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Company = request.Company.Trim(),
            Email = email,
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            CustomerCount = request.CustomerCount,
            Source = string.IsNullOrWhiteSpace(request.Source) ? null : request.Source.Trim(),
            CreatedAt = clock.GetUtcNow()
        };

        db.Leads.Add(lead);
        await db.SaveChangesAsync(ct);

        loggerFactory.CreateLogger("Leads").LogInformation("Nieuwe lead van {Company} ({Email})", lead.Company, lead.Email);

        return Results.Created($"/leads/{lead.Id}", new { id = lead.Id });
    }
}
