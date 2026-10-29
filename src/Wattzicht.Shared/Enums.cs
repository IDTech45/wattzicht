using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wattzicht.Shared;

/// <summary>Soort apparaat dat Wattzicht uitleest.</summary>
public enum DeviceType
{
    /// <summary>Omvormer van zonnepanelen.</summary>
    Inverter = 0,

    /// <summary>Thuisbatterij.</summary>
    Battery = 1,

    /// <summary>Laadpaal voor de elektrische auto.</summary>
    Charger = 2,

    /// <summary>Slimme-meteruitlezer (P1-poort).</summary>
    P1Meter = 3
}

/// <summary>Merk/fabrikant van het apparaat; bepaalt welke connector wordt gebruikt.</summary>
public enum DeviceBrand
{
    Growatt = 0,
    Sessy = 1,
    Deye = 2,
    Alfen = 3,
    Zaptec = 4,
    HomeWizard = 5,

    /// <summary>Demo-apparaat met gesimuleerde data; geen echte verbinding.</summary>
    Demo = 99
}

/// <summary>Soort storing/melding.</summary>
public enum AlertType
{
    /// <summary>Al langer dan 30 minuten geen meting ontvangen.</summary>
    NoData = 0,

    /// <summary>Geen zonopwek overdag terwijl dat wel verwacht wordt.</summary>
    ZeroProduction = 1,

    /// <summary>Apparaat meldt zichzelf offline of de verbinding mislukt.</summary>
    Offline = 2
}

/// <summary>Rol van een ingelogde gebruiker.</summary>
public enum UserRole
{
    /// <summary>Bewoner van een huishouden.</summary>
    Resident = 0,

    /// <summary>Installateur; ziet alle huishoudens van zijn eigen bedrijf.</summary>
    Installer = 1
}

/// <summary>
/// Status van een huishouden zoals de installateur die ziet.
/// Wordt in JSON als kleine letters geserialiseerd (<c>online</c>, <c>fault</c>, <c>nodata</c>) voor het portal.
/// </summary>
[JsonConverter(typeof(LowerCaseEnumConverter<HouseholdStatus>))]
public enum HouseholdStatus
{
    /// <summary>Alles werkt, recente data aanwezig.</summary>
    Online = 0,

    /// <summary>Er staat minstens één open melding.</summary>
    Fault = 1,

    /// <summary>Geen enkel apparaat gekoppeld of al meer dan een dag geen data.</summary>
    NoData = 2
}

/// <summary>Serialiseert een enum als tekst in kleine letters; lezen is hoofdletterongevoelig.</summary>
public sealed class LowerCaseEnumConverter<TEnum>() : JsonStringEnumConverter<TEnum>(LowerCaseNamingPolicy.Instance, allowIntegerValues: true)
    where TEnum : struct, Enum;

/// <summary>Naamgevingsbeleid: alles in kleine letters.</summary>
public sealed class LowerCaseNamingPolicy : JsonNamingPolicy
{
    public static LowerCaseNamingPolicy Instance { get; } = new();

    public override string ConvertName(string name) => name.ToLowerInvariant();
}
