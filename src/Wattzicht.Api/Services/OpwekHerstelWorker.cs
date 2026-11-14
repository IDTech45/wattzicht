using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;

namespace Wattzicht.Api.Services;

/// <summary>
/// De dagen die nooit zijn ingevuld alsnog invullen.
///
/// De dagopbrengst werd opgeteld uit losse vermogensmetingen. Bij een kastje dat de app zelf
/// uitleest komt er alleen een meting binnen als iemand thuis is en de app openmaakt, en bovendien
/// werkte die optelling helemaal niet op die route. Gevolg: bewoners met een Enphase, HomeWizard,
/// Sessy, Alfen of Peblar zagen maandenlang overal nul, terwijl hun panelen gewoon draaiden.
///
/// De metingen zelf zijn er wel, met de tellerstand van de omvormer erbij. Daaruit is per dag
/// precies uit te rekenen wat er is opgewekt: de stand aan het eind van de dag min die van de dag
/// ervoor.
///
/// Belangrijk, en hier ging het de eerste keer mis: er stonden voor die woningen vaak helemaal geen
/// dagen in de database. Een ronde die alleen bestaande nullen invult vindt dan niets en doet niets.
/// Daarom begint deze ronde bij de metingen en maakt hij de ontbrekende dagen zelf aan.
///
/// Een dag waar al een getal staat blijft zoals hij is. Dan is er iets wat we niet van een teller
/// weten, en dat hoort niet overschreven te worden door een berekening achteraf.
/// </summary>
public sealed class OpwekHerstelWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<OpwekHerstelWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Even wachten tot de database klaar is met opstarten en migreren.
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            var hersteld = await HerstelAsync(stoppingToken);

            if (hersteld > 0)
            {
                logger.LogInformation("{Aantal} dag(en) opwek alsnog ingevuld uit de tellerstanden", hersteld);
            }
        }
        catch (OperationCanceledException)
        {
            // Afsluiten tijdens het herstellen is geen fout; de volgende start pakt het weer op.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Opwek herstellen mislukt");
        }
    }

    /// <summary>Eén ronde; geeft terug hoeveel dagen er zijn ingevuld.</summary>
    internal async Task<int> HerstelAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Begin bij de metingen: welke woningen hebben überhaupt een tellerstand doorgegeven?
        var woningen = await db.Readings.IgnoreQueryFilters()
            .Where(r => r.PvEnergyWh != null && r.PvEnergyWh > 0)
            .Select(r => r.Device!.HouseholdId)
            .Distinct()
            .ToListAsync(ct);

        if (woningen.Count == 0) return 0;

        var nu = clock.GetUtcNow();
        var ingevuld = 0;

        foreach (var woning in woningen)
        {
            var perDag = await DagenUitTellersAsync(db, woning, ct);
            if (perDag.Count == 0) continue;

            var bestaand = await db.DailySummaries.IgnoreQueryFilters()
                .Where(s => s.HouseholdId == woning)
                .ToDictionaryAsync(s => s.Date, ct);

            // Dagen die er al staan maar nergens op slaan, eerst opruimen. Een tellerwissel kon
            // eerder een hele levensopbrengst als dagopbrengst opleveren, en zo'n getal blijft staan
            // tot iemand het weghaalt.
            foreach (var rij in bestaand.Values.Where(r => r.PvKwh > SummaryService.MaximumDagKwh))
            {
                rij.PvKwh = 0;
                rij.UpdatedAt = nu;
                ingevuld++;
            }

            foreach (var (dag, kwh) in perDag)
            {
                if (kwh <= 0 || kwh > SummaryService.MaximumDagKwh) continue;

                if (bestaand.TryGetValue(dag, out var rij))
                {
                    // Staat er al een getal, dan laten we het staan.
                    if (rij.PvKwh > 0) continue;

                    rij.PvKwh = Math.Round(kwh, 3);
                    rij.UpdatedAt = nu;
                    ingevuld++;
                    continue;
                }

                db.DailySummaries.Add(new DailySummary
                {
                    Id = Guid.NewGuid(),
                    HouseholdId = woning,
                    Date = dag,
                    PvKwh = Math.Round(kwh, 3),
                    UpdatedAt = nu,
                });
                ingevuld++;
            }
        }

        if (ingevuld > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return ingevuld;
    }

    /// <summary>
    /// Per dag de opwek van één woning, uit de tellerstanden in de metingen.
    ///
    /// Per apparaat de laatste stand van elke dag, en dan het verschil met de laatste stand van de
    /// dag ervoor. Is er geen dag ervoor, dan nemen we binnen die dag het verschil tussen de eerste
    /// en de laatste meting; dat is een ondergrens en geen schatting.
    /// </summary>
    private static async Task<Dictionary<DateOnly, double>> DagenUitTellersAsync(
        AppDbContext db, Guid woning, CancellationToken ct)
    {
        var metingen = await db.Readings.IgnoreQueryFilters()
            .Where(r => r.Device!.HouseholdId == woning && r.PvEnergyWh != null && r.PvEnergyWh > 0)
            .OrderBy(r => r.Timestamp)
            .Select(r => new { r.DeviceId, r.Timestamp, Teller = r.PvEnergyWh!.Value })
            .ToListAsync(ct);

        var uit = new Dictionary<DateOnly, double>();

        foreach (var perApparaat in metingen.GroupBy(m => m.DeviceId))
        {
            var dagen = perApparaat
                .GroupBy(m => DutchTime.LocalDate(m.Timestamp))
                .OrderBy(g => g.Key)
                .Select(g => new
                {
                    Dag = g.Key,
                    Eerste = g.Min(x => x.Teller),
                    Laatste = g.Max(x => x.Teller),
                })
                .ToList();

            double? vorigeStand = null;

            foreach (var dag in dagen)
            {
                var begin = vorigeStand is { } v && v <= dag.Eerste ? v : dag.Eerste;
                var opwek = dag.Laatste - begin;
                vorigeStand = dag.Laatste;

                if (opwek <= 0) continue;

                uit[dag.Dag] = uit.GetValueOrDefault(dag.Dag) + opwek / 1000d;
            }
        }

        return uit;
    }
}
