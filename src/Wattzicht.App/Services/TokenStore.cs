namespace Wattzicht.App.Services;

/// <summary>
/// Bewaart het JWT-token veilig via SecureStorage (Keychain op iOS, EncryptedSharedPreferences op Android).
/// Houdt een kopie in het geheugen zodat ApiClient niet bij elk verzoek de keychain hoeft te lezen.
/// </summary>
public sealed class TokenStore
{
    private const string TokenKey = "wz_token";
    private const string ExpiresKey = "wz_token_expires";

    private string? _token;
    private DateTimeOffset? _expiresAt;
    private bool _loaded;

    public async Task<string?> GetTokenAsync()
    {
        await EnsureLoadedAsync();
        if (_expiresAt is { } exp && exp <= DateTimeOffset.UtcNow)
            return null;
        return _token;
    }

    public async Task<DateTimeOffset?> GetExpiresAtAsync()
    {
        await EnsureLoadedAsync();
        return _expiresAt;
    }

    public async Task SetAsync(string token, DateTimeOffset expiresAt)
    {
        _token = token;
        _expiresAt = expiresAt;
        _loaded = true;
        await SecureStorage.Default.SetAsync(TokenKey, token);
        await SecureStorage.Default.SetAsync(ExpiresKey, expiresAt.ToString("O"));
    }

    public void Clear()
    {
        _token = null;
        _expiresAt = null;
        _loaded = true;
        SecureStorage.Default.Remove(TokenKey);
        SecureStorage.Default.Remove(ExpiresKey);
    }

    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        try
        {
            _token = await SecureStorage.Default.GetAsync(TokenKey);
            var exp = await SecureStorage.Default.GetAsync(ExpiresKey);
            _expiresAt = DateTimeOffset.TryParse(exp, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : null;
        }
        catch
        {
            // SecureStorage kan falen op een emulator zonder schermvergrendeling; dan ben je gewoon uitgelogd.
            _token = null;
            _expiresAt = null;
        }
        _loaded = true;
    }
}
