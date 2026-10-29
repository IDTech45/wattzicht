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

    public static AuthorizationOptions AddWattzichtPolicies(this AuthorizationOptions options)
    {
        options.AddPolicy(Resident, policy => policy
            .RequireAuthenticatedUser()
            .RequireAssertion(ctx => ctx.User.GetHouseholdId() is not null));

        options.AddPolicy(Installer, policy => policy
            .RequireAuthenticatedUser()
            .RequireRole(nameof(UserRole.Installer)));

        return options;
    }
}
