using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;

namespace Wattzicht.Api.Services;

/// <summary>
/// De avondronde langs LaadCash: wat er die dag geladen is, bij de dag zetten.
///
/// Zelfde moment en zelfde reden als het ophalen bij Enphase: een dagtotaal is pas af als de dag dat
/// is. Zie <see cref="AvondRonde"/>.
///
/// Alleen woningen die bij LaadCash zijn aangemeld; dat weten we doordat LaadCash het terugmeldt bij
/// de aanmelding. De rest slaan we over en daar gaat dus ook geen verzoek heen.
/// </summary>
public sealed class LaadcashLaaddataWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<LaadcashLaaddataWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(95), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        do
        {
            try
            {
                var dagen = await RondeAsync(stoppingToken);
                if (dagen > 0) logger.LogInformation("{Aantal} dag(en) laaddata opgehaald bij LaadCash", dagen);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Laaddata bij LaadCash ophalen mislukt");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task<int> RondeAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var laaddata = scope.ServiceProvider.GetRequiredService<LaadcashLaaddata>();

        if (!laaddata.Ingesteld) return 0;

        var nu = clock.GetUtcNow();

        // Wie net is aangemeld wacht niet tot vanavond: dan staat zijn laadhistorie er meteen.
        var woningen = (await db.Households.IgnoreQueryFilters()
            .Where(h => h.LaadcashLinkedAt != null)
            .ToListAsync(ct))
            .Where(h => h.LaadcashLaaddataAt is null || AvondRonde.IsTijd(nu, h.LaadcashLaaddataAt))
            .ToList();

        if (woningen.Count == 0) return 0;

        var gevuld = 0;
        foreach (var woning in woningen)
        {
            gevuld += await laaddata.HaalAsync(db, woning, ct);
        }

        await db.SaveChangesAsync(ct);
        return gevuld;
    }
}
