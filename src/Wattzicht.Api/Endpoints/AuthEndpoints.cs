using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Endpoints;

/// <summary>Registreren, inloggen en token vernieuwen.</summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/register", RegisterAsync)
            .WithSummary("Maakt een huishouden met eerste gebruiker aan en logt direct in.")
            .Produces<AuthResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);

        group.MapPost("/register-installer", RegisterInstallerAsync)
            .WithSummary("Meldt een installateur aan: bedrijf plus eerste gebruiker, in afwachting van goedkeuring.")
            .Produces<AuthResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);

        group.MapPost("/login", LoginAsync)
            .WithSummary("Logt in met e-mail en wachtwoord.")
            .Produces<AuthResponse>()
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/refresh", RefreshAsync)
            .RequireAuthorization()
            .WithSummary("Geeft een nieuw token op basis van een nog geldig token.")
            .Produces<AuthResponse>();

        return app;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        AppDbContext db,
        PasswordHasher hasher,
        TokenService tokens,
        TimeProvider clock,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;

        if (!email.Contains('@') || email.Length > 320)
        {
            errors["email"] = ["Vul een geldig e-mailadres in."];
        }

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
        {
            errors["password"] = ["Kies een wachtwoord van minimaal 8 tekens."];
        }

        if (string.IsNullOrWhiteSpace(request.HouseholdName))
        {
            errors["householdName"] = ["Geef je huishouden een naam."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            return Results.Conflict(new { message = "Er bestaat al een account met dit e-mailadres." });
        }

        // Een opgegeven installateurscode moet bestaan: nooit stilletjes bij een andere installateur terechtkomen.
        Installer? installer;
        if (string.IsNullOrWhiteSpace(request.InstallerSlug))
        {
            installer = await db.Installers.FirstOrDefaultAsync(i => i.Slug == DbSeeder.TripleEnergieSlug, ct)
                        ?? await db.Installers.OrderBy(i => i.CreatedAt).FirstOrDefaultAsync(ct);
        }
        else
        {
            var slug = request.InstallerSlug.Trim().ToLowerInvariant();
            installer = await db.Installers.FirstOrDefaultAsync(i => i.Slug == slug, ct);

            // Een nog niet goedgekeurde aanmelding mag geen klanten binnenhalen: anders schrijft iemand
            // zich in als installateur en trekt hij bewoners naar zijn eigen portal.
            if (installer is null || installer.ApprovedAt is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["installerSlug"] = ["Onbekende installateurscode. Controleer de code van je installateur."]
                });
            }
        }

        if (installer is null)
        {
            return Results.Problem(title: "Er is nog geen installateur ingesteld.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var now = clock.GetUtcNow();
        var household = new Household
        {
            Id = Guid.NewGuid(),
            Name = request.HouseholdName.Trim(),
            InstallerId = installer.Id,
            Installer = installer,
            InstallerAccessGranted = request.AllowInstallerAccess,
            InstallerAccessChangedAt = now,
            Plan = Plans.Normaliseer(request.Plan),
            PlanChosenAt = now,
            CreatedAt = now
        };

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = hasher.Hash(request.Password),
            Role = UserRole.Resident,
            HouseholdId = household.Id,
            Household = household,
            InstallerId = installer.Id,
            Installer = installer,
            CreatedAt = now
        };

        db.Households.Add(household);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        var (token, expires) = tokens.CreateToken(user);
        return Results.Created("/me", new AuthResponse(token, expires, HouseholdQueries.ToDto(household), HouseholdQueries.ToDto(installer)));
    }

    /// <summary>
    /// Een installateur meldt zich aan: bedrijf plus eerste gebruiker in een keer.
    ///
    /// Hij kan daarna inloggen en zijn eigen gegevens invullen, maar zijn code werkt nog niet bij het
    /// aanmelden van bewoners. Dat gaat pas open als Wattzicht hem goedkeurt; zie Installer.ApprovedAt.
    /// </summary>
    private static async Task<IResult> RegisterInstallerAsync(
        RegisterInstallerRequest request,
        AppDbContext db,
        PasswordHasher hasher,
        TokenService tokens,
        TimeProvider clock,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var bedrijf = request.Company?.Trim() ?? string.Empty;
        var contact = request.ContactPerson?.Trim() ?? string.Empty;

        if (bedrijf.Length is < 2 or > 200) errors["company"] = ["Vul je bedrijfsnaam in."];
        if (contact.Length is < 2 or > 200) errors["contactPerson"] = ["Vul je naam in."];
        if (!email.Contains('@') || email.Length > 320) errors["email"] = ["Vul een geldig e-mailadres in."];
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
        {
            errors["password"] = ["Kies een wachtwoord van minimaal acht tekens."];
        }

        if (request.CustomerCount is < 0 or > 1_000_000) errors["customerCount"] = ["Ongeldig aantal klanten."];
        if (request.Phone is { Length: > 40 }) errors["phone"] = ["Telefoonnummer is te lang."];
        if (request.City is { Length: > 120 }) errors["city"] = ["Plaatsnaam is te lang."];

        if (errors.Count > 0) return Results.ValidationProblem(errors);

        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email, ct))
        {
            return Results.Conflict(new { message = "Er bestaat al een account met dit e-mailadres." });
        }

        var now = clock.GetUtcNow();
        var installer = new Installer
        {
            Id = Guid.NewGuid(),
            Name = bedrijf,
            Slug = await VrijeSlugAsync(db, bedrijf, ct),
            ContactPerson = contact,
            ContactEmail = email,
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            City = string.IsNullOrWhiteSpace(request.City) ? null : request.City.Trim(),
            CustomerCount = request.CustomerCount,
            ApprovedAt = null,
            CreatedAt = now
        };

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = hasher.Hash(request.Password!),
            Role = UserRole.Installer,
            InstallerId = installer.Id,
            Installer = installer,
            CreatedAt = now
        };

        db.Installers.Add(installer);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        var (token, expires) = tokens.CreateToken(user);
        return Results.Created("/me", new AuthResponse(token, expires, null, HouseholdQueries.ToDto(installer),
            UserRole.Installer));
    }

    /// <summary>
    /// Een korte code uit de bedrijfsnaam, en bij een botsing een cijfer erachter. De code staat in
    /// links en vullen bewoners over, dus alleen letters, cijfers en streepjes.
    /// </summary>
    private static async Task<string> VrijeSlugAsync(AppDbContext db, string naam, CancellationToken ct)
    {
        var basis = new string(naam.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray());

        while (basis.Contains("--")) basis = basis.Replace("--", "-");
        basis = basis.Trim('-');
        if (basis.Length > 50) basis = basis[..50].Trim('-');
        if (basis.Length == 0) basis = "installateur";

        var kandidaat = basis;
        for (var n = 2; await db.Installers.IgnoreQueryFilters().AnyAsync(i => i.Slug == kandidaat, ct); n++)
        {
            kandidaat = $"{basis}-{n}";
        }

        return kandidaat;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        AppDbContext db,
        PasswordHasher hasher,
        TokenService tokens,
        CancellationToken ct)
    {
        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;

        // Inloggen is anoniem; het eigen huishouden van deze gebruiker mag wel mee in het antwoord.
        var user = await db.Users
            .IgnoreQueryFilters()
            .Include(u => u.Installer)
            .Include(u => u.Household!)
                .ThenInclude(h => h.Installer)
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        if (user is null || !hasher.Verify(request.Password, user.PasswordHash))
        {
            return Results.Problem(
                title: "Onjuist e-mailadres of wachtwoord.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return Results.Ok(BuildResponse(user, tokens));
    }

    private static AuthResponse BuildResponse(User user, TokenService tokens)
    {
        var (token, expires) = tokens.CreateToken(user);
        var household = user.Household is null ? null : HouseholdQueries.ToDto(user.Household);
        return new AuthResponse(token, expires, household, HouseholdQueries.ToDto(user.Installer), user.Role);
    }

    private static async Task<IResult> RefreshAsync(
        HttpContext http,
        AppDbContext db,
        TokenService tokens,
        CancellationToken ct)
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

        return Results.Ok(BuildResponse(user, tokens));
    }
}
