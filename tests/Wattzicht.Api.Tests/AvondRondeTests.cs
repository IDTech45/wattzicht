using Wattzicht.Api.Services;
using Xunit;

namespace Wattzicht.Api.Tests;

/// <summary>
/// De dag ophalen als hij voorbij is, één keer per woning.
///
/// Overdag een dagtotaal opvragen levert een getal op dat een uur later alweer achterloopt, en bij
/// elke ronde schuift het opnieuw. Om tien uur 's avonds is de dag klaar.
///
/// En het budget is de tweede reden: Enphase telt aanroepen per developer-app, niet per woning. Met
/// de gedeelde sleutel delen alle klanten samen duizend per maand. Eén aanroep per woning per dag is
/// dertig; vier keer per dag zou er honderdtwintig van maken en dan is het budget bij zeven klanten op.
/// </summary>
public class AvondRondeTests
{
    /// <summary>Een moment op de Nederlandse klok.</summary>
    private static DateTimeOffset Nl(int dag, int uur, int minuut = 0)
    {
        var lokaal = new DateTime(2026, 11, dag, uur, minuut, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(lokaal, DutchTime.Zone.GetUtcOffset(lokaal));
    }

    [Fact]
    public void Overdag_halen_we_niets_op()
    {
        Assert.False(AvondRonde.IsTijd(Nl(14, 8), null));
        Assert.False(AvondRonde.IsTijd(Nl(14, 13), null));
        Assert.False(AvondRonde.IsTijd(Nl(14, 21, 59), null));
    }

    [Fact]
    public void In_het_avondvenster_wel()
    {
        Assert.True(AvondRonde.IsTijd(Nl(14, 22), null));
        Assert.True(AvondRonde.IsTijd(Nl(14, 22, 45), null));
    }

    [Fact]
    public void Na_het_venster_niet_meer()
    {
        // Anders zou een woning die om elf uur langskomt alsnog een tweede aanroep krijgen.
        Assert.False(AvondRonde.IsTijd(Nl(14, 23), null));
        Assert.False(AvondRonde.IsTijd(Nl(15, 0, 30), null));
    }

    [Fact]
    public void Binnen_hetzelfde_venster_maar_een_keer()
    {
        // De ronde komt elk kwartier langs. Zonder deze regel zouden dat vier aanroepen worden.
        var gedaan = Nl(14, 22, 2);

        Assert.False(AvondRonde.IsTijd(Nl(14, 22, 17), gedaan));
        Assert.False(AvondRonde.IsTijd(Nl(14, 22, 47), gedaan));
    }

    [Fact]
    public void De_volgende_avond_weer()
    {
        var gisteren = Nl(14, 22, 2);

        Assert.True(AvondRonde.IsTijd(Nl(15, 22, 5), gisteren));
    }

    [Fact]
    public void Een_ophaling_van_overdag_houdt_de_avond_niet_tegen()
    {
        // De eerste keer gebeurt meteen bij het koppelen, en dat kan midden op de dag zijn. Die dag
        // hoort 's avonds alsnog compleet gemaakt te worden.
        var bijHetKoppelen = Nl(14, 8, 29);

        Assert.True(AvondRonde.IsTijd(Nl(14, 22, 5), bijHetKoppelen));
    }
}
