using Wattzicht.Api.Services;
using Wattzicht.Shared;
using Xunit;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Vanaf wanneer stilte van een apparaat een storing is.
///
/// Een halfuur klopt voor een merk dat elke vijf minuten antwoordt. Voor Enphase niet: dat merk mag
/// van zijn eigen maandlimiet maar eens per zoveel uur uitgelezen worden, en met een drempel van een
/// halfuur zou er permanent een melding staan. Omgekeerd is het net zo fout: op 16, 17 en 18 november
/// 2026 kwam er van een Enphase-omvormer drie dagen niets binnen zonder dat er iets afging, omdat de
/// controle alleen draaide ná een geslaagde uitlezing. De bewoner zag het zelf, aan een lege
/// weekgrafiek, en dat is precies de verkeerde volgorde.
/// </summary>
public class StiltedrempelTests
{
    [Fact]
    public void Een_snel_merk_houdt_het_halve_uur()
    {
        Assert.Equal(AlertService.NoDataThreshold, AlertService.Stiltedrempel(TimeSpan.FromMinutes(5)));
    }

    /// <summary>Drie gemiste beurten: één keer is ruis, drie keer is er iets stuk.</summary>
    [Fact]
    public void Een_traag_merk_krijgt_drie_gemiste_beurten()
    {
        Assert.Equal(TimeSpan.FromHours(6), AlertService.Stiltedrempel(TimeSpan.FromHours(2)));
    }

    [Fact]
    public void Zonder_bekend_interval_blijft_het_halve_uur_staan()
    {
        Assert.Equal(AlertService.NoDataThreshold, AlertService.Stiltedrempel(TimeSpan.Zero));
    }

    /// <summary>
    /// Het geval van november: Enphase met twee woningen. De drempel moet ruim binnen een dag
    /// liggen, zodat drie dagen stilte onmogelijk onopgemerkt blijft, en boven één uitleesinterval,
    /// zodat een normale pauze geen melding oplevert.
    /// </summary>
    [Fact]
    public void Drie_dagen_stilte_bij_Enphase_kan_niet_meer_onopgemerkt_blijven()
    {
        var interval = PollingWorker.KortsteInterval(DeviceBrand.Enphase, enphaseApparaten: 2);
        var drempel = AlertService.Stiltedrempel(interval);

        Assert.True(drempel > interval, "een gewone pauze mag geen melding zijn");
        Assert.True(drempel < TimeSpan.FromHours(24),
            $"drempel staat op {drempel.TotalHours:F1} uur; dan merk je een stilgevallen omvormer pas de volgende dag");
    }
}
