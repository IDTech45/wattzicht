using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Services;

/// <summary>
/// De geladen kilowatturen bij LaadCash ophalen.
///
/// LaadCash en Wattzicht zijn twee apps van hetzelfde bedrijf, en wie zijn laadpaal daar heeft
/// hangen hoeft hem hier niet nog eens te koppelen. Wij vragen per woning op wat er per dag door de
/// laadpaal is gegaan en zetten dat bij de dag. Zo staat het laden in hetzelfde beeld als de opwek
/// en het verbruik, in plaats van in een tweede app ernaast.
///
/// Van server naar server, met hetzelfde gedeelde geheim als de melding de andere kant op. Er gaat
/// alleen een woning-id heen en er komen alleen dagen met kilowatturen terug.
///
/// Wat wij zelf meten gaat voor. Hangt er in deze woning een laadpaal die wij uitlezen, dan is die
/// meting van hier en vullen we alleen de dagen waar wij niets hebben. Anders zou een dag dubbel
/// geteld worden: één keer door onze eigen meter en één keer door LaadCash.
/// </summary>
public sealed class LaadcashLaaddata(
    IHttpClientFactory http,
    IConfiguration config,
    TimeProvider clock,
    ILogger<LaadcashLaaddata> logger)
{
    public const string HttpClientName = "laadcash";

    /// <summary>Zo ver terug vragen we de eerste keer.</summary>
    public const int MaximaalDagen = 400;

    /// <summary>En zo ver bij elke volgende ronde, om gaatjes van de afgelopen week te vullen.</summary>
    public const int VersDagen = 7;

    private string Basis => config["Laadcash:ApiBase"]?.TrimEnd('/') ?? "https://api.laadcash.nl";
    private string? Geheim => config["Laadcash:SharedSecret"];

    public bool Ingesteld => !string.IsNullOrWhiteSpace(Geheim);

    private sealed record Antwoord(List<DagDto>? Dagen);

    private sealed record DagDto(DateOnly Dag, double Kwh);

    /// <summary>
    /// Eén woning bijwerken; geeft terug hoeveel dagen er gevuld zijn.
    /// </summary>
    public async Task<int> HaalAsync(AppDbContext db, Household woning, CancellationToken ct)
    {
        if (!Ingesteld || woning.LaadcashLinkedAt is null) return 0;

        var nu = clock.GetUtcNow();
        var eersteKeer = woning.LaadcashLaaddataAt is null;
        var vanaf = DutchTime.LocalDate(nu).AddDays(-(eersteKeer ? MaximaalDagen : VersDagen));

        List<DagDto> dagen;
        try
        {
            var client = http.CreateClient(HttpClientName);
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.Remove("X-Laadcash-Secret");
            client.DefaultRequestHeaders.Add("X-Laadcash-Secret", Geheim);

            var url = $"{Basis}/api/v1/wattzicht/laaddata?home={woning.Id}&vanaf={vanaf:yyyy-MM-dd}";
            var antwoord = await client.GetFromJsonAsync<Antwoord>(url, ct);
            dagen = antwoord?.Dagen ?? [];
        }
        catch (Exception ex)
        {
            // Eén woning die niet lukt mag de ronde niet stoppen, en de datum zetten we niet: dan
            // probeert hij het morgen gewoon opnieuw.
            logger.LogWarning(ex, "Laaddata van LaadCash ophalen mislukte voor woning {Id}", woning.Id);
            return 0;
        }

        woning.LaadcashLaaddataAt = nu;
        if (dagen.Count == 0) return 0;

        // Meet deze woning zelf een laadpaal, dan is dat de bron en vult LaadCash alleen de gaten.
        var eigenPaal = await db.Devices.IgnoreQueryFilters()
            .AnyAsync(d => d.HouseholdId == woning.Id && d.Type == DeviceType.Charger, ct);

        var bestaand = await db.DailySummaries.IgnoreQueryFilters()
            .Where(s => s.HouseholdId == woning.Id)
            .ToDictionaryAsync(s => s.Date, ct);

        var gevuld = 0;
        foreach (var dag in dagen)
        {
            if (dag.Kwh <= 0) continue;

            if (bestaand.TryGetValue(dag.Dag, out var rij))
            {
                if (eigenPaal && rij.ChargedKwh > 0) continue;
                if (Math.Abs(rij.ChargedKwh - dag.Kwh) < 0.001) continue;

                rij.ChargedKwh = dag.Kwh;
                rij.UpdatedAt = nu;
                gevuld++;
                continue;
            }

            db.DailySummaries.Add(new DailySummary
            {
                Id = Guid.NewGuid(),
                HouseholdId = woning.Id,
                Date = dag.Dag,
                ChargedKwh = dag.Kwh,
                UpdatedAt = nu,
            });
            gevuld++;
        }

        return gevuld;
    }
}
