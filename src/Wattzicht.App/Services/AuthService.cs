using Wattzicht.Shared;

namespace Wattzicht.App.Services;

/// <summary>
/// Inloggen, uitloggen en sessiestatus. Token staat in SecureStorage via <see cref="TokenStore"/>.
/// </summary>
public sealed class AuthService
{
    private readonly ApiClient _api;
    private readonly TokenStore _tokens;
    private readonly PushService _push;

    public HouseholdDto? Household { get; private set; }

    /// <summary>
    /// De rol van wie er is ingelogd. Nodig om na het inloggen naar het juiste scherm te gaan: een
    /// installateur hoort niet op een dashboard van een woning die niet van hem is.
    ///
    /// Blijft op het toestel staan, want bij het opstarten weten we de rol anders pas na een
    /// aanroep, en dan zou een installateur eerst een half bewonersscherm te zien krijgen.
    /// </summary>
    public UserRole Rol
    {
        get => (UserRole)Preferences.Default.Get(RolSleutel, (int)UserRole.Resident);
        private set => Preferences.Default.Set(RolSleutel, (int)value);
    }

    private const string RolSleutel = "gebruiker-rol";

    public bool IsInstallateur => Rol == UserRole.Installer;

    /// <summary>
    /// Vervangt wat we van de woning weten na een wijziging door de bewoner.
    ///
    /// Zonder dit staat de oude naam nog in de app tot je opnieuw inlogt, en zou het instellingen-
    /// scherm een plaats tonen die er niet meer is.
    /// </summary>
    public void ZetHuishouden(HouseholdDto woning) => Household = woning;

    public event EventHandler? LoggedOut;

    public AuthService(ApiClient api, TokenStore tokens, PushService push)
    {
        _api = api;
        _tokens = tokens;
        _push = push;
    }

    public async Task<bool> IsLoggedInAsync() =>
        !string.IsNullOrEmpty(await _tokens.GetTokenAsync());

    public async Task LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var response = await _api.LoginAsync(new LoginRequest(email.Trim(), password), ct);
        await _tokens.SetAsync(response.Token, response.ExpiresAt);
        Household = response.Household;
        Rol = response.Role;
        LoggedIn?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Een account aanmaken en er meteen mee binnen zijn.
    ///
    /// De server geeft bij een geslaagde registratie hetzelfde token terug als bij een login, dus
    /// hoeft niemand zijn wachtwoord twee keer in te tikken. Een installateurscode mag, maar hoeft
    /// niet: iedereen kan zich aanmelden en zijn apparaten koppelen. Vult iemand wél een code in,
    /// dan geeft hij die installateur ook toegang tot zijn gegevens, en dat vraagt de server
    /// expliciet.
    /// </summary>
    public async Task RegisterAsync(string email, string password, string householdName,
        string? installerSlug = null, CancellationToken ct = default)
    {
        var code = string.IsNullOrWhiteSpace(installerSlug) ? null : installerSlug.Trim();
        var response = await _api.RegisterAsync(
            new RegisterRequest(email.Trim(), password, householdName.Trim(), code, code is not null), ct);

        await _tokens.SetAsync(response.Token, response.ExpiresAt);
        Household = response.Household;
        Rol = response.Role;
        LoggedIn?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Inloggen met een token van Google of Apple.
    ///
    /// Kent de server het adres, dan log je in; kent hij het niet, dan maakt hij het account meteen
    /// aan. Voor de app is dat hetzelfde: er komt een token terug en je bent binnen.
    /// </summary>
    public async Task ExternalLoginAsync(string provider, string idToken, CancellationToken ct = default)
    {
        var response = await _api.ExternalLoginAsync(new ExternalLoginRequest(provider, idToken), ct);
        await _tokens.SetAsync(response.Token, response.ExpiresAt);
        Household = response.Household;
        Rol = response.Role;
        LoggedIn?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Na een geslaagde login (bv. om het toestel voor pushmeldingen aan te melden).</summary>
    public event EventHandler? LoggedIn;

    /// <summary>Vernieuwt het token als het binnen een dag verloopt. Fouten worden stil genegeerd.</summary>
    public async Task TryRefreshAsync(CancellationToken ct = default)
    {
        try
        {
            var expires = await _tokens.GetExpiresAtAsync();
            if (expires is null || expires > DateTimeOffset.UtcNow.AddDays(1)) return;

            var response = await _api.RefreshAsync(ct);
            await _tokens.SetAsync(response.Token, response.ExpiresAt);
            Household = response.Household;
            Rol = response.Role;
        }
        catch (ApiException ex) when (ex.IsUnauthorized)
        {
            await LogoutAsync();
        }
        catch (ApiException)
        {
            // Offline: laat het oude token staan.
        }
    }

    public async Task LogoutAsync()
    {
        // Eerst push afmelden (heeft het token nog nodig), ook bij een automatische uitlog na een 401:
        // dan lukt de API-call niet, maar het Firebase-token wordt gewist en de server ruimt het daarna op.
        await _push.UnregisterAsync();
        _tokens.Clear();
        Household = null;
        Rol = UserRole.Resident;
        LoggedOut?.Invoke(this, EventArgs.Empty);
    }
}
