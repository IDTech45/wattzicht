using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Connectors;

/// <summary>Verzoek om bij een merk in te loggen en de apparaten op te halen.</summary>
/// <param name="Brand">Merk.</param>
/// <param name="Type">Gewenst apparaattype (de connector mag andere typen meeleveren, bijvoorbeeld een batterij bij een hybride omvormer).</param>
/// <param name="Credentials">Ontsleutelde credentials (<c>token</c>, <c>host</c>, <c>username</c>, <c>password</c>, ...).</param>
public sealed record ConnectRequest(
    DeviceBrand Brand,
    DeviceType Type,
    IReadOnlyDictionary<string, string> Credentials);

/// <summary>Eén bij de fabrikant gevonden apparaat.</summary>
/// <param name="ExternalId">Serienummer of id bij de fabrikant.</param>
/// <param name="Name">Voorgestelde weergavenaam.</param>
/// <param name="Type">Apparaattype.</param>
/// <param name="ExtraCredentials">
/// Merkspecifieke gegevens die nodig zijn om later uit te lezen (bijvoorbeeld <c>plant_id</c> of <c>device_kind</c>);
/// worden samengevoegd met de credentials van het verzoek en versleuteld opgeslagen.
/// </param>
public sealed record DiscoveredDevice(
    string ExternalId,
    string Name,
    DeviceType Type,
    IReadOnlyDictionary<string, string>? ExtraCredentials = null);

/// <summary>Uitkomst van <see cref="IDeviceConnector.ConnectAsync"/>.</summary>
public sealed record ConnectResult(bool Success, string? Error, IReadOnlyList<DiscoveredDevice> Devices)
{
    public static ConnectResult Ok(IReadOnlyList<DiscoveredDevice> devices) => new(true, null, devices);
    public static ConnectResult Fail(string error) => new(false, error, []);
}

/// <summary>
/// Genormaliseerde momentopname van één apparaat, onafhankelijk van merk.
/// Tekenconventie: grid positief = afname, negatief = teruglevering; batterij positief = ontladen, negatief = laden.
/// Vermogens in watt, tellers cumulatief in Wh (<c>null</c> als het merk ze niet levert).
/// </summary>
public sealed record DeviceReading(
    DateTimeOffset Timestamp,
    double PvPowerW = 0,
    double LoadPowerW = 0,
    double GridPowerW = 0,
    double BatteryPowerW = 0,
    double? BatterySoc = null,
    double ChargerPowerW = 0,
    double? PvEnergyWh = null,
    double? ImportEnergyWh = null,
    double? ExportEnergyWh = null,
    double? ChargerEnergyWh = null,
    double? BatteryChargeEnergyWh = null,
    double? BatteryDischargeEnergyWh = null)
{
    /// <summary>Zet om naar een database-entiteit voor het opgegeven apparaat.</summary>
    public Reading ToEntity(Guid deviceId) => new()
    {
        DeviceId = deviceId,
        Timestamp = Timestamp,
        PvPowerW = PvPowerW,
        LoadPowerW = LoadPowerW,
        GridPowerW = GridPowerW,
        BatteryPowerW = BatteryPowerW,
        BatterySoc = BatterySoc,
        ChargerPowerW = ChargerPowerW,
        PvEnergyWh = PvEnergyWh,
        ImportEnergyWh = ImportEnergyWh,
        ExportEnergyWh = ExportEnergyWh,
        ChargerEnergyWh = ChargerEnergyWh,
        BatteryChargeEnergyWh = BatteryChargeEnergyWh,
        BatteryDischargeEnergyWh = BatteryDischargeEnergyWh
    };
}

/// <summary>Fout bij het uitlezen van een apparaat (verbinding, authenticatie, onverwacht antwoord).</summary>
public sealed class ConnectorException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Contract per merk. Nieuwe merken: één klasse die dit implementeert + registratie in <see cref="ConnectorRegistry"/>.
/// </summary>
public interface IDeviceConnector
{
    DeviceBrand Brand { get; }

    /// <summary>Valideert de credentials en haalt de apparaten op.</summary>
    Task<ConnectResult> ConnectAsync(ConnectRequest request, CancellationToken ct);

    /// <summary>Leest één genormaliseerde meting. Gooit <see cref="ConnectorException"/> bij een fout.</summary>
    /// <param name="device">Het apparaat (voor id, type en externalId).</param>
    /// <param name="credentials">Ontsleutelde credentials inclusief eventuele extra's uit <see cref="DiscoveredDevice.ExtraCredentials"/>.</param>
    Task<DeviceReading> ReadAsync(Device device, IReadOnlyDictionary<string, string> credentials, CancellationToken ct);
}
