using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wattzicht.Shared;

namespace Wattzicht.App.Services;

/// <summary>
/// Getypte client voor de Wattzicht-API (zie docs/ARCHITECTUUR.md: auth, devices, dashboard, installer).
/// Zet automatisch de Bearer-header en vertaalt fouten naar <see cref="ApiException"/>.
/// </summary>
public sealed class ApiClient
{
    private readonly HttpClient _http;
    private readonly TokenStore _tokens;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ApiClient(HttpClient http, TokenStore tokens)
    {
        _http = http;
        _tokens = tokens;
    }

    // ---------- Auth ----------

    public Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default) =>
        PostAsync<LoginRequest, AuthResponse>("/auth/login", request, authenticated: false, ct);

    public Task<AuthResponse> RefreshAsync(CancellationToken ct = default) =>
        PostAsync<object?, AuthResponse>("/auth/refresh", null, authenticated: true, ct);

    public Task<HouseholdDto> GetHouseholdAsync(CancellationToken ct = default) =>
        GetAsync<HouseholdDto>("/household", ct);

    // ---------- Dashboard ----------

    public Task<DashboardDto> GetDashboardAsync(CancellationToken ct = default) =>
        GetAsync<DashboardDto>("/dashboard", ct);

    public Task<LiveSnapshotDto> GetLiveAsync(CancellationToken ct = default) =>
        GetAsync<LiveSnapshotDto>("/dashboard/live", ct);

    public Task<IReadOnlyList<AlertDto>> GetAlertsAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<AlertDto>>("/dashboard/alerts", ct);

    /// <summary>Dagelijkse samenvattingen (inclusief) tussen twee datums.</summary>
    public Task<IReadOnlyList<DailySummaryDto>> GetDailySummariesAsync(DateOnly from, DateOnly to, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<DailySummaryDto>>($"/dashboard/summaries?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", ct);

    // ---------- Devices ----------

    public Task<IReadOnlyList<DeviceDto>> GetDevicesAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<DeviceDto>>("/devices", ct);

    public Task<IReadOnlyList<DeviceDto>> ConnectDeviceAsync(ConnectDeviceRequest request, CancellationToken ct = default) =>
        PostAsync<ConnectDeviceRequest, IReadOnlyList<DeviceDto>>("/devices", request, authenticated: true, ct);

    public Task DeleteDeviceAsync(Guid deviceId, CancellationToken ct = default) =>
        DeleteAsync($"/devices/{deviceId}", ct);

    // ---------- Pushmeldingen ----------

    public Task RegisterPushDeviceAsync(RegisterPushDeviceRequest request, CancellationToken ct = default) =>
        PostNoContentAsync("/push/devices", request, ct);

    public Task UnregisterPushDeviceAsync(string token, CancellationToken ct = default) =>
        DeleteAsync($"/push/devices?token={Uri.EscapeDataString(token)}", ct);

    public Task<NotificationSettingsDto> GetNotificationSettingsAsync(CancellationToken ct = default) =>
        GetAsync<NotificationSettingsDto>("/push/settings", ct);

    public Task<NotificationSettingsDto> SetNotificationSettingsAsync(NotificationSettingsDto settings, CancellationToken ct = default) =>
        PutAsync<NotificationSettingsDto, NotificationSettingsDto>("/push/settings", settings, ct);

    // ---------- Meekijken door installateur ----------

    public Task<InstallerAccessDto> GetInstallerAccessAsync(CancellationToken ct = default) =>
        GetAsync<InstallerAccessDto>("/household/installer-access", ct);

    public Task<InstallerAccessDto> SetInstallerAccessAsync(bool granted, CancellationToken ct = default) =>
        PutAsync<UpdateInstallerAccessRequest, InstallerAccessDto>("/household/installer-access", new UpdateInstallerAccessRequest(granted), ct);

    public Task<IReadOnlyList<AccessLogEntryDto>> GetAccessLogAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<AccessLogEntryDto>>("/household/access-log", ct);

    // ---------- Slim sturen ----------

    public Task<FeedInDto> GetFeedInAsync(CancellationToken ct = default) =>
        GetAsync<FeedInDto>("/feed-in", ct);

    public Task<AdviceDto> GetAdviceAsync(CancellationToken ct = default) =>
        GetAsync<AdviceDto>("/advice", ct);

    // ---------- Tarief & installateur ----------

    public Task<TariffDto> GetTariffAsync(CancellationToken ct = default) =>
        GetAsync<TariffDto>("/tariff", ct);

    public Task<TariffDto> UpdateTariffAsync(TariffDto tariff, CancellationToken ct = default) =>
        PutAsync<TariffDto, TariffDto>("/tariff", tariff, ct);

    public Task<InstallerBrandingDto> GetInstallerAsync(CancellationToken ct = default) =>
        GetAsync<InstallerBrandingDto>("/installer", ct);

    // ---------- Intern ----------

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        await AuthorizeAsync(req);
        return await SendAsync<T>(req, ct);
    }

    private async Task<TOut> PostAsync<TIn, TOut>(string path, TIn? body, bool authenticated, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path);
        if (body is not null)
            req.Content = JsonContent.Create(body, options: JsonOptions);
        if (authenticated)
            await AuthorizeAsync(req);
        return await SendAsync<TOut>(req, ct);
    }

    private async Task<TOut> PutAsync<TIn, TOut>(string path, TIn body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        await AuthorizeAsync(req);
        return await SendAsync<TOut>(req, ct);
    }

    private async Task PostNoContentAsync<TIn>(string path, TIn body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        await AuthorizeAsync(req);
        using var res = await SendRawAsync(req, ct);
        await EnsureSuccessAsync(res, ct);
    }

    private async Task DeleteAsync(string path, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, path);
        await AuthorizeAsync(req);
        using var res = await SendRawAsync(req, ct);
        await EnsureSuccessAsync(res, ct);
    }

    private async Task AuthorizeAsync(HttpRequestMessage req)
    {
        var token = await _tokens.GetTokenAsync();
        if (!string.IsNullOrEmpty(token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage req, CancellationToken ct)
    {
        using var res = await SendRawAsync(req, ct);
        await EnsureSuccessAsync(res, ct);

        if (res.StatusCode == HttpStatusCode.NoContent)
            return default!;

        try
        {
            var result = await res.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
            return result ?? throw new ApiException("Leeg antwoord van de server.", res.StatusCode);
        }
        catch (JsonException ex)
        {
            throw new ApiException("Onverwacht antwoord van de server.", res.StatusCode, ex);
        }
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpRequestMessage req, CancellationToken ct)
    {
        try
        {
            return await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            throw new ApiException("De server reageert niet. Probeer het later opnieuw.", null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw ApiException.Network(ex);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage res, CancellationToken ct)
    {
        if (res.IsSuccessStatusCode) return;

        string? serverMessage = null;
        try
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!string.IsNullOrWhiteSpace(body))
            {
                // ProblemDetails ("title"/"detail") of een platte { "message": "..." }
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String) serverMessage = d.GetString();
                    else if (root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String) serverMessage = m.GetString();
                    else if (root.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String) serverMessage = t.GetString();
                }
            }
        }
        catch
        {
            // Body is geen JSON: gebruik de standaardmelding.
        }

        throw ApiException.FromStatus(res.StatusCode, serverMessage);
    }
}
