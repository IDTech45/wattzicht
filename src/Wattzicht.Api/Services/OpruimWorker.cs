using Wattzicht.Api.Data;

namespace Wattzicht.Api.Services;

/// <summary>
/// Ruimt op wat een bewaartermijn heeft.
///
/// Voorlopig alleen de documenten waar het inlezen niets uit haalde. Een bewaartermijn die alleen
/// in een toelichting staat en nergens draait, is geen bewaartermijn; daarom is dit een eigen
/// worker en geen regel die ergens achteraan een andere taak hangt.
/// </summary>
public sealed class OpruimWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<OpruimWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            try
            {
                await RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Opruimen mislukt");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Eén ronde. Intern zodat een test hem direct kan aanroepen.</summary>
    internal async Task<int> RunAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var weg = await ImportDagboek.OpruimenAsync(db, clock.GetUtcNow(), ct);
        if (weg > 0)
        {
            logger.LogInformation("{Aantal} onleesbare document(en) verlopen en verwijderd", weg);
        }

        return weg;
    }
}
