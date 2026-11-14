using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests;

/// <summary>
/// De melding van de avond ervoor: morgen is het zonnig, zet je auto dan aan de lader.
///
/// De rem is het hele punt van deze test. Een melding over laden kan botsen met de melding die
/// vlak voor het goedkope uur uitgaat, en die twee spreken elkaar tegen: het ene zegt "nu de stekker
/// erin", het andere "wacht tot morgenmiddag". Twee berichten op één avond is bovendien drammen, en
/// dat is precies waarna mensen alle meldingen uitzetten.
/// </summary>
public sealed class ZonMorgenMeldingTests
{
    // 10 november 2026 is wintertijd: UTC+1.
    private static DateTimeOffset Lokaal(int uur, int minuut = 0) =>
        new(2026, 11, 10, uur - 1, minuut, 0, TimeSpan.Zero);

    [Fact]
    public void Om_acht_uur_s_avonds_mag_het()
    {
        Assert.True(PushPolicy.MaySendZonMorgen(new Household(), Lokaal(20)));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(12)]
    [InlineData(19)]
    [InlineData(21)]
    [InlineData(23)]
    public void Op_elk_ander_uur_niet(int uur)
    {
        Assert.False(PushPolicy.MaySendZonMorgen(new Household(), Lokaal(uur)));
    }

    [Fact]
    public void Uitgezet_door_de_bewoner_nooit()
    {
        Assert.False(PushPolicy.MaySendZonMorgen(new Household { NotifyZonMorgen = false }, Lokaal(20)));
    }

    [Fact]
    public void Niet_twee_keer_op_dezelfde_avond_maar_morgen_weer_wel()
    {
        var woning = new Household { LastZonMorgenPushAt = Lokaal(20) };

        Assert.False(PushPolicy.MaySendZonMorgen(woning, Lokaal(20, 45)));
        Assert.True(PushPolicy.MaySendZonMorgen(woning, Lokaal(20).AddDays(1)));
    }

    [Fact]
    public void Niet_kort_na_een_laadmoment_melding()
    {
        // Die ging over vannacht, deze over morgenmiddag. Ze zouden onder elkaar staan en elkaar
        // tegenspreken, dus zwijgen we.
        var woning = new Household { LastSmartMomentPushAt = Lokaal(18, 30) };

        Assert.False(PushPolicy.MaySendZonMorgen(woning, Lokaal(20)));
    }

    [Fact]
    public void Wel_als_die_laadmoment_melding_van_vanmiddag_was()
    {
        var woning = new Household { LastSmartMomentPushAt = Lokaal(13) };

        Assert.True(PushPolicy.MaySendZonMorgen(woning, Lokaal(20)));
    }

    [Fact]
    public void Het_bericht_noemt_de_uren_en_de_lader()
    {
        var (titel, tekst) = PushPolicy.ZonMorgenMessage(new ZonBlokDto(11, 15, 9.4));

        Assert.Equal("Morgen is het zonnig", titel);
        Assert.Equal("Morgen zon tussen 11:00 en 15:00. Zet je auto dan aan de lader.", tekst);
    }

    [Fact]
    public void Het_bericht_spreekt_de_taal_van_de_bewoner()
    {
        var engels = PushPolicy.ZonMorgenMessage(new ZonBlokDto(11, 15, 9.4), "en").Body;
        var turks = PushPolicy.ZonMorgenMessage(new ZonBlokDto(11, 15, 9.4), "tr").Body;

        Assert.Contains("11:00", engels);
        Assert.Contains("15:00", turks);
        Assert.NotEqual(engels, turks);
    }
}
