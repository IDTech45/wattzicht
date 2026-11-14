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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(75), stoppingToken);
            var dagen = await HaalAsync(stoppingToken);

            if (dagen > 0)
            {
                logger.LogInformation("{Aantal} dag(en) opwek opgehaald bij Enphase", dagen);
            }
        }
        catch (OperationCanceledException)
        {
            // Afsluiten tijdens het ophalen is geen fout.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Geschiedenis bij Enphase ophalen mislukt");
        }
    }

    /// <summary>Eén ronde; geeft terug hoeveel dagen er zijn gevuld.</summary>
    internal async Task<int> HaalAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var registry = scope.ServiceProvider.GetRequiredService<ConnectorRegistry>();
        var protector = scope.ServiceProvider.GetRequiredService<CredentialProtector>();

        if (registry.Get(DeviceBrand.Enphase) is not EnphaseConnector enphase) return 0;

        var apparaten = await db.Devices.IgnoreQueryFilters()
            .Where(d => d.Brand == DeviceBrand.Enphase && d.PvHistorieOpgehaaldAt == null)
            .ToListAsync(ct);

        if (apparaten.Count == 0) return 0;

        var nu = clock.GetUtcNow();
        var vanaf = DutchTime.LocalDate(nu).AddDays(-MaximaalDagen);
        var gevuld = 0;

        foreach (var apparaat in apparaten)
        {
            try
            {
                var credentials = new DeviceCredentials(protector.Unprotect(apparaat.EncryptedCredentials));
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
                        // Wat wij zelf gemeten hebben blijft staan; dat is verser.
                        if (rij.PvKwh > 0) continue;

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
