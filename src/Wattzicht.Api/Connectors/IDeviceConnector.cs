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

/// <summary>
/// De credentials van één apparaat tijdens het uitlezen, met de mogelijkheid ze bij te werken.
///
/// Nodig voor merken die hun sleutel onderweg vernieuwen. Enphase geeft bij elke token-vernieuwing een
/// nieuw refresh-token en maakt het oude ongeldig; bewaren we dat niet, dan werkt de koppeling precies
/// één keer en daarna nooit meer. Een connector zet de nieuwe waarde met <see cref="Set"/>; de poller
/// ziet <see cref="Changed"/> en versleutelt het geheel opnieuw op het apparaat.
/// </summary>
public sealed class DeviceCredentials : IReadOnlyDictionary<string, string>
{
    private readonly Dictionary<string, string> _values;

    public DeviceCredentials(IDictionary<string, string> values)
        => _values = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);

    /// <summary>Is er sinds het inlezen iets veranderd dat bewaard moet worden?</summary>
    public bool Changed { get; private set; }

    /// <summary>Werkt een waarde bij. Dezelfde waarde opnieuw zetten verandert niets.</summary>
    public void Set(string key, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            if (_values.Remove(key)) Changed = true;
            return;
        }

        if (_values.TryGetValue(key, out var huidig) && huidig == value) return;
        _values[key] = value;
        Changed = true;
    }

    /// <summary>Platte kopie om te versleutelen.</summary>
    public Dictionary<string, string> ToDictionary() => new(_values, StringComparer.OrdinalIgnoreCase);

    /// <summary>Een platte dictionary mag er zo in; handig voor tests en voor merken zonder sleutelvernieuwing.</summary>
    public static implicit operator DeviceCredentials(Dictionary<string, string> values) => new(values);

    public string this[string key] => _values[key];
    public IEnumerable<string> Keys => _values.Keys;
    public IEnumerable<string> Values => _values.Values;
    public int Count => _values.Count;
    public bool ContainsKey(string key) => _values.ContainsKey(key);
    public bool TryGetValue(string key, out string value) => _values.TryGetValue(key, out value!);
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _values.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Wat we een laadpaal kunnen vragen.</summary>
public enum Laadopdracht
{
    /// <summary>Beginnen of doorgaan met laden.</summary>
    Start = 0,

    /// <summary>Pauzeren. De auto blijft aangesloten; dit is geen stekker eruit.</summary>
    Pauze = 1,
}

/// <summary>Fout bij het uitlezen van een apparaat (verbinding, authenticatie, onverwacht antwoord).</summary>
public sealed class ConnectorException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Contract per merk. Nieuwe merken: één klasse die dit implementeert + registratie in <see cref="ConnectorRegistry"/>.
/// </summary>
public interface IDeviceConnector
{
    DeviceBrand Brand { get; }

    /// <summary>
    /// Wordt dit apparaat door de app uitgelezen in plaats van door de server? Dat geldt voor
    /// apparaten die alleen op het thuisnetwerk praten, zoals een P1-meter op de slimme meter. De
    /// poller slaat ze over, want 192.168.1.50 bestaat voor onze server niet.
    /// </summary>
    bool IsLokaal => false;

    /// <summary>
    /// Hetzelfde, maar dan met de gegevens van één koppeling erbij.
    ///
    /// Nodig voor merken die het allebei kunnen. Een Sessy-thuisbatterij staat bij vrijwel iedereen op
    /// een adres als 192.168.1.50, en daar komt onze server nooit; maar wie een poort heeft
    /// opengezet of een VPN gebruikt geeft een adres op dat wél vanaf buiten bereikbaar is, en dan is
    /// uitlezen vanaf de server juist handiger, want dan hoeft er niemand thuis een app te openen.
    /// </summary>
    bool IsLokaalVoor(IReadOnlyDictionary<string, string> credentials) => IsLokaal;

    /// <summary>
    /// Kan dit merk überhaupt vanuit de app worden aangeleverd? Bepaalt of <c>POST /devices/{id}/readings</c>
    /// een meting van dit apparaat aanneemt. Ruimer dan <see cref="IsLokaalVoor"/>: een Sessy achter een
    /// opengezette poort wordt normaal door de server gelezen, maar als de app er thuis toch eentje
    /// opstuurt is dat geen fout en gooien we die meting niet weg.
    /// </summary>
    bool KanDoorDeAppGelezenWorden => IsLokaal;

    /// <summary>
    /// Zet het rauwe antwoord dat de app op het thuisnetwerk ophaalde om naar een meting.
    ///
    /// De omzetting hoort hier en niet in de app: een telefoon die een half jaar niet is bijgewerkt
    /// moet nog steeds bruikbare gegevens opsturen, en dat lukt alleen als hij het antwoord
    /// onvertaald doorgeeft en wij het uitleggen.
    /// </summary>
    DeviceReading LeesLokaal(System.Text.Json.JsonElement rauw, DateTimeOffset gemeten) =>
        throw new ConnectorException($"{Brand} wordt niet vanuit de app uitgelezen.");

    /// <summary>
    /// Kan dit merk ook aangestuurd worden, of alleen uitgelezen?
    ///
    /// Standaard niet. Sturen is iets anders dan kijken: het verandert iets in het huis van iemand
    /// anders, en dat doen we alleen bij merken waar we het hebben kunnen nakijken.
    /// </summary>
    bool KanSturen => false;

    /// <summary>
    /// Start of pauzeert het laden.
    ///
    /// Alleen aanroepen als de bewoner het heeft aangezet; die controle hoort bij de aanroeper,
    /// niet hier. Gooit <see cref="ConnectorException"/> met een zin die de bewoner iets zegt.
    /// </summary>
    Task StuurAsync(Device device, DeviceCredentials credentials, Laadopdracht opdracht, CancellationToken ct) =>
        throw new ConnectorException($"{Brand} kan niet worden aangestuurd.");

    /// <summary>Valideert de credentials en haalt de apparaten op.</summary>
    Task<ConnectResult> ConnectAsync(ConnectRequest request, CancellationToken ct);

    /// <summary>Leest één genormaliseerde meting. Gooit <see cref="ConnectorException"/> bij een fout.</summary>
    /// <param name="device">Het apparaat (voor id, type en externalId).</param>
    /// <param name="credentials">
    /// Ontsleutelde credentials inclusief eventuele extra's uit <see cref="DiscoveredDevice.ExtraCredentials"/>.
    /// Vernieuwt het merk onderweg een sleutel, zet die dan met <see cref="DeviceCredentials.Set"/>; de poller bewaart hem.
    /// </param>
    /// <param name="ct">Afbreektoken.</param>
    Task<DeviceReading> ReadAsync(Device device, DeviceCredentials credentials, CancellationToken ct);
}
