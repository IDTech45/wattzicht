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

        group.MapPost("/external", ExternalAsync)
            .WithSummary("Logt in of meldt aan met een token van Google of Apple.")
            .Produces<AuthResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/providers", (ExternalAuthService extern_) => Results.Ok(extern_.Beschikbaar))
            .WithSummary("Welke inlogknoppen de app mag tonen.")
            .Produces<IReadOnlyList<string>>();

        group.MapPost("/password", WachtwoordWijzigenAsync)
            .RequireAuthorization()
            .WithSummary("Wijzigt het wachtwoord van de ingelogde gebruiker.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

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
        SubscriptionWriter subscriptions,
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

        // Met een installateurscode hoort een expliciet vinkje: zonder toestemming mag die installateur
        // de gegevens van deze woning niet zien, en dan is er ook geen grond om hem eraan te koppelen.
        if (!string.IsNullOrWhiteSpace(request.InstallerSlug) && !request.AllowInstallerAccess)
        {
            errors["allowInstallerAccess"] =
                ["Vink aan dat je installateur je gegevens mag zien, of laat de code leeg."];
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

        // Kwam hij binnen met de code van zijn installateur, dan regelt die installateur zijn Plus.
        // Bij een gratis installateur (Triple Energie) is dat twaalf maanden; bij een betalende loopt
        // het door zolang hij klant is. Zonder code gebeurt er niets: dan is hij gewoon gratis, en
        // krijgt hij zijn proefperiode zodra hij zijn eerste apparaat koppelt.
        if (!string.IsNullOrWhiteSpace(request.InstallerSlug))
        {
            var duur = installer.BillingMode == InstallerBillingMode.Free
                ? TimeSpan.FromDays(365)
                : (TimeSpan?)null;
            await subscriptions.StartAsync(
                household.Id, SubscriptionSource.Installer, duur, installerId: installer.Id, ct: ct);
        }

        var (token, expires) = tokens.CreateToken(user);
        return Results.Created("/me", new AuthResponse(token, expires, HouseholdQueries.ToDto(household), HouseholdQueries.ToDto(installer)));
    }

    /// <summary>
    /// Inloggen of aanmelden met Google of Apple.
    ///
    /// Eén endpoint voor allebei, want het verschil zit alleen in het controleren van het token. Is
    /// het adres bij ons bekend, dan logt diegene in; kennen we het niet, dan maken we het account
    /// meteen aan. Dat is precies wat iemand verwacht als hij op "doorgaan met Google" drukt: hij
    /// denkt niet in aanmelden tegenover inloggen.
    ///
    /// Koppelen gaat op het e-mailadres, en alleen als de aanbieder zegt dat hij het zelf heeft
    /// gecontroleerd. Zonder die eis zou iemand een account bij een of andere partij kunnen maken met
    /// jouw adres en daarmee bij jouw woning naar binnen lopen.
    ///
    /// Een account dat zo ontstaat heeft geen wachtwoord. De hash blijft leeg, en de controle wijst
    /// een lege hash altijd af; inloggen met een wachtwoord kan dus niet, ook niet met een gokje.
    /// </summary>
    private static async Task<IResult> ExternalAsync(
        ExternalLoginRequest request,
        ExternalAuthService extern_,
        AppDbContext db,
        TokenService tokens,
        ILoggerFactory logs,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdToken))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["idToken"] = ["Er kwam geen token mee."]
            });
        }

        ExternalUser info;
        try
        {
            info = await extern_.ControleerAsync(request.Provider, request.IdToken, ct);
        }
        catch (ExternalAuthException ex)
        {
            // De reden hoort in het logboek, niet op het scherm van de gebruiker: hij kan er niets
            // mee, en een afgekeurd token is precies het geval waarin je niets wilt prijsgeven.
            logs.CreateLogger("ExternalAuth").LogWarning("Inlogtoken afgekeurd: {Reden}", ex.Message);
            return Results.Unauthorized();
        }

        if (!info.EmailVerified)
        {
            logs.CreateLogger("ExternalAuth").LogWarning(
                "Token van {Provider} met een ongecontroleerd e-mailadres geweigerd.", info.Provider);
            return Results.Unauthorized();
        }

        var bestaand = await db.Users
            .Include(u => u.Household)
            .Include(u => u.Installer)
            .FirstOrDefaultAsync(u => u.Email == info.Email, ct);

        if (bestaand is not null)
        {
            var (bestaandToken, bestaandTot) = tokens.CreateToken(bestaand);
            return Results.Ok(new AuthResponse(bestaandToken, bestaandTot,
                bestaand.Household is null ? null : HouseholdQueries.ToDto(bestaand.Household),
                HouseholdQueries.ToDto(bestaand.Installer)));
        }

        var installer = await db.Installers.FirstOrDefaultAsync(i => i.Slug == DbSeeder.TripleEnergieSlug, ct)
                        ?? await db.Installers.OrderBy(i => i.CreatedAt).FirstOrDefaultAsync(ct);
        if (installer is null)
        {
            return Results.Problem(title: "Er is nog geen installateur ingesteld.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var nu = clock.GetUtcNow();
        var woning = new Household
        {
            Id = Guid.NewGuid(),
            // Zonder opgegeven naam gewoon "Thuis". Iemand die met één druk op de knop binnenkomt,
            // hoort niet eerst een formulier te krijgen; hij kan de naam later wijzigen.
            Name = string.IsNullOrWhiteSpace(request.HouseholdName) ? "Thuis" : request.HouseholdName.Trim(),
            InstallerId = installer.Id,
            Installer = installer,
            InstallerAccessGranted = false,
            InstallerAccessChangedAt = nu,
            Plan = Plans.Normaliseer(null),
            PlanChosenAt = nu,
            CreatedAt = nu
        };

        var nieuw = new User
        {
            Id = Guid.NewGuid(),
            Email = info.Email,
            PasswordHash = string.Empty,
            Role = UserRole.Resident,
            HouseholdId = woning.Id,
            Household = woning,
            InstallerId = installer.Id,
            Installer = installer,
            CreatedAt = nu
        };

        db.Households.Add(woning);
        db.Users.Add(nieuw);
        await db.SaveChangesAsync(ct);

        var (token, tot) = tokens.CreateToken(nieuw);
        return Results.Created("/me", new AuthResponse(token, tot,
            HouseholdQueries.ToDto(woning), HouseholdQueries.ToDto(installer)));
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

    /// <summary>
    /// Je eigen wachtwoord wijzigen.
    ///
    /// Dit kon helemaal niet: je kreeg bij het aanmelden een wachtwoord en daar zat je aan vast. Het
    /// huidige wachtwoord moet mee, ook al ben je ingelogd: een telefoon die even onbeheerd op tafel
    /// ligt hoort niet genoeg te zijn om iemand buiten te sluiten.
    ///
    /// Wie met Google of Apple is binnengekomen heeft geen wachtwoord. Die krijgt dat ook te horen,
    /// in plaats van een foutmelding over een verkeerd wachtwoord dat nooit heeft bestaan.
    /// </summary>
    private static async Task<IResult> WachtwoordWijzigenAsync(
        WachtwoordWijzigenRequest request,
        HttpContext http,
        AppDbContext db,
        PasswordHasher hasher,
        CancellationToken ct)
    {
        var userId = http.User.GetUserId();
        var gebruiker = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (gebruiker is null) return Results.NotFound();

        if (string.IsNullOrEmpty(gebruiker.PasswordHash))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["huidig"] = ["Je bent binnengekomen met Google of Apple en hebt dus geen wachtwoord."],
            });
        }

        if (!hasher.Verify(request.Huidig ?? string.Empty, gebruiker.PasswordHash))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["huidig"] = ["Dit is niet je huidige wachtwoord."],
            });
        }

        var nieuw = request.Nieuw ?? string.Empty;
        if (nieuw.Length < 8)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["nieuw"] = ["Kies een wachtwoord van minimaal 8 tekens."],
            });
        }

        if (hasher.Verify(nieuw, gebruiker.PasswordHash))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["nieuw"] = ["Dat is je huidige wachtwoord."],
            });
        }

        gebruiker.PasswordHash = hasher.Hash(nieuw);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
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
