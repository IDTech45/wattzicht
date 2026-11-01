using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Connectors;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

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

    // Eén uitleesronde tegelijk per huishouden: "opnieuw uitlezen" vanuit het portal mag niet samenvallen met de
    // gewone pollronde (dubbele dagtotalen of meldingen).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, SemaphoreSlim> HouseholdLocks = new();

    /// <summary>
    /// Wanneer we een apparaat voor het laatst probeerden uit te lezen. Alleen in het geheugen: na een
    /// herstart leest elk apparaat één keer extra uit, en dat is goedkoper dan een kolom erbij.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, DateTimeOffset> LaatstePoging = new();

    /// <summary>
    /// Het kortste interval per merk. De poller draait elke vijf minuten, maar niet elk merk mag dat.
    ///
    /// Enphase staat op het gratis plan "Watt" met 1.000 verzoeken per maand. Elke vijf minuten uitlezen
    /// is 8.640 verzoeken per maand: dan is het quotum na drie dagen op en stopt ook de Enphase-koppeling
    /// van LaadCash, want die hangt aan hetzelfde developer-account. Eén keer per uur past wel.
    /// </summary>
    private static TimeSpan KortsteInterval(DeviceBrand brand) => brand switch
    {
        DeviceBrand.Enphase => TimeSpan.FromMinutes(60),
        // Elke uitlezing is een login plus een state-aanroep, en Zaptec knijpt af op ons IP-adres en
        // niet op het account. Tien minuten is voor een laadpaal ruim genoeg: een laadbeurt duurt uren.
        DeviceBrand.Zaptec => TimeSpan.FromMinutes(10),
        _ => TimeSpan.Zero,
    };

    private bool NogTeVroeg(Device device, out DateTimeOffset wachtTot)
    {
        wachtTot = default;
        var minimum = KortsteInterval(device.Brand);
        if (minimum == TimeSpan.Zero) return false;
        if (!LaatstePoging.TryGetValue(device.Id, out var vorige)) return false;

        wachtTot = vorige + minimum;
        return clock.GetUtcNow() < wachtTot;
    }

    /// <summary>Eén huishouden nu uitlezen (ook gebruikt door "opnieuw uitlezen" in het installateursportal).</summary>
    public async Task<(int Ok, int Fail)> PollHouseholdAsync(Guid householdId, CancellationToken ct)
    {
        var gate = HouseholdLocks.GetOrAdd(householdId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            return await PollHouseholdCoreAsync(householdId, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<(int Ok, int Fail)> PollHouseholdCoreAsync(Guid householdId, CancellationToken ct)
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

            if (NogTeVroeg(device, out var wachtTot))
            {
                // Dit merk mag niet zo vaak. Overslaan is hier geen fout: het vorige antwoord staat nog
                // in de database en de klant ziet gewoon de laatste meting.
                logger.LogDebug("Apparaat {DeviceId} ({Brand}) overgeslagen tot {WachtTot:HH:mm}", device.Id, device.Brand, wachtTot);
                continue;
            }

            LaatstePoging[device.Id] = clock.GetUtcNow();

            try
            {
                var credentials = new DeviceCredentials(protector.Unprotect(device.EncryptedCredentials));

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));

                var reading = await connector.ReadAsync(device, credentials, timeout.Token);

                // Heeft het merk onderweg een nieuwe sleutel gegeven, dan moet die bewaard worden,
                // anders werkt de koppeling na deze ronde niet meer (Enphase doet dit bij elke vernieuwing).
                if (credentials.Changed)
                {
                    device.EncryptedCredentials = protector.Protect(credentials.ToDictionary());
                    logger.LogInformation("Vernieuwde sleutel bewaard voor apparaat {DeviceId} ({Brand})", device.Id, device.Brand);
                }

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

        LiveSnapshotDto? snapshotForPush = null;
        if (latest.Count > 0)
        {
            var snapshot = SnapshotBuilder.Build(latest);
            await summaries.ApplySnapshotAsync(householdId, snapshot, ct);
            snapshotForPush = snapshot;
        }

        await db.SaveChangesAsync(ct);

        // Pushmeldingen pas na het opslaan: een mislukte melding mag de meting niet kosten.
        try
        {
            await SendPushesAsync(scope.ServiceProvider, db, householdId, devices, alerts.Opened, snapshotForPush, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Ook een time-out (TaskCanceledException) mag de pollronde niet stoppen.
            logger.LogWarning(ex, "Pushmeldingen voor huishouden {HouseholdId} mislukt", householdId);
        }

        return (ok, fail);
    }

    private async Task SendPushesAsync(
        IServiceProvider services,
        AppDbContext db,
        Guid householdId,
        List<Device> devices,
        IReadOnlyList<Alert> openedAlerts,
        LiveSnapshotDto? snapshot,
        CancellationToken ct)
    {
        var push = services.GetRequiredService<IPushSender>();
        if (!push.Enabled)
        {
            return;
        }

        var household = await db.Households.FirstOrDefaultAsync(h => h.Id == householdId, ct);
        if (household is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        if (openedAlerts.Count > 0 && PushPolicy.MaySendAlert(household, now))
        {
            var names = devices.ToDictionary(d => d.Id, d => d.Name);
            var (title, body) = PushPolicy.AlertMessage(openedAlerts, names);
            await push.SendToHouseholdAsync(householdId, title, body, "alerts", ct);
            household.LastAlertPushAt = now;
            await db.SaveChangesAsync(ct);
        }

        if (snapshot is not null
            && devices.Any(d => d.Type == DeviceType.Inverter)
            && PushPolicy.MaySendSurplus(household, now))
        {
            var summaries = services.GetRequiredService<SummaryService>();
            var rates = await summaries.GetRatesAsync(householdId, DutchTime.LocalDate(now), ct);
            var tip = FeedInCalculator.LiveSurplusTip(snapshot, now, devices.Any(d => d.Type == DeviceType.Charger), rates);
            if (tip is not null)
            {
                var body = tip.Note is null ? tip.Detail : $"{tip.Detail.TrimEnd('.')}. {tip.Note.TrimEnd('.')}.";
                await push.SendToHouseholdAsync(householdId, tip.Title, body, "advice", ct);
                household.LastSurplusPushAt = now;
                await db.SaveChangesAsync(ct);
            }
        }
    }
}
