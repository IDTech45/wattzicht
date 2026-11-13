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
    public void Storingsmeldingen_hooguit_eens_per_2_uur()
    {
        var household = new Household { LastAlertPushAt = Local(10) };

        Assert.False(PushPolicy.MaySendAlert(household, Local(11, 59)));
        Assert.True(PushPolicy.MaySendAlert(household, Local(12)));
        Assert.False(PushPolicy.MaySendAlert(new Household { NotifyAlerts = false }, Local(12)));
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

    [Fact]
    public void Een_melding_volgt_de_taal_van_het_huishouden()
    {
        // Een pushmelding komt uit een achtergrondtaak, dus zonder verzoek waarin een taal staat.
        // Hij hoort de taal te volgen die bij de woning is vastgelegd.
        var start = Local(14);

        var nl = PushPolicy.SmartMomentMessage(start, 0.75m, 0, "nl");
        var en = PushPolicy.SmartMomentMessage(start, 0.75m, 0, "en");
        var tr = PushPolicy.SmartMomentMessage(start, 0.75m, 0, "tr");

        Assert.Contains("Straks", nl.Title);
        Assert.Contains("cheapest", en.Title);
        Assert.Contains("avantajl", tr.Title);

        // Het bedrag staat in alle drie, in de notatie van die taal. Dat is met opzet: een komma in
        // een Engelse melding leest als een duizendscheiding, en "0.75" in een Nederlandse melding
        // ziet eruit als een fout. Deze regel bewaakt ook dat het niet de cultuur van de server
        // volgt; die is op Linux invariant en gaf iedereen een punt.
        Assert.Contains("0,75", nl.Body);
        Assert.Contains("0.75", en.Body);
        Assert.Contains("0,75", tr.Body);
    }

    [Fact]
    public void Zonder_taal_blijft_het_Nederlands()
    {
        // Oude woningen hebben nog geen taal staan; die horen gewoon Nederlands te krijgen en niet
        // een lege melding.
        var zonder = PushPolicy.SmartMomentMessage(Local(14), 0m, 1.5);
        var leeg = PushPolicy.SmartMomentMessage(Local(14), 0m, 1.5, "");

        Assert.Equal("Straks laden op je eigen zon", zonder.Title);
        Assert.Equal(zonder, leeg);
    }

    [Fact]
    public void Een_laadbeurt_in_het_Turks_noemt_nog_steeds_de_kilowatturen()
    {
        var sessie = new Laadsessie(Local(18), Local(20), 12.4, 9.3, true);

        var tr = PushPolicy.ChargeSessionMessage(sessie, null, "tr");

        Assert.Contains("12,4", tr.Body);
        Assert.Contains("9,3", tr.Body);
        Assert.DoesNotContain("Er ging", tr.Body);
    }
}
