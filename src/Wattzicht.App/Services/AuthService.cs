using Wattzicht.Shared;

namespace Wattzicht.App.Services;

/// <summary>
/// Inloggen, uitloggen en sessiestatus. Token staat in SecureStorage via <see cref="TokenStore"/>.
/// </summary>
public sealed class AuthService
{
    private readonly ApiClient _api;
    private readonly TokenStore _tokens;

    public HouseholdDto? Household { get; private set; }

    public event EventHandler? LoggedOut;

    public AuthService(ApiClient api, TokenStore tokens)
    {
        _api = api;
        _tokens = tokens;
    }

    public async Task<bool> IsLoggedInAsync() =>
        !string.IsNullOrEmpty(await _tokens.GetTokenAsync());

    public async Task LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var response = await _api.LoginAsync(new LoginRequest(email.Trim(), password), ct);
        await _tokens.SetAsync(response.Token, response.ExpiresAt);
        Household = response.Household;
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

    public Task LogoutAsync()
    {
        _tokens.Clear();
        Household = null;
        LoggedOut?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }
}
