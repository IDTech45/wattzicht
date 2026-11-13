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

    public Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default) =>
        PostAsync<RegisterRequest, AuthResponse>("/auth/register", request, authenticated: false, ct);

    public Task<AuthResponse> ExternalLoginAsync(ExternalLoginRequest request, CancellationToken ct = default) =>
        PostAsync<ExternalLoginRequest, AuthResponse>("/auth/external", request, authenticated: false, ct);

    /// <summary>
    /// Welke inlogknoppen de server aan heeft staan.
    ///
    /// Gaat er iets mis, dan geeft dit een lege lijst en blijven de knoppen weg. Een knop die het
    /// niet doet is vervelender dan geen knop: je tikt erop, er gebeurt niets, en je weet niet of het
    /// aan jou ligt.
    /// </summary>
    public async Task<IReadOnlyList<string>> ProvidersAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, Relative("/auth/providers"));
            return await SendAsync<List<string>>(req, ct) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

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

    /// <summary>
    /// Een meting doorsturen die de app zelf op het thuisnetwerk ophaalde, zoals van een P1-meter.
    /// Het antwoord van het kastje gaat onvertaald mee; de server doet de omzetting.
    /// </summary>
    public Task StuurLokaleMetingAsync(Guid deviceId, LocalReadingRequest meting, CancellationToken ct = default) =>
        PostNoContentAsync($"/devices/{deviceId}/readings", meting, ct);

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

    public Task<SustainabilityDto> GetSustainabilityAsync(CancellationToken ct = default) =>
        GetAsync<SustainabilityDto>("/sustainability", ct);

    public Task<FeedInDto> GetFeedInAsync(CancellationToken ct = default) =>
        GetAsync<FeedInDto>("/feed-in", ct);

    public Task<AdviceDto> GetAdviceAsync(CancellationToken ct = default) =>
        GetAsync<AdviceDto>("/advice", ct);

    /// <summary>
    /// De tip van vandaag voor de kaart op het startscherm. Geeft null als er niets te melden valt;
    /// de server stuurt dan 204, en dan blijft de kaart weg in plaats van iets algemeens te zeggen.
    /// </summary>
    public async Task<DayTipDto?> GetDayTipAsync(CancellationToken ct = default)
    {
        try
        {
            return await GetAsync<DayTipDto?>("/today", ct);
        }
        catch (ApiException ex) when (!ex.IsUnauthorized)
        {
            // Geen tip is geen storing; het startscherm moet gewoon laden.
            return null;
        }
    }

    /// <summary>
    /// Wanneer de auto laden het voordeligst is. De planner weegt de uurprijs tegen de zon die op het
    /// eigen dak wordt verwacht; een kilowattuur van je eigen panelen kost alleen de
    /// terugleververgoeding die je misloopt.
    /// </summary>
    public Task<ChargePlanDto> GetChargePlanAsync(double kwh, DateTimeOffset klaarOm, CancellationToken ct = default) =>
        GetAsync<ChargePlanDto>(
            $"/charge-plan?kwh={kwh.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            + $"&by={Uri.EscapeDataString(klaarOm.ToString("O"))}", ct);

    // ---------- Tarief & installateur ----------

    public Task<TariffDto> GetTariffAsync(CancellationToken ct = default) =>
        GetAsync<TariffDto>("/tariff", ct);

    public Task<TariffDto> UpdateTariffAsync(TariffDto tariff, CancellationToken ct = default) =>
        PutAsync<TariffDto, TariffDto>("/tariff", tariff, ct);

    public Task<InstallerBrandingDto> GetInstallerAsync(CancellationToken ct = default) =>
        GetAsync<InstallerBrandingDto>("/installer", ct);

    /// <summary>Verwijdert het eigen huishouden met alles wat eraan hangt. Onomkeerbaar.</summary>
    public async Task VerwijderHuishoudenAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, Relative("/household"));
        await AuthorizeAsync(req);
        using var antwoord = await _http.SendAsync(req, ct);
        if (!antwoord.IsSuccessStatusCode)
        {
            throw new ApiException(Taal.T("fout.verwijderen-mislukt"), antwoord.StatusCode);
        }
    }

    /// <summary>
    /// Stuurt een export uit het portaal van de fabrikant naar de server, die er dagtotalen van maakt.
    ///
    /// Het bestand gaat zoals het is de deur uit. Hier parsen zou betekenen dat een telefoon die een
    /// half jaar niet is bijgewerkt een ander resultaat geeft dan de server, en dat is precies het
    /// soort verschil dat niemand terugvindt.
    /// </summary>
    public async Task<HistorieImportDto> ImporteerHistorieAsync(
        Stream bestand, string bestandsnaam, CancellationToken ct = default)
    {
        using var inhoud = new MultipartFormDataContent();
        using var deel = new StreamContent(bestand);
        deel.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        inhoud.Add(deel, "bestand", bestandsnaam);

        using var req = new HttpRequestMessage(HttpMethod.Post, Relative("/history/import")) { Content = inhoud };
        await AuthorizeAsync(req);
        return await SendAsync<HistorieImportDto>(req, ct);
    }

    /// <summary>
    /// Stuurt een energiecontract of jaarafrekening naar de server en krijgt de gevonden tarieven
    /// terug. Er wordt niets opgeslagen: de bewoner ziet eerst wat wij gelezen hebben.
    /// </summary>
    public async Task<TariefVoorstelDto> LeesTarievenAsync(
        Stream bestand, string bestandsnaam, string contentType, CancellationToken ct = default)
    {
        using var inhoud = new MultipartFormDataContent();
        using var deel = new StreamContent(bestand);
        deel.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        inhoud.Add(deel, "bestand", bestandsnaam);

        using var req = new HttpRequestMessage(HttpMethod.Post, Relative("/tariff/import")) { Content = inhoud };
        await AuthorizeAsync(req);
        return await SendAsync<TariefVoorstelDto>(req, ct);
    }

    /// <summary>Leest de kilowatturen per maand uit een overzicht van de leverancier.</summary>
    public async Task<VerbruikImportDto> LeesVerbruikAsync(
        Stream bestand, string bestandsnaam, string contentType, CancellationToken ct = default)
    {
        using var inhoud = new MultipartFormDataContent();
        using var deel = new StreamContent(bestand);
        deel.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        inhoud.Add(deel, "bestand", bestandsnaam);

        using var req = new HttpRequestMessage(HttpMethod.Post, Relative("/usage/import")) { Content = inhoud };
        await AuthorizeAsync(req);
        return await SendAsync<VerbruikImportDto>(req, ct);
    }

    /// <summary>Laat de server de apparaten van deze woning nu uitlezen.</summary>
    public Task<UitleesResultaatDto> NuUitlezenAsync(CancellationToken ct = default) =>
        PostAsync<object, UitleesResultaatDto>("/devices/refresh", new { }, authenticated: true, ct);

    /// <summary>Werkt naam, plaats en piekvermogen van de woning bij.</summary>
    public Task<HouseholdDto> WoningBijwerkenAsync(UpdateHouseholdRequest verzoek, CancellationToken ct = default) =>
        PutAsync<UpdateHouseholdRequest, HouseholdDto>("/household", verzoek, ct);

    /// <summary>Wat de app over LaadCash mag tonen voor deze woning.</summary>
    public Task<LaadcashPromoDto> LaadcashPromoAsync(CancellationToken ct = default) =>
        GetAsync<LaadcashPromoDto>("/laadcash/promo", ct);

    /// <summary>Legt vast dat de klant hem gezien of aangeklikt heeft.</summary>
    public Task LaadcashActieAsync(string kanaal, string actie, CancellationToken ct = default) =>
        PostAsync<object, object>("/laadcash/promo/action", new { channel = kanaal, action = actie }, authenticated: true, ct);

    // ---------- Intern ----------

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, Relative(path));
        await AuthorizeAsync(req);
        return await SendAsync<T>(req, ct);
    }

    private async Task<TOut> PostAsync<TIn, TOut>(string path, TIn? body, bool authenticated, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Relative(path));
        if (body is not null)
            req.Content = JsonContent.Create(body, options: JsonOptions);
        if (authenticated)
            await AuthorizeAsync(req);
        return await SendAsync<TOut>(req, ct);
    }

    private async Task<TOut> PutAsync<TIn, TOut>(string path, TIn body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, Relative(path))
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        await AuthorizeAsync(req);
        return await SendAsync<TOut>(req, ct);
    }

    private async Task PostNoContentAsync<TIn>(string path, TIn body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Relative(path))
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        await AuthorizeAsync(req);
        using var res = await SendRawAsync(req, ct);
        await EnsureSuccessAsync(res, ct);
    }

    private async Task DeleteAsync(string path, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, Relative(path));
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
            return result ?? throw new ApiException(Taal.T("fout.leeg-antwoord"), res.StatusCode);
        }
        catch (JsonException ex)
        {
            throw new ApiException(Taal.T("fout.onverwacht-antwoord"), res.StatusCode, ex);
        }
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpRequestMessage req, CancellationToken ct)
    {
        // De gekozen taal gaat mee bij elk verzoek. Een deel van de tekst die de gebruiker ziet
        // wordt op de server opgebouwd met gemeten waarden (de tips, de dagtip, het laadplan); zonder
        // deze kop staat daar Nederlands onder een Turks scherm.
        if (!req.Headers.Contains("Accept-Language"))
        {
            req.Headers.Add("Accept-Language", Taal.Code);
        }

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
            throw new ApiException(Taal.T("fout.server-reageert-niet"), null, ex);
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

    /// <summary>
    /// Paden relatief aan de basis-URL maken. De API staat nu onder <c>https://api.wattzicht.nl/api/</c>;
    /// een pad met voorloop-slash zou dat <c>/api</c> overslaan.
    /// </summary>
    private static string Relative(string path) => path.TrimStart('/');
}
