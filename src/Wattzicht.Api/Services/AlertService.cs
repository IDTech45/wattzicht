using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Services;

/// <summary>
/// Storingsdetectie. Regels v0.1:
/// <list type="bullet">
/// <item><b>NoData</b>: langer dan 30 minuten geen meting van het apparaat.</item>
/// <item><b>ZeroProduction</b>: omvormer meldt 0 W zon tussen 10:00 en 16:00 Nederlandse tijd.</item>
/// <item><b>Offline</b>: de connector kan het apparaat niet bereiken of krijgt een fout terug.</item>
/// </list>
/// Meldingen worden automatisch gesloten zodra de oorzaak weg is. Wijzigingen worden niet opgeslagen;
/// de aanroeper doet <c>SaveChangesAsync</c>.
/// </summary>
public sealed class AlertService(AppDbContext db, TimeProvider clock, ILogger<AlertService> logger)
{
    private readonly List<Alert> _opened = [];

    /// <summary>Meldingen die in deze scope nieuw zijn geopend (voor de pushmelding na het opslaan).</summary>
    public IReadOnlyList<Alert> Opened => _opened;

    public static readonly TimeSpan NoDataThreshold = TimeSpan.FromMinutes(30);
    public const int ProductionWindowStartHour = 10;
    public const int ProductionWindowEndHour = 16;

    /// <summary>Werkt NoData bij op basis van <see cref="Device.LastSeenAt"/>.</summary>
    public async Task CheckNoDataAsync(Device device, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var reference = device.LastSeenAt ?? device.CreatedAt;
        var stale = now - reference > NoDataThreshold;

        if (stale)
        {
            await OpenAsync(device, AlertType.NoData,
                $"Al {FormatDuration(now - reference)} geen gegevens van {device.Name}.", ct);
        }
        else
        {
            await ResolveAsync(device, AlertType.NoData, ct);
        }
    }

    /// <summary>Werkt ZeroProduction bij op basis van de laatste meting van een omvormer.</summary>
    public async Task CheckZeroProductionAsync(Device device, Reading reading, CancellationToken ct)
    {
        if (device.Type != DeviceType.Inverter)
        {
            return;
        }

        var hour = DutchTime.LocalHour(reading.Timestamp);
        var inWindow = hour >= ProductionWindowStartHour && hour < ProductionWindowEndHour;

        if (inWindow && reading.PvPowerW <= 0)
        {
            await OpenAsync(device, AlertType.ZeroProduction,
                $"{device.Name} wekt overdag niets op. Controleer of de omvormer aanstaat.", ct);
        }
        else if (reading.PvPowerW > 0)
        {
            await ResolveAsync(device, AlertType.ZeroProduction, ct);
        }
    }

    /// <summary>Zet of sluit de Offline-melding na een uitleespoging.</summary>
    public async Task SetOfflineAsync(Device device, string? error, CancellationToken ct)
    {
        if (error is null)
        {
            await ResolveAsync(device, AlertType.Offline, ct);
        }
        else
        {
            await OpenAsync(device, AlertType.Offline, $"{device.Name} is niet bereikbaar: {error}", ct);
        }
    }

    private async Task OpenAsync(Device device, AlertType type, string message, CancellationToken ct)
    {
        var existing = await FindOpenAsync(device.Id, type, ct);
        if (existing is not null)
        {
            // Bericht actueel houden (bijvoorbeeld de duur), maar 'Since' niet verschuiven.
            existing.Message = Truncate(message);
            return;
        }

        logger.LogInformation("Melding {Type} geopend voor apparaat {DeviceId}", type, device.Id);
        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            DeviceId = device.Id,
            Type = type,
            Message = Truncate(message),
            Since = clock.GetUtcNow()
        };
        db.Alerts.Add(alert);
        _opened.Add(alert);
    }

    private async Task ResolveAsync(Device device, AlertType type, CancellationToken ct)
    {
        var existing = await FindOpenAsync(device.Id, type, ct);
        if (existing is null)
        {
            return;
        }

        existing.ResolvedAt = clock.GetUtcNow();
        logger.LogInformation("Melding {Type} gesloten voor apparaat {DeviceId}", type, device.Id);
    }

    private Task<Alert?> FindOpenAsync(Guid deviceId, AlertType type, CancellationToken ct)
    {
        // Eerst in de change tracker kijken, zodat we binnen één pollronde geen dubbele melding maken.
        var tracked = db.ChangeTracker.Entries<Alert>()
            .Select(e => e.Entity)
            .FirstOrDefault(a => a.DeviceId == deviceId && a.Type == type && a.ResolvedAt is null);

        return tracked is not null
            ? Task.FromResult<Alert?>(tracked)
            : db.Alerts.FirstOrDefaultAsync(a => a.DeviceId == deviceId && a.Type == type && a.ResolvedAt == null, ct);
    }

    private static string FormatDuration(TimeSpan span) =>
        span.TotalHours >= 48 ? $"{(int)span.TotalDays} dagen"
        : span.TotalHours >= 1 ? $"{(int)span.TotalHours} uur"
        : $"{(int)span.TotalMinutes} minuten";

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500];
}
