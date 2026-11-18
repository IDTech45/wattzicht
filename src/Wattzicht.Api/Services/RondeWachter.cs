namespace Wattzicht.Api.Services;

/// <summary>
/// Slaat alarm als de uitleesronde stilvalt.
///
/// Staat bewust los van <see cref="PollingWorker"/>. Een ronde die zichzelf bewaakt meldt niets meer
/// zodra hij zelf niet meer start, en precies dat is bij LaadCash gebeurd: de planner kon de taak
/// niet aanmaken en het bleef wekenlang stil. Deze wachter kijkt alleen naar de klok en naar de
/// hartslag, dus hij blijft praten ook als de ronde helemaal weg is.
///
/// Wat "alarm" betekent, hangt ervan af wat er is ingericht:
/// - er gaat een foutregel in het log (die vangt Sentry op als de DSN er staat);
/// - en als <c>Monitoring:RondeHeartbeatUrl</c> is ingevuld, wordt die URL na elke gezonde ronde
///   aangeroepen. Blijft die ping uit, dan stuurt healthchecks.io zelf een mail. Dat is de enige
///   manier om te horen dat er níets gebeurt: een server die plat ligt mailt ook niet meer.
/// </summary>
public sealed class RondeWachter(
    Hartslag hartslag,
    IHttpClientFactory httpFactory,
    IConfiguration configuration,
    ILogger<RondeWachter> logger) : BackgroundService
{
    public const string HttpClientName = "rondewachter";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Polling:Enabled", true))
        {
            logger.LogInformation("Rondewachter blijft stil: de uitleesronde staat uit.");
            return;
        }

        var interval = TimeSpan.FromMinutes(configuration.GetValue("Monitoring:WachterIntervalMinuten", 10));
        // Standaard zes keer het pollinterval van vijf minuten. Eén gemiste ronde is ruis (een trage
        // leverancier, een herstart); een half uur stilte is een storing.
        var grens = TimeSpan.FromMinutes(configuration.GetValue("Monitoring:StilteGrensMinuten", 30));
        var pingUrl = configuration["Monitoring:RondeHeartbeatUrl"];

        // Niet meteen klagen na het starten: migraties, seed en de eerste ronde hebben even nodig.
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var meldde = false;
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                var stand = hartslag.Stand();
                if (stand.Stilte > grens)
                {
                    // Eén keer melden per storing, niet elke tien minuten opnieuw: een alarm dat
                    // blijft herhalen wordt een alarm dat niemand meer leest.
                    if (!meldde)
                    {
                        logger.LogError(
                            "De uitleesronde staat stil: {Minuten:F0} minuten geen ronde afgerond (grens {Grens:F0}). "
                            + "Laatste ronde: {Laatste}.",
                            stand.Stilte.TotalMinutes, grens.TotalMinutes,
                            stand.LaatsteRonde?.ToString("u") ?? "nog nooit sinds het starten");
                        meldde = true;
                    }
                }
                else
                {
                    if (meldde)
                    {
                        logger.LogInformation("De uitleesronde draait weer: laatste ronde {Laatste}.",
                            stand.LaatsteRonde?.ToString("u"));
                        meldde = false;
                    }

                    await PingAsync(pingUrl, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Rondewachter kon zijn controle niet doen.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// Laat de buitenwereld weten dat het goed gaat. Mislukt die ping, dan is dat geen storing van
    /// ons: dan is de bewakingsdienst even weg, en daar hoeft niemand wakker van te liggen.
    /// </summary>
    private async Task PingAsync(string? url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            var client = httpFactory.CreateClient(HttpClientName);
            using var antwoord = await client.GetAsync(url, ct);
            if (!antwoord.IsSuccessStatusCode)
                logger.LogWarning("Hartslag-ping gaf {Status}.", (int)antwoord.StatusCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Hartslag-ping kon niet verstuurd worden.");
        }
    }
}
