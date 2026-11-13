using Wattzicht.Api.Connectors;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Wat de omvormer over de installatie vertelt, overnemen in de woning.
///
/// Het piekvermogen stond als invulveld in de app, en dat is een vraag die bijna niemand uit zijn
/// hoofd kan beantwoorden. Terwijl Enphase het gewoon meldt in de samenvatting die wij elk kwartier
/// ophalen. Deze test legt twee dingen vast: dat we het overnemen, en dat we het getal van de
/// bewoner met rust laten.
/// </summary>
public sealed class InstallatieFeitenTests
{
    private static Household Woning(double? kwp = null, int? panelen = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Thuis",
        Plan = "free",
        PvPeakKw = kwp,
        PvPanelCount = panelen,
    };

    private static DeviceReading Meting(double? piekW = null, int? panelen = null) =>
        new(DateTimeOffset.UtcNow, PvPeakW: piekW, PanelCount: panelen);

    [Fact]
    public void Een_lege_woning_krijgt_het_piekvermogen_van_de_omvormer()
    {
        var woning = Woning();

        Assert.True(InstallatieFeiten.Overnemen(woning, Meting(piekW: 4900, panelen: 14)));

        Assert.Equal(4.9, woning.PvPeakKw);
        Assert.Equal(14, woning.PvPanelCount);
    }

    [Fact]
    public void Wat_de_bewoner_zelf_invulde_blijft_staan()
    {
        // Hij pakte zijn offerte erbij en vulde 6,4 in. Dat hoort niet de volgende ochtend
        // overschreven te worden door 6,2 uit een systeem dat een paneel in de schaduw niet meetelt.
        var woning = Woning(kwp: 6.4);

        InstallatieFeiten.Overnemen(woning, Meting(piekW: 6200, panelen: 20));

        Assert.Equal(6.4, woning.PvPeakKw);
        Assert.Equal(20, woning.PvPanelCount);
    }

    [Fact]
    public void Het_aantal_panelen_volgt_wel_altijd_de_omvormer()
    {
        // Dat kan de bewoner nergens invullen, dus is er ook niets van hem om te beschermen. Legt hij
        // er twee bij, dan hoort dat de volgende ronde gewoon te kloppen.
        var woning = Woning(panelen: 14);

        Assert.True(InstallatieFeiten.Overnemen(woning, Meting(panelen: 16)));
        Assert.Equal(16, woning.PvPanelCount);

        Assert.False(InstallatieFeiten.Overnemen(woning, Meting(panelen: 16)));
    }

    [Fact]
    public void Een_onzinnig_vermogen_nemen_we_niet_over()
    {
        var woning = Woning();

        InstallatieFeiten.Overnemen(woning, Meting(piekW: 0));
        Assert.Null(woning.PvPeakKw);

        InstallatieFeiten.Overnemen(woning, Meting(piekW: 250_000));
        Assert.Null(woning.PvPeakKw);
    }

    [Fact]
    public void Een_merk_dat_er_niets_over_zegt_verandert_niets()
    {
        var woning = Woning(kwp: 4.2, panelen: 12);

        Assert.False(InstallatieFeiten.Overnemen(woning, Meting()));

        Assert.Equal(4.2, woning.PvPeakKw);
        Assert.Equal(12, woning.PvPanelCount);
    }
}
