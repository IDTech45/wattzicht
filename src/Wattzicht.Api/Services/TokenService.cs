using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Services;

/// <summary>Instellingen voor JWT-tokens (sectie <c>Jwt</c> in appsettings; secret via env <c>JWT_SECRET</c>).</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "wattzicht";
    public string Audience { get; set; } = "wattzicht-app";

    /// <summary>Minimaal 32 tekens. Komt uit <c>JWT_SECRET</c>.</summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>Geldigheidsduur; standaard 7 dagen (zie ARCHITECTUUR.md).</summary>
    public int LifetimeDays { get; set; } = 7;
}

/// <summary>Namen van de claims die Wattzicht in het token zet.</summary>
public static class WattzichtClaims
{
    public const string UserId = "sub";
    public const string Email = "email";
    /// <summary>Korte claimnaam; we zetten de outbound-mapping van JwtSecurityTokenHandler uit en gebruiken deze naam letterlijk.</summary>
    public const string Role = "role";
    public const string HouseholdId = "household_id";
    public const string InstallerId = "installer_id";
}

/// <summary>Maakt en valideert JWT bearer-tokens.</summary>
public sealed class TokenService
{
    private readonly JwtOptions _options;
    private readonly SymmetricSecurityKey _key;
    private readonly TimeProvider _clock;

    public TokenService(JwtOptions options, TimeProvider clock)
    {
        if (string.IsNullOrWhiteSpace(options.Secret) || options.Secret.Length < 32)
        {
            throw new InvalidOperationException("JWT_SECRET ontbreekt of is korter dan 32 tekens.");
        }

        _options = options;
        _clock = clock;
        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Secret));
    }

    /// <summary>Validatieparameters voor de JwtBearer-middleware; dezelfde sleutel en issuer/audience als bij uitgifte.</summary>
    public TokenValidationParameters ValidationParameters => new()
    {
        ValidateIssuer = true,
        ValidIssuer = _options.Issuer,
        ValidateAudience = true,
        ValidAudience = _options.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = _key,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1),
        RoleClaimType = WattzichtClaims.Role,
        NameClaimType = WattzichtClaims.Email
    };

    /// <summary>Maakt een token voor de gebruiker en geeft ook het verloopmoment terug.</summary>
    public (string Token, DateTimeOffset ExpiresAt) CreateToken(User user)
    {
        var now = _clock.GetUtcNow();
        var expires = now.AddDays(_options.LifetimeDays);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(WattzichtClaims.Role, user.Role.ToString()),
            new(WattzichtClaims.InstallerId, user.InstallerId.ToString())
        };

        if (user.HouseholdId is { } householdId)
        {
            claims.Add(new Claim(WattzichtClaims.HouseholdId, householdId.ToString()));
        }

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));

        // Geen outbound-mapping: claimnamen blijven exact zoals hierboven ("sub", "email", "role").
        var handler = new JwtSecurityTokenHandler { OutboundClaimTypeMap = new Dictionary<string, string>() };
        return (handler.WriteToken(token), expires);
    }
}

/// <summary>Hulpjes om de ingelogde gebruiker uit de claims te halen.</summary>
public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                    ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }

    public static Guid? GetHouseholdId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirst(WattzichtClaims.HouseholdId)?.Value;
        return Guid.TryParse(value, out var id) ? id : null;
    }

    public static Guid GetInstallerId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirst(WattzichtClaims.InstallerId)?.Value;
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }

    public static bool IsInstaller(this ClaimsPrincipal principal) =>
        principal.IsInRole(nameof(UserRole.Installer));
}
