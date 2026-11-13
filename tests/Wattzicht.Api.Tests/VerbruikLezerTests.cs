using Wattzicht.Api.Services;

namespace Wattzicht.Api.Tests;

/// <summary>
/// De kilowatturen per maand uit een overzicht van de leverancier halen.
///
/// Bijna iedereen krijgt zo'n mail, en voor wie geen P1-meter heeft is dat het enige wat we over de
/// netkant kunnen weten. Elke leverancier maakt dat overzicht anders, dus staat hier per vorm een
/// regel: een tabel met maanden onder elkaar, een regel met afname en teruglevering naast elkaar,
/// maandnamen voluit of afgekort, en een periode in cijfers.
/// </summary>
public sealed class VerbruikLezerTests
{
    private static readonly DateOnly Vandaag = new(2026, 11, 13);

    [Fact]
    public void Een_tabel_met_maanden_onder_elkaar()
    {
        var tekst = """
            Maand Afname Teruglevering
            september 2026 180 kWh 240 kWh
            oktober 2026 230 kWh 120 kWh
            """;

        var uit = VerbruikLezer.Lees(tekst, Vandaag);

        Assert.True(uit.Gelukt, uit.Fout);
        Assert.Equal(2, uit.Maanden.Count);
        Assert.Equal(new DateOnly(2026, 9, 1), uit.Maanden[0].Maand_);
        Assert.Equal(180, uit.Maanden[0].AfnameKwh);
        Assert.Equal(240, uit.Maanden[0].TerugleveringKwh);
        Assert.Equal(230, uit.Maanden[1].AfnameKwh);
    }

    [Fact]
    public void Afname_en_teruglevering_op_losse_regels()
    {
        var tekst = """
            Overzicht oktober 2026
            Verbruik oktober 2026: 230 kWh
            Teruggeleverd oktober 2026: 120 kWh
            """;

        var uit = VerbruikLezer.Lees(tekst, Vandaag);

        var maand = Assert.Single(uit.Maanden);
        Assert.Equal(230, maand.AfnameKwh);
        Assert.Equal(120, maand.TerugleveringKwh);
    }

    [Fact]
    public void Alleen_afname_mag_ook()
    {
        var uit = VerbruikLezer.Lees("Verbruik mei 2026 310 kWh", Vandaag);

        var maand = Assert.Single(uit.Maanden);
        Assert.Equal(310, maand.AfnameKwh);
        Assert.Null(maand.TerugleveringKwh);
    }

    [Fact]
    public void Een_maand_zonder_jaartal_hoort_bij_het_lopende_jaar()
    {
        var uit = VerbruikLezer.Lees("Verbruik oktober: 230 kWh", Vandaag);

        Assert.Equal(new DateOnly(2026, 10, 1), Assert.Single(uit.Maanden).Maand_);
    }

    [Fact]
    public void Een_maand_die_nog_moet_komen_is_van_vorig_jaar()
    {
        // Het overzicht van december krijg je in januari. Zonder deze regel zou dat een maand in de
        // toekomst worden en verdwijnt hij uit elke grafiek.
        var uit = VerbruikLezer.Lees("Verbruik december: 410 kWh", new DateOnly(2027, 1, 8));

        Assert.Equal(new DateOnly(2026, 12, 1), Assert.Single(uit.Maanden).Maand_);
    }

    [Fact]
    public void Afgekorte_maandnamen_tellen_ook()
    {
        var uit = VerbruikLezer.Lees("okt. 2026 afname 230 kWh", Vandaag);

        Assert.Equal(new DateOnly(2026, 10, 1), Assert.Single(uit.Maanden).Maand_);
    }

    [Fact]
    public void Een_periode_in_cijfers()
    {
        var uit = VerbruikLezer.Lees("Periode 10-2026 verbruik 230 kWh", Vandaag);

        Assert.Equal(new DateOnly(2026, 10, 1), Assert.Single(uit.Maanden).Maand_);
    }

    [Fact]
    public void Duizendtallen_worden_goed_gelezen()
    {
        var uit = VerbruikLezer.Lees("Verbruik januari 2026 1.240 kWh", Vandaag);

        Assert.Equal(1240, Assert.Single(uit.Maanden).AfnameKwh);
    }

    [Fact]
    public void Een_jaartotaal_is_geen_maand()
    {
        // 3.450 kWh in een jaar is geen maandverbruik; daarboven houden we het op een ander getal.
        var uit = VerbruikLezer.Lees("Jaarverbruik 2026: 5.450 kWh", Vandaag);

        Assert.False(uit.Gelukt);
    }

    [Fact]
    public void Een_brief_zonder_cijfers_levert_een_nette_melding()
    {
        var uit = VerbruikLezer.Lees("Beste meneer Dogan, hierbij uw maandoverzicht.", Vandaag);

        Assert.False(uit.Gelukt);
        Assert.Contains("geen maanden", uit.Fout);
    }

    [Fact]
    public void Leeg_is_geen_crash()
    {
        Assert.False(VerbruikLezer.Lees("", Vandaag).Gelukt);
        Assert.False(VerbruikLezer.Lees("   \n ", Vandaag).Gelukt);
    }
}
