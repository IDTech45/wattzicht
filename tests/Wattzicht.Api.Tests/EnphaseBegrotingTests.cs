using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Het gratis Enphase-plan geeft 1.000 verzoeken per maand en LaadCash deelt hetzelfde
/// developer-account. Hier stond een vast uur tussen twee uitlezingen, en dat lijkt zuinig tot je het
/// narekent: 24 maal 30 is 720 verzoeken per maand voor één huis. Het tweede huis trok het quotum er
/// dus doorheen en nam de Enphase-koppeling van LaadCash mee in de val.
/// </summary>
public sealed class EnphaseBegrotingTests
{
    [Fact]
    public void Een_huis_wordt_hooguit_een_keer_per_uur_uitgelezen()
        => Assert.True(PollingWorker.EnphaseInterval(1).TotalMinutes >= 60);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(20)]
    public void Het_maandbudget_wordt_nooit_overschreden(int huizen)
    {
        var interval = PollingWorker.EnphaseInterval(huizen);
        var perHuisPerMaand = PollingWorker.EnphaseUrenPerDag * 30.0 * 60.0 / interval.TotalMinutes;
        var totaal = perHuisPerMaand * huizen;
        Assert.True(totaal <= PollingWorker.EnphaseMaandbudget,
            $"{huizen} huizen zouden samen {totaal:F0} verzoeken doen, en dat mogen er hoogstens {PollingWorker.EnphaseMaandbudget} zijn");
    }

    [Fact]
    public void Meer_huizen_betekent_rustiger_uitlezen()
        => Assert.True(PollingWorker.EnphaseInterval(10) > PollingWorker.EnphaseInterval(2));

    [Fact]
    public void Een_merk_zonder_limiet_mag_elke_ronde()
        => Assert.Equal(TimeSpan.Zero, PollingWorker.KortsteInterval(DeviceBrand.HomeWizard));

    [Theory]
    [InlineData(3, false)]   // midden in de nacht
    [InlineData(5, false)]
    [InlineData(6, true)]    // eerste uur
    [InlineData(13, true)]
    [InlineData(21, true)]   // laatste uur
    [InlineData(22, false)]
    public void Alleen_overdag_uitlezen(int uurNl, bool verwacht)
    {
        // Januari, dus wintertijd: Nederlandse tijd is dan UTC+1.
        var nu = new DateTimeOffset(2027, 1, 15, uurNl - 1, 30, 0, TimeSpan.Zero);
        Assert.Equal(verwacht, PollingWorker.InDeUitleesuren(nu));
    }
}
