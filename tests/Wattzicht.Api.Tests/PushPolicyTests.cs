using Wattzicht.Api.Data;
using Wattzicht.Api.Services;

namespace Wattzicht.Api.Tests;

/// <summary>Overschot-meldingen: niet 's nachts, niet te vaak, en alleen als de bewoner het wil.</summary>
public sealed class PushPolicyTests
{
    // 30 oktober 2026 is wintertijd: UTC+1.
    private static DateTimeOffset Local(int hour, int minute = 0) => new(2026, 10, 30, hour - 1, minute, 0, TimeSpan.Zero);

    [Fact]
    public void Overdag_zonder_eerdere_melding_mag_het()
    {
        Assert.True(PushPolicy.MaySendSurplus(new Household(), Local(12)));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(20)]
    [InlineData(23)]
    public void Buiten_9_tot_20_uur_niet(int hour)
    {
        Assert.False(PushPolicy.MaySendSurplus(new Household(), Local(hour)));
    }

    [Fact]
    public void Niet_binnen_3_uur_na_de_vorige()
    {
        var household = new Household { LastSurplusPushAt = Local(10) };

        Assert.False(PushPolicy.MaySendSurplus(household, Local(12, 59)));
        Assert.True(PushPolicy.MaySendSurplus(household, Local(13)));
    }

    [Fact]
    public void Uitgezet_door_bewoner_nooit()
    {
        Assert.False(PushPolicy.MaySendSurplus(new Household { NotifySurplus = false }, Local(12)));
    }

    [Fact]
    public void Storingsmelding_noemt_het_apparaat_of_telt_ze()
    {
        var a = new Alert { DeviceId = Guid.NewGuid(), Message = "Geen data sinds 10:00" };
        var b = new Alert { DeviceId = Guid.NewGuid(), Message = "Offline" };
        var names = new Dictionary<Guid, string> { [a.DeviceId] = "SolaX X1-Hybrid" };

        Assert.Equal(("Storing: SolaX X1-Hybrid", "Geen data sinds 10:00"), PushPolicy.AlertMessage([a], names));
        Assert.Equal("2 storingen", PushPolicy.AlertMessage([a, b], names).Title);
    }
}
