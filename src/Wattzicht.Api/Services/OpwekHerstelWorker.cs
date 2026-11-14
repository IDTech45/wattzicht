using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;

namespace Wattzicht.Api.Services;

/// <summary>
/// De dagen die op nul bleven staan alsnog invullen.
///
/// De dagopbrengst werd opgeteld uit losse vermogensmetingen. Bij een kastje dat de app zelf
/// uitleest komt er alleen een meting binnen als iemand thuis is en de app openmaakt, en bovendien
/// werkte die optelling helemaal niet op die route. Gevolg: bewoners met een Enphase, HomeWizard,
/// Sessy, Alfen of Peblar zagen maandenlang overal nul staan, terwijl hun panelen gewoon draaiden.
///
/// De metingen zelf zijn er wel, met de tellerstand van de omvormer erbij. Daaruit is per dag
/// precies uit te rekenen wat er is opgewekt: de stand aan het eind van de dag min die van de dag
/// ervoor. Deze eenmalige ronde doet dat voor elke dag die nu op nul staat.
///
/// Alleen nullen worden ingevuld. Een dag waar al een getal staat blijft zoals hij is, want dan is
/// er iets wat we niet van een teller weten en dat hoort niet overschreven te worden door een
/// berekening achteraf.
/// </summary>
public sealed class OpwekHerstelWorker(
    IServiceScopeFactory scopeFactory,
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

        // Alleen woningen waar iets te herstellen valt: een dag op nul.
        var legeDagen = await db.DailySummaries.IgnoreQueryFilters()
            .Where(s => s.PvKwh == 0)
            .Select(s => new { s.HouseholdId, s.Date })
            .ToListAsync(ct);

        if (legeDagen.Count == 0) return 0;

        var woningen = legeDagen.Select(d => d.HouseholdId).Distinct().ToList();
        var ingevuld = 0;

        foreach (var woning in woningen)
        {
            var perDag = await DagenUitTellersAsync(db, woning, ct);
            if (perDag.Count == 0) continue;

            var dagen = legeDagen.Where(d => d.HouseholdId == woning).Select(d => d.Date).ToHashSet();

            var rijen = await db.DailySummaries.IgnoreQueryFilters()
                .Where(s => s.HouseholdId == woning && s.PvKwh == 0)
                .ToListAsync(ct);

            foreach (var rij in rijen)
            {
                if (!perDag.TryGetValue(rij.Date, out var kwh) || kwh <= 0) continue;

                rij.PvKwh = Math.Round(kwh, 3);
                ingevuld++;
            }

            _ = dagen;
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
