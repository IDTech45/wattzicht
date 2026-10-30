namespace Wattzicht.Api.Services;

/// <summary>
/// Wie vraagt er data op? Bepaalt de scheiding tussen installateurs en huishoudens in <see cref="Data.AppDbContext"/>.
/// <list type="bullet">
/// <item><b>Systeem</b> (pollworker, seeder, anonieme endpoints zoals login): geen filter.</item>
/// <item><b>Bewoner</b>: alleen het eigen huishouden.</item>
/// <item><b>Installateur</b>: alleen huishoudens van de eigen installateur die toestemming gaven.</item>
/// <item>Ingelogd maar geen van beide: niets.</item>
/// </list>
/// </summary>
public sealed class TenantContext
{
    private readonly IHttpContextAccessor? _accessor;

    public TenantContext(IHttpContextAccessor accessor) => _accessor = accessor;

    private TenantContext() => _accessor = null;

    /// <summary>Voor achtergrondwerk en tests zonder DI: altijd systeem, kijkt nooit naar een request.</summary>
    public static TenantContext System { get; } = new();

    private HttpContext? Http => _accessor?.HttpContext;

    private bool IsAuthenticated => Http?.User.Identity?.IsAuthenticated == true;

    /// <summary>Geen ingelogde gebruiker: achtergrondtaak of anoniem endpoint. Die zien geen huishouddata via de API.</summary>
    public bool IsSystem => !IsAuthenticated;

    /// <summary>Het huishouden van een ingelogde bewoner.</summary>
    public Guid? HouseholdId => IsAuthenticated ? Http!.User.GetHouseholdId() : null;

    /// <summary>De installateur, alleen als de ingelogde gebruiker de rol Installer heeft.</summary>
    public Guid? InstallerId
    {
        get
        {
            if (!IsAuthenticated)
            {
                return null;
            }

            var user = Http!.User;
            if (!user.IsInstaller())
            {
                return null;
            }

            var id = user.GetInstallerId();
            return id == Guid.Empty ? null : id;
        }
    }
}
