using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Wattzicht.Shared;
using Xunit;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Hetzelfde dak, langs de tweede weg gekoppeld.
///
/// Enphase kan twee kanten op: de gateway in de meterkast, die de app op het thuisnetwerk uitleest,
/// en de cloud van Enphase, die de geschiedenis heeft. Je wilt allebei. De gateway geeft het
/// huisverbruik en de netkant, de cloud geeft de dagen van vóór je koppelde.
///
/// Alleen leveren die twee een ander kenmerk op, en de dagtotalen tellen alle apparaten van een
/// woning bij elkaar op. Zonder deze keuze zou het dak er twee keer in staan en zou de app het
/// dubbele opgewekt tonen van wat er werkelijk ligt.
/// </summary>
public class ZelfdeInstallatieTests
{
    private static Device Apparaat(string kenmerk, DeviceType soort = DeviceType.Inverter) => new()
    {
        Id = Guid.NewGuid(),
        HouseholdId = Guid.NewGuid(),
        Type = soort,
        Brand = DeviceBrand.Enphase,
        Name = "Zonnepanelen",
        ExternalId = kenmerk,
    };

    [Fact]
    public void De_cloud_erbij_koppelen_pakt_de_gateway_die_er_al_hangt()
    {
        var lokaal = Apparaat("enphase-lokaal-192.168.178.59");

        var gekozen = ZelfdeInstallatie.Kies([lokaal], DeviceType.Inverter, "4012345");

        Assert.Same(lokaal, gekozen);
    }

    [Fact]
    public void En_andersom_ook()
    {
        var cloud = Apparaat("4012345");

        var gekozen = ZelfdeInstallatie.Kies([cloud], DeviceType.Inverter, "enphase-lokaal-192.168.178.59");

        Assert.Same(cloud, gekozen);
    }

    [Fact]
    public void Hetzelfde_kenmerk_blijft_voorgaan()
    {
        // Twee omvormers op één dak, allebei uit de cloud. Dan moet hij de juiste pakken en niet de
        // eerste de beste van hetzelfde soort.
        var een = Apparaat("4012345");
        var twee = Apparaat("4099999");

        Assert.Same(twee, ZelfdeInstallatie.Kies([een, twee], DeviceType.Inverter, "4099999"));
    }

    [Fact]
    public void Twee_apparaten_uit_dezelfde_cloud_blijven_twee_apparaten()
    {
        // Allebei niet-lokaal: dan is er geen reden om ze op één hoop te gooien, en is dit gewoon een
        // tweede omvormer.
        var een = Apparaat("4012345");

        Assert.Null(ZelfdeInstallatie.Kies([een], DeviceType.Inverter, "4099999"));
    }

    [Fact]
    public void Een_ander_soort_apparaat_telt_niet_mee()
    {
        // Een laadpaal is geen omvormer, ook niet als hij lokaal wordt uitgelezen.
        var laadpaal = Apparaat("alfen-lokaal-192.168.178.20", DeviceType.Charger);

        Assert.Null(ZelfdeInstallatie.Kies([laadpaal], DeviceType.Inverter, "4012345"));
    }

    [Fact]
    public void Zonder_apparaten_is_er_niets_te_kiezen()
    {
        Assert.Null(ZelfdeInstallatie.Kies([], DeviceType.Inverter, "4012345"));
    }
}
