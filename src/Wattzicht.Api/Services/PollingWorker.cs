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
        EnphaseMaandbudget = Math.Max(1, configuration.GetValue("Polling:EnphaseMaandbudget", 100));

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
            // Hoe meer Enphase-huizen, hoe rustiger elk van hen aan de beurt komt. Hier tellen en niet
            // per apparaat opzoeken: het is één telling per ronde in plaats van één per koppeling.
            _enphaseApparaten = await db.Devices.CountAsync(d => d.Brand == DeviceBrand.Enphase, ct);
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
    /// Hoeveel Enphase-apparaten er op dit moment gekoppeld zijn. Bepaalt hoe vaak elk van hen aan de
    /// beurt komt; wordt per pollronde bijgewerkt.
    /// </summary>
    private static int _enphaseApparaten = 1;

    /// <summary>
    /// Het kortste interval per merk. De poller draait elke vijf minuten, maar niet elk merk mag dat.
    ///
    /// Enphase staat op het gratis plan "Watt": 1.000 verzoeken per maand, gedeeld met LaadCash want
    /// dat hangt aan hetzelfde developer-account. Hier stond een vast uur, en dat leek genoeg tot je
    /// het uitrekent: 24 maal 30 is 720 verzoeken per maand voor ÉÉN huis. Het tweede huis trekt het
    /// quotum er dus doorheen en neemt de Enphase-koppeling van LaadCash mee in de val.
    ///
    /// Daarom twee dingen. We lezen alleen overdag uit, want 's nachts maakt een zonnepaneel niets en
    /// verandert er niets aan de teller. En we delen wat er overblijft door het aantal huizen, zodat
    /// het tiende huis het eerste niet omver duwt. Bij één huis komt daar ongeveer een uur uit, bij
    /// vier huizen ruim twee uur, en dat is voor een opbrengstgrafiek nog steeds ruim genoeg.
    /// </summary>
    internal static TimeSpan KortsteInterval(DeviceBrand brand, int enphaseApparaten = 1) => brand switch
    {
        DeviceBrand.Enphase => EnphaseInterval(enphaseApparaten),
        // Elke uitlezing is een login plus een state-aanroep, en Zaptec knijpt af op ons IP-adres en
        // niet op het account. Tien minuten is voor een laadpaal ruim genoeg: een laadbeurt duurt uren.
        DeviceBrand.Zaptec => TimeSpan.FromMinutes(10),
        _ => TimeSpan.Zero,
    };

    /// <summary>Het aantal uren per etmaal dat we Enphase uitlezen (zie <see cref="InDeUitleesuren"/>).</summary>
    internal const int EnphaseUrenPerDag = EnphaseLaatsteUur - EnphaseEersteUur + 1;
    private const int EnphaseEersteUur = 6;
    private const int EnphaseLaatsteUur = 21;

    /// <summary>
    /// Wat wij van de maandlimiet voor onszelf houden. Het gratis plan geeft er 1.000, maar dat is
    /// gedeeld met LaadCash en niemand wil erachter komen dat het op is doordat een klant belt. Honderd
    /// is bewust ruim onder de grens: genoeg om een opbrengstgrafiek te vullen, te weinig om ooit een
    /// rekening te veroorzaken. Te verhogen met Polling:EnphaseMaandbudget als de cloud ooit de
    /// hoofdweg wordt in plaats van de achterdeur.
    /// </summary>
    internal static int EnphaseMaandbudget { get; private set; } = 100;

    internal static TimeSpan EnphaseInterval(int apparaten)
    {
        var huizen = Math.Max(1, apparaten);
        // Verzoeken die één huis per maand mag doen, omgerekend naar een pauze tussen twee uitlezingen.
        var perHuisPerMaand = (double)EnphaseMaandbudget / huizen;
        var uitleesurenPerMaand = EnphaseUrenPerDag * 30.0;
        var minuten = uitleesurenPerMaand * 60.0 / Math.Max(1.0, perHuisPerMaand);
        // Nooit vaker dan een keer per uur, ook niet als er maar één huis is: zo vaak verandert een
        // dagopbrengst niet, en er moet ruimte blijven voor een handmatige "opnieuw uitlezen".
        return TimeSpan.FromMinutes(Math.Max(60, minuten));
    }

    /// <summary>
    /// Is dit een uur waarop we Enphase uitlezen? Tussen tien voor tien 's avonds en zes uur
    /// 's ochtends staat de zon niet op en blijft de teller staan, dus daar is geen verzoek voor nodig.
    /// Nederlandse tijd, want daar wonen de klanten en daar hoort de dagindeling bij.
    /// </summary>
    internal static bool InDeUitleesuren(DateTimeOffset nu)
    {
        var uur = TimeZoneInfo.ConvertTime(nu, Tijdzone).Hour;
        return uur is >= EnphaseEersteUur and <= EnphaseLaatsteUur;
    }

    private static readonly TimeZoneInfo Tijdzone = Zoek("Europe/Amsterdam", "W. Europe Standard Time");

    private static TimeZoneInfo Zoek(params string[] namen)
    {
        foreach (var naam in namen)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(naam); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }

    private bool NogTeVroeg(Device device, out DateTimeOffset wachtTot)
    {
        wachtTot = default;
        var nu = clock.GetUtcNow();

        if (device.Brand == DeviceBrand.Enphase && !InDeUitleesuren(nu))
        {
            wachtTot = nu.AddHours(1);
            return true;
        }

        var minimum = KortsteInterval(device.Brand, _enphaseApparaten);
        if (minimum == TimeSpan.Zero) return false;
        if (!LaatstePoging.TryGetValue(device.Id, out var vorige)) return false;

        wachtTot = vorige + minimum;
        return nu < wachtTot;
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

            if (connector.IsLokaal)
            {
                // Een P1-meter praat alleen op het netwerk van de bewoner; de app stuurt die metingen
                // door. Hier niets doen en vooral geen fout melden: er is niets mis.
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

                // Sommige merken kunnen het allebei: een Sessy op 192.168.1.50 hoort bij de app, een
                // Sessy achter een opengezette poort bij ons. Dat is pas te zien als de gegevens
                // ontsleuteld zijn, vandaar hier en niet bij de controle hierboven.
                if (connector.IsLokaalVoor(credentials))
                {
                    continue;
                }

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
            var snapshot = SnapshotBuilder.Build(latest, devices.Select(d => d.Type));
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
            var (title, body) = PushPolicy.AlertMessage(openedAlerts, names, household.Taal);
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
