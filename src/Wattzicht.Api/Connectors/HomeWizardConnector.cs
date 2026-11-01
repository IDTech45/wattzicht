using System.Text.Json;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Connectors;

/// <summary>
/// P1-meter op de slimme meter, bijvoorbeeld een HomeWizard-dongle.
///
/// Het bijzondere aan dit merk: onze server kan er niet bij. Zo'n kastje heeft een adres als
/// 192.168.1.50 en praat alleen op het eigen netwerk van de bewoner, zonder cloud en zonder sleutel.
/// De telefoon staat daar wél op zodra iemand thuis is, dus de app leest uit en stuurt het door naar
/// <c>POST /devices/{id}/readings</c>. Deze connector doet daarom geen netwerkverkeer: hij legt
/// alleen vast wat de app bij het koppelen al lokaal heeft gezien, en de poller slaat hem over.
///
/// Waarom het de moeite waard is: zonder deze poort weten we bij micro-omvormers en gewone
/// string-omvormers alleen wat het dak maakt en niet wat het huis van het net haalt. Dan is er geen
/// besparing uit te rekenen, en dat is precies waar de app voor is.
///
/// Merkneutraal opgezet: elke dongle leest dezelfde P1-poort uit. De app stuurt wat hij gevonden
/// heeft mee als <c>product</c> en <c>serial</c>.
/// </summary>
public sealed class HomeWizardConnector : IDeviceConnector
{
    public DeviceBrand Brand => DeviceBrand.HomeWizard;

    /// <summary>Dit apparaat wordt door de app uitgelezen, niet door de server.</summary>
    public bool IsLokaal => true;

    public Task<ConnectResult> ConnectAsync(ConnectRequest request, CancellationToken ct)
    {
        var host = request.Credentials.Get("host") ?? request.Credentials.Get("ip");
        if (string.IsNullOrWhiteSpace(host))
        {
            return Task.FromResult(ConnectResult.Fail("Vul het IP-adres van je P1-meter in, bijvoorbeeld 192.168.1.50."));
        }

        // De app heeft het kastje al aangesproken voordat hij hier kwam; zonder serienummer zou een
        // tweede meter in hetzelfde huis dezelfde regel overschrijven, dus dan valt hij terug op het
        // adres. Dat is niet ideaal (een router kan het adres opnieuw uitdelen) maar wel uniek genoeg.
        var serial = request.Credentials.Get("serial") ?? host;
        var product = request.Credentials.Get("product");
        var naam = string.IsNullOrWhiteSpace(product) ? "P1-meter" : product!;

        return Task.FromResult(ConnectResult.Ok([new DiscoveredDevice(serial, naam, DeviceType.P1Meter)]));
    }

    public Task<DeviceReading> ReadAsync(Device device, DeviceCredentials credentials, CancellationToken ct) =>
        throw new ConnectorException(
            "Een P1-meter wordt vanuit de app uitgelezen, niet vanaf onze server. Open de app op hetzelfde wifi-netwerk.");

    /// <summary>
    /// Het antwoord van de dongle omzetten naar een meting. Apart en static, zodat de ingestie van de
    /// app en een test dezelfde omzetting gebruiken.
    ///
    /// De P1-poort geeft vier tellers (laag en hoog, afname en teruglevering) en een vermogen dat
    /// positief is bij afname en negatief bij teruglevering. Dat is precies onze eigen afspraak, dus
    /// het teken blijft zoals het is.
    /// </summary>
    public static DeviceReading LeesP1(JsonElement root, DateTimeOffset nu)
    {
        var watt = root.GetDoubleOrNull("active_power_w", "activePowerW") ?? 0;

        var afname = Som(root, "total_power_import_kwh", "total_power_import_t1_kwh", "total_power_import_t2_kwh");
        var terug = Som(root, "total_power_export_kwh", "total_power_export_t1_kwh", "total_power_export_t2_kwh");

        return new DeviceReading(
            Timestamp: nu,
            GridPowerW: watt,
            ImportEnergyWh: afname is null ? null : afname * 1000,
            ExportEnergyWh: terug is null ? null : terug * 1000);
    }

    /// <summary>
    /// De totaalteller als die er is, anders laag plus hoog bij elkaar. Oudere firmware geeft alleen
    /// de twee tarieven; wie dan de eerste pakt die hij vindt telt de nachtstroom niet mee.
    /// </summary>
    private static double? Som(JsonElement root, string totaal, string t1, string t2)
    {
        if (root.GetDoubleOrNull(totaal) is { } heel) return heel;

        var een = root.GetDoubleOrNull(t1);
        var twee = root.GetDoubleOrNull(t2);
        return een is null && twee is null ? null : (een ?? 0) + (twee ?? 0);
    }
}
