using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Services;

/// <summary>Leesquery's die door meerdere endpoints gebruikt worden (dashboard, installateursportal).</summary>
public sealed class HouseholdQueries(AppDbContext db, TimeProvider clock)
{
    /// <summary>Laatste meting per apparaat van een huishouden.</summary>
    public async Task<List<Reading>> LatestReadingsAsync(Guid householdId, CancellationToken ct)
    {
        var deviceIds = await db.Devices
            .Where(d => d.HouseholdId == householdId)
            .Select(d => d.Id)
            .ToListAsync(ct);

        var result = new List<Reading>(deviceIds.Count);
        foreach (var deviceId in deviceIds)
        {
            var reading = await db.Readings
                .Where(r => r.DeviceId == deviceId)
                .OrderByDescending(r => r.Timestamp)
                .FirstOrDefaultAsync(ct);
            if (reading is not null)
            {
                result.Add(reading);
            }
        }

        return result;
    }

    public async Task<LiveSnapshotDto> LiveAsync(Guid householdId, CancellationToken ct) =>
        SnapshotBuilder.Build(await LatestReadingsAsync(householdId, ct));

    /// <summary>Dagsamenvattingen in een bereik (inclusief), oplopend op datum.</summary>
    public async Task<List<DailySummaryDto>> SummariesAsync(Guid householdId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var rows = await db.DailySummaries
            .Where(s => s.HouseholdId == householdId && s.Date >= from && s.Date <= to)
            .OrderBy(s => s.Date)
            .ToListAsync(ct);

        return rows.Select(ToDto).ToList();
    }

    /// <summary>Telt een bereik op tot één samenvatting; <paramref name="label"/> wordt de datum van het resultaat.</summary>
    public async Task<DailySummaryDto> AggregateAsync(Guid householdId, DateOnly from, DateOnly to, DateOnly label, CancellationToken ct)
    {
        var rows = await db.DailySummaries
            .Where(s => s.HouseholdId == householdId && s.Date >= from && s.Date <= to)
            .ToListAsync(ct);

        return new DailySummaryDto(
            label,
            Math.Round(rows.Sum(r => r.PvKwh), 2),
            Math.Round(rows.Sum(r => r.SelfUseKwh), 2),
            Math.Round(rows.Sum(r => r.ExportKwh), 2),
            Math.Round(rows.Sum(r => r.ImportKwh), 2),
            Math.Round(rows.Sum(r => r.ChargedKwh), 2),
            Math.Round(rows.Sum(r => r.BatteryChargedKwh), 2),
            Math.Round(rows.Sum(r => r.BatteryDischargedKwh), 2),
            Math.Round(rows.Sum(r => r.SavedEur), 2));
    }

    public async Task<List<AlertDto>> OpenAlertsAsync(Guid householdId, CancellationToken ct)
    {
        return await db.Alerts
            .Where(a => a.Device.HouseholdId == householdId && a.ResolvedAt == null)
            .OrderByDescending(a => a.Since)
            .Select(a => new AlertDto(a.Id, a.DeviceId, a.Device.Name, a.Type, a.Message, a.Since, a.ResolvedAt))
            .ToListAsync(ct);
    }

    public async Task<List<DeviceDto>> DevicesAsync(Guid householdId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var devices = await db.Devices
            .Where(d => d.HouseholdId == householdId)
            .OrderBy(d => d.CreatedAt)
            .ToListAsync(ct);

        return devices.Select(d => ToDto(d, now)).ToList();
    }

    public async Task<DashboardDto> DashboardAsync(Guid householdId, CancellationToken ct)
    {
        var today = DutchTime.LocalDate(clock.GetUtcNow());
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var yearStart = new DateOnly(today.Year, 1, 1);

        var live = await LiveAsync(householdId, ct);
        var todaySummary = await AggregateAsync(householdId, today, today, today, ct);
        var month = await AggregateAsync(householdId, monthStart, today, monthStart, ct);
        var year = await AggregateAsync(householdId, yearStart, today, yearStart, ct);
        var alerts = await OpenAlertsAsync(householdId, ct);

        return new DashboardDto(live, todaySummary, month, year, alerts);
    }

    /// <summary>Status zoals de installateur die ziet.</summary>
    public static HouseholdStatus DetermineStatus(int deviceCount, int openAlerts, DateTimeOffset? lastSeen, DateTimeOffset now)
    {
        if (deviceCount == 0 || lastSeen is null || now - lastSeen > TimeSpan.FromHours(24))
        {
            return HouseholdStatus.NoData;
        }

        return openAlerts > 0 ? HouseholdStatus.Fault : HouseholdStatus.Online;
    }

    /// <summary>Alle meldingen van de laatste <paramref name="days"/> dagen (open en opgelost), nieuwste eerst.</summary>
    public async Task<List<AlertDto>> RecentAlertsAsync(Guid householdId, int days, CancellationToken ct)
    {
        var since = clock.GetUtcNow().AddDays(-days);
        return await db.Alerts
            .Where(a => a.Device.HouseholdId == householdId && (a.ResolvedAt == null || a.Since >= since))
            .OrderByDescending(a => a.Since)
            .Take(200)
            .Select(a => new AlertDto(a.Id, a.DeviceId, a.Device.Name, a.Type, a.Message, a.Since, a.ResolvedAt))
            .ToListAsync(ct);
    }

    public static DailySummaryDto ToDto(DailySummary s) => new(
        s.Date,
        Math.Round(s.PvKwh, 2),
        Math.Round(s.SelfUseKwh, 2),
        Math.Round(s.ExportKwh, 2),
        Math.Round(s.ImportKwh, 2),
        Math.Round(s.ChargedKwh, 2),
        Math.Round(s.BatteryChargedKwh, 2),
        Math.Round(s.BatteryDischargedKwh, 2),
        Math.Round(s.SavedEur, 2));

    public static DeviceDto ToDto(Device d, DateTimeOffset now) => new(
        d.Id,
        d.Type,
        d.Brand,
        d.Name,
        d.ExternalId,
        d.LastSeenAt,
        d.LastSeenAt is { } seen && now - seen <= AlertService.NoDataThreshold);

    public static HouseholdDto ToDto(Household h) => new(
        h.Id,
        h.Name,
        h.PostalCode,
        h.City,
        h.InstallerId,
        h.Installer.Name,
        h.CreatedAt);

    public static InstallerBrandingDto ToDto(Installer i) => new(
        i.Id,
        i.Name,
        i.Slug,
        i.LogoUrl,
        i.PrimaryColor,
        i.AccentColor,
        i.QuoteUrl,
        i.Phone,
        i.ContactEmail);

    public static BrandingDto ToBrandingDto(Installer i) => new(
        i.Name,
        i.LogoUrl,
        i.PrimaryColor,
        i.QuoteUrl,
        i.AccentColor);
}
