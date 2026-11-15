using Wattzicht.Api.Services;
using Xunit;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Wanneer we de geschiedenis bij Enphase ophalen.
///
/// Dit ging op 15-11-2026 mis op een manier die niet te zien is. De avond ervoor stempelde de ronde
/// mijn lokale gateway af als "geschiedenis opgehaald", terwijl daar zonder cloudtoken nooit iets te
/// halen viel. Toen ik die nacht de cloud koppelde, stond die vlag dus al op gedaan en bleef mijn
/// hele jaar op nul staan. Geen fout, geen melding, alleen een lege grafiek.
/// </summary>
public class HistorieNodigTests
{
    private static DateTimeOffset Nl(int uur) =>
        new(2026, 11, 15, uur, 10, 0, TimeSpan.FromHours(1));

    [Fact]
    public void Zonder_token_halen_we_niets_en_stempelen_we_niets()
    {
        Assert.False(EnphaseGeschiedenisWorker.Nodig(Nl(22), null, heeftToken: false));
        Assert.False(EnphaseGeschiedenisWorker.Nodig(Nl(12), null, heeftToken: false));
    }

    [Fact]
    public void Net_gekoppeld_wacht_niet_tot_vanavond()
    {
        // Dat is het eerste wat iemand wil zien nadat hij op verbinden heeft gedrukt.
        Assert.True(EnphaseGeschiedenisWorker.Nodig(Nl(2), null, heeftToken: true));
        Assert.True(EnphaseGeschiedenisWorker.Nodig(Nl(12), null, heeftToken: true));
    }

    [Fact]
    public void Daarna_alleen_s_avonds()
    {
        var gisteravond = new DateTimeOffset(2026, 11, 14, 22, 5, 0, TimeSpan.FromHours(1));

        Assert.False(EnphaseGeschiedenisWorker.Nodig(Nl(12), gisteravond, heeftToken: true));
        Assert.True(EnphaseGeschiedenisWorker.Nodig(Nl(22), gisteravond, heeftToken: true));
    }

    [Fact]
    public void En_binnen_dat_venster_maar_een_keer()
    {
        var zojuist = new DateTimeOffset(2026, 11, 15, 22, 2, 0, TimeSpan.FromHours(1));

        Assert.False(EnphaseGeschiedenisWorker.Nodig(Nl(22), zojuist, heeftToken: true));
    }
}
