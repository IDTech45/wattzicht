using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Connectors;
using Wattzicht.Api.Data;

namespace Wattzicht.Api.Services;

/// <summary>
/// Leest elke 5 minuten alle apparaten uit via hun connector, slaat een <see cref="Reading"/> op,
/// werkt de <see cref="DailySummary"/> van elk huishouden bij en zet/sluit meldingen.
/// Huishoudens worden één voor één verwerkt zodat één kapotte koppeling de rest niet ophoudt.
/// </summary>
public sealed class PollingWorker(
    IServiceScopeFactory scopeFactory,
    ConnectorRegistry registry,
    TimeProvider clock,
    IConfiguration configuration,
    ILogger<PollingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(configuration.GetValue("Polling:IntervalMinutes", 5));
        var enabled = configuration.GetValue("Polling:Enabled", true);

        if (!enabled)
        {
            logger.LogInformation("Polling staat uit (Polling:Enabled=false)");
            return;
        }

        // Korte startvertraging zodat migraties en seed klaar zijn.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await PollAllAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Pollronde mislukt");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Eén volledige ronde over alle huishoudens. Intern zodat tests hem direct kunnen aanroepen.</summary>
    internal async Task PollAllAsync(CancellationToken ct)
    {
        var started = clock.GetUtcNow();

        List<Guid> householdIds;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            householdIds = await db.Households
                .Where(h => h.Devices.Any())
                .Select(h => h.Id)
                .ToListAsync(ct);
        }

        var okCount = 0;
        var failCount = 0;

        foreach (var householdId in householdIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var (ok, fail) = await PollHouseholdAsync(householdId, ct);
                okCount += ok;
                failCount += fail;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Huishouden {HouseholdId} kon niet verwerkt worden", householdId);
            }
        }

        logger.LogInformation(
            "Pollronde klaar in {Duration:F1}s: {Households} huishoudens, {Ok} apparaten ok, {Fail} mislukt",
            (clock.GetUtcNow() - started).TotalSeconds, householdIds.Count, okCount, failCount);
    }

    private async Task<(int Ok, int Fail)> PollHouseholdAsync(Guid householdId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<CredentialProtector>();
        var alerts = scope.ServiceProvider.GetRequiredService<AlertService>();
        var summaries = scope.ServiceProvider.GetRequiredService<SummaryService>();

        var devices = await db.Devices
            .Where(d => d.HouseholdId == householdId)
            .ToListAsync(ct);

        var latest = new List<Reading>(devices.Count);
        var ok = 0;
        var fail = 0;

        foreach (var device in devices)
        {
            var connector = registry.Get(device.Brand);
            if (connector is null)
            {
                device.LastError = $"Merk {device.Brand} wordt nog niet ondersteund.";
                await alerts.SetOfflineAsync(device, device.LastError, ct);
                fail++;
                continue;
            }

            try
            {
                var credentials = protector.Unprotect(device.EncryptedCredentials);

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));

                var reading = await connector.ReadAsync(device, credentials, timeout.Token);
                var entity = reading.ToEntity(device.Id);

                db.Readings.Add(entity);
                latest.Add(entity);

                device.LastSeenAt = entity.Timestamp;
                device.LastError = null;
                ok++;

                await alerts.SetOfflineAsync(device, null, ct);
                await alerts.CheckZeroProductionAsync(device, entity, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var message = ex is ConnectorException ? ex.Message : "Onbekende fout bij uitlezen.";
                logger.LogWarning(ex, "Uitlezen van apparaat {DeviceId} ({Brand}) mislukt", device.Id, device.Brand);
                device.LastError = message;
                fail++;
                await alerts.SetOfflineAsync(device, message, ct);
            }

            await alerts.CheckNoDataAsync(device, ct);
        }

        if (latest.Count > 0)
        {
            var snapshot = SnapshotBuilder.Build(latest);
            await summaries.ApplySnapshotAsync(householdId, snapshot, ct);
        }

        await db.SaveChangesAsync(ct);
        return (ok, fail);
    }
}
