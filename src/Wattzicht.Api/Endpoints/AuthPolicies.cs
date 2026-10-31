using Microsoft.AspNetCore.Authorization;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Endpoints;

/// <summary>Namen en registratie van de autorisatiebeleidsregels.</summary>
public static class AuthPolicies
{
    /// <summary>Bewoner met een huishouden in het token.</summary>
    public const string Resident = "Resident";

    /// <summary>Installateur (rol Installer).</summary>
    public const string Installer = "Installer";

    /// <summary>Beheerder van Wattzicht zelf (rol Admin).</summary>
    public const string Admin = "Admin";

    public static AuthorizationOptions AddWattzichtPolicies(this AuthorizationOptions options)
    {
        options.AddPolicy(Resident, policy => policy
            .RequireAuthenticatedUser()
            .RequireAssertion(ctx => ctx.User.GetHouseholdId() is not null));

        options.AddPolicy(Installer, policy => policy
            .RequireAuthenticatedUser()
            .RequireRole(nameof(UserRole.Installer)));

        // Bewust alleen Admin: een beheerder hoort niet stilletjes als installateur te kunnen werken,
        // want die endpoints filteren op zijn eigen bedrijf en dat heeft hij niet.
        options.AddPolicy(Admin, policy => policy
            .RequireAuthenticatedUser()
            .RequireRole(nameof(UserRole.Admin)));

        return options;
    }
}
