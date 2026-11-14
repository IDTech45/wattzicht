using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Connectors;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Services;

/// <summary>
/// De geschiedenis van een Enphase-installatie ophalen.
///
/// Waarom dit bestaat: wij bouwden de dagen op uit onze eigen metingen, en die beginnen pas op het
/// moment dat iemand koppelt. Wie daarvoor al een jaar zonnepanelen had, keek dus naar een lege
/// grafiek terwijl zijn dak dat jaar gewoon gedraaid heeft. Enphase houdt die dagen bij en geeft ze
/// in één aanroep terug.
///
/// Eén keer per apparaat, de dag nadat hij gekoppeld is al genoeg: de dagen daarna vullen zich
/// vanzelf uit wat de omvormer dagelijks meldt. Daarom onthouden we per apparaat wanneer we het
/// gehaald hebben, en laten we het daarna met rust. Het developer-plan heeft een maandquotum, en
/// een ronde die elke start opnieuw de hele geschiedenis opvraagt is daar zo doorheen.
///
/// Dagen die wij al hebben blijven staan: onze eigen meting van vandaag is verser dan wat Enphase
/// vannacht heeft weggeschreven.
/// </summary>
public sealed class EnphaseGeschiedenisWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<EnphaseGeschiedenisWorker> logger) : BackgroundService
{
    /// <summary>Zo ver kijken we terug. Verder heeft niemand iets aan, en het is zo opgehaald.</summary>
    public static readonly int MaximaalDagen = 400;

    /// <summary>
    /// Hoeveel dagen we daarna elke dag opnieuw ophalen.
    ///
    /// Een week. Niet omdat er zo veel verandert, maar omdat er altijd iets mis kan gaan op een dag:
    /// een app die dicht bleef, een gateway die stilstond, een meting die niet aankwam. Eenmaal per
    /// dag een aanroep die de laatste week terugzet kost niets en haalt elk gaatje op.
    /// </summary>
    public const int VersDagen = 7;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(75), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Elk kwartier langskomen, maar bijna altijd meteen weer weggaan. Wie nog nooit is opgehaald
        // krijgt zijn geschiedenis zodra hij koppelt; de rest alleen in het avondvenster, als de dag
        // voorbij is. Zie AvondRonde voor waarom dat tien uur 's avonds is.
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        do
        {
            try
            {
                var dagen = await HaalAsync(stoppingToken);
                if (dagen > 0)
                {
                    logger.LogInformation("{Aantal} dag(en) opwek opgehaald bij Enphase", dagen);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Geschiedenis bij Enphase ophalen mislukt");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Eén ronde; geeft terug hoeveel dagen er zijn gevuld.</summary>
    internal async Task<int> HaalAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var registry = scope.ServiceProvider.GetRequiredService<ConnectorRegistry>();
        var protector = scope.ServiceProvider.GetRequiredService<CredentialProtector>();

        if (registry.Get(DeviceBrand.Enphase) is not EnphaseConnector enphase) return 0;

        var nu = clock.GetUtcNow();

        // Twee soorten werk, en ze hebben elk hun eigen moment.
        //
        // Wie nog nooit is opgehaald krijgt zijn hele geschiedenis, en die hoeft niet tot vanavond te
        // wachten: dat is het eerste wat iemand wil zien nadat hij gekoppeld heeft. Wie dat al had
        // krijgt de laatste week opnieuw, maar alleen in het avondvenster, als de dag van vandaag
        // voorbij is. Overdag ophalen geeft een dagtotaal dat een uur later alweer verouderd is.
        var alles = await db.Devices.IgnoreQueryFilters()
            .Where(d => d.Brand == DeviceBrand.Enphase)
            .ToListAsync(ct);

        var apparaten = alles
            .Where(d => d.PvHistorieOpgehaaldAt is null || AvondRonde.IsTijd(nu, d.PvHistorieOpgehaaldAt))
            .ToList();

        if (apparaten.Count == 0) return 0;

        var vandaag = DutchTime.LocalDate(nu);
        var gevuld = 0;

        foreach (var apparaat in apparaten)
        {
            try
            {
                var credentials = new DeviceCredentials(protector.Unprotect(apparaat.EncryptedCredentials));

                var eersteKeer = apparaat.PvHistorieOpgehaaldAt is null;
                var vanaf = eersteKeer
                    ? vandaag.AddDays(-MaximaalDagen)
                    : vandaag.AddDays(-VersDagen);

                var reeks = await enphase.DagelijkseOpwekAsync(apparaat, credentials, vanaf, ct);

                // Ook zonder reeks de datum zetten: een apparaat zonder cloudkoppeling hoeft niet bij
                // elke herstart opnieuw geprobeerd te worden.
                apparaat.PvHistorieOpgehaaldAt = nu;

                if (credentials.Changed)
                {
                    apparaat.EncryptedCredentials = protector.Protect(credentials.ToDictionary());
                }

                if (reeks.Count == 0) continue;

                var bestaand = await db.DailySummaries.IgnoreQueryFilters()
                    .Where(s => s.HouseholdId == apparaat.HouseholdId)
                    .ToDictionaryAsync(s => s.Date, ct);

                foreach (var (dag, kwh) in reeks)
                {
                    if (kwh <= 0 || kwh > SummaryService.MaximumDagKwh) continue;

                    if (bestaand.TryGetValue(dag, out var rij))
                    {
                        // Vandaag laten we met rust: onze eigen meting loopt nog en die van Enphase
                        // is van het laatste moment dat hun cloud iets binnenkreeg.
                        if (dag == vandaag) continue;

                        // Een dag die al een getal heeft blijft staan, tenzij Enphase meer weet. Een
                        // dag waarop onze metingen half zijn binnengekomen staat te laag, en dan is
                        // het antwoord van de omvormer zelf het betere.
                        if (rij.PvKwh >= kwh) continue;

                        rij.PvKwh = kwh;
                        rij.UpdatedAt = nu;
                        gevuld++;
                        continue;
                    }

                    db.DailySummaries.Add(new DailySummary
                    {
                        Id = Guid.NewGuid(),
                        HouseholdId = apparaat.HouseholdId,
                        Date = dag,
                        PvKwh = kwh,
                        UpdatedAt = nu,
                    });
                    gevuld++;
                }
            }
            catch (Exception ex)
            {
                // Eén apparaat dat niet meewerkt mag de rest niet tegenhouden. En geen datum zetten:
                // dit was een storing en geen antwoord, dus volgende keer opnieuw proberen.
                logger.LogWarning(ex, "Geschiedenis van apparaat {Id} kon niet worden opgehaald", apparaat.Id);
            }
        }

        await db.SaveChangesAsync(ct);
        return gevuld;
    }
}
