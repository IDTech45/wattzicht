using Microsoft.Extensions.Time.Testing;
using Wattzicht.Api.Services;
using Xunit;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Bewaking op de uitleesronde.
///
/// De aanleiding staat in LaadCash: daar draaide de sync wekenlang niet omdat de planner de taak
/// niet kon aanmaken, en niemand zag het. Een ronde die stopt meldt zichzelf nooit, dus moet iets
/// anders de klok in de gaten houden. Hier staat wat die bewaking moet doen, en net zo belangrijk
/// wat ze niet moet doen: niet klagen vlak na een herstart, en niet blijven herhalen.
/// </summary>
public class RondeBewakingTests
{
    private static Hartslag MaakHartslag(FakeTimeProvider klok) => new(klok);

    [Fact]
    public void Zonder_ronde_telt_de_stilte_vanaf_het_starten()
    {
        var klok = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 20, 0, 0, TimeSpan.Zero));
        var hartslag = MaakHartslag(klok);

        klok.Advance(TimeSpan.FromMinutes(40));

        // Niet nul: een proces dat meteen na de start omvalt zou zich anders nooit als stil melden.
        Assert.Equal(40, hartslag.Stilte().TotalMinutes, 1);
        Assert.Null(hartslag.Stand().LaatsteRonde);
    }

    [Fact]
    public void Een_afgeronde_ronde_zet_de_klok_terug_op_nul()
    {
        var klok = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 20, 0, 0, TimeSpan.Zero));
        var hartslag = MaakHartslag(klok);

        klok.Advance(TimeSpan.FromMinutes(25));
        hartslag.Rondeklaar(huishoudens: 3, apparatenOk: 7, apparatenMislukt: 1);

        var stand = hartslag.Stand();
        Assert.Equal(0, stand.Stilte.TotalMinutes, 1);
        Assert.Equal(klok.GetUtcNow(), stand.LaatsteRonde);
        Assert.Equal(3, stand.Huishoudens);
        Assert.Equal(7, stand.ApparatenOk);
        Assert.Equal(1, stand.ApparatenMislukt);
    }

    [Fact]
    public void Na_een_ronde_loopt_de_stilte_weer_op()
    {
        var klok = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 20, 0, 0, TimeSpan.Zero));
        var hartslag = MaakHartslag(klok);

        hartslag.Rondeklaar(1, 1, 0);
        klok.Advance(TimeSpan.FromMinutes(31));

        Assert.Equal(31, hartslag.Stilte().TotalMinutes, 1);
    }

    /// <summary>
    /// De grens staat standaard op een half uur: zes gemiste rondes van vijf minuten. Eén gemiste
    /// ronde is ruis (een trage leverancier, een herstart); een half uur stilte is een storing.
    /// </summary>
    [Theory]
    [InlineData(5, false)]
    [InlineData(29, false)]
    [InlineData(31, true)]
    [InlineData(600, true)]
    public void De_grens_bepaalt_wanneer_het_alarm_hoort_af_te_gaan(int minutenStil, bool alarm)
    {
        var klok = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 20, 0, 0, TimeSpan.Zero));
        var hartslag = MaakHartslag(klok);
        var grens = TimeSpan.FromMinutes(30);

        hartslag.Rondeklaar(1, 1, 0);
        klok.Advance(TimeSpan.FromMinutes(minutenStil));

        Assert.Equal(alarm, hartslag.Stilte() > grens);
    }

    /// <summary>
    /// De hartslag wordt vanuit de pollronde gezet en vanuit de wachter gelezen: twee verschillende
    /// draden. Bij elkaar opgeteld moet het aantal rondes kloppen en mag er niets omvallen.
    /// </summary>
    [Fact]
    public async Task Tegelijk_schrijven_en_lezen_gaat_goed()
    {
        var klok = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 20, 0, 0, TimeSpan.Zero));
        var hartslag = MaakHartslag(klok);

        var schrijven = Task.Run(() =>
        {
            for (var i = 0; i < 500; i++) hartslag.Rondeklaar(i, i, 0);
        });
        var lezen = Task.Run(() =>
        {
            for (var i = 0; i < 500; i++) _ = hartslag.Stand();
        });

        await Task.WhenAll(schrijven, lezen);

        Assert.Equal(499, hartslag.Stand().Huishoudens);
    }
}
