using Wattzicht.Shared;

namespace Wattzicht.Api.Data;

/// <summary>Installateur (tenant). Elk huishouden en elke gebruiker hoort bij precies één installateur.</summary>
public sealed class Installer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Korte code voor in links en bij registratie, bijvoorbeeld <c>triple-energie</c>.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Logo als https-link of <c>data:image/...;base64,</c>-URL.</summary>
    public string? LogoUrl { get; set; }

    public string PrimaryColor { get; set; } = "#0F172A";
    public string AccentColor { get; set; } = "#F59E0B";
    public string? QuoteUrl { get; set; }
    public string? Phone { get; set; }
    public string? ContactEmail { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public List<Household> Households { get; set; } = [];
    public List<User> Users { get; set; } = [];
}

/// <summary>Huishouden: één woning met apparaten, tarieven en samenvattingen.</summary>
public sealed class Household
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public Guid InstallerId { get; set; }
    public Installer Installer { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Toestemming van de bewoner: mag de installateur meekijken? Standaard uit (AVG: opt-in).
    /// Zonder toestemming ziet de installateur dit huishouden niet in het portal.
    /// </summary>
    public bool InstallerAccessGranted { get; set; }

    /// <summary>Wanneer de bewoner de toestemming voor het laatst aan- of uitzette.</summary>
    public DateTimeOffset? InstallerAccessChangedAt { get; set; }

    public List<User> Users { get; set; } = [];
    public List<Device> Devices { get; set; } = [];
    public List<Tariff> Tariffs { get; set; } = [];
    public List<DailySummary> DailySummaries { get; set; } = [];
}

/// <summary>Gebruiker. Bewoners horen bij een huishouden; installateurs alleen bij een installateur.</summary>
public sealed class User
{
    public Guid Id { get; set; }

    /// <summary>E-mailadres, altijd in kleine letters opgeslagen.</summary>
    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public Guid? HouseholdId { get; set; }
    public Household? Household { get; set; }
    public Guid InstallerId { get; set; }
    public Installer Installer { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Gekoppeld apparaat met versleutelde toegangsgegevens.</summary>
public sealed class Device
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;
    public DeviceType Type { get; set; }
    public DeviceBrand Brand { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Serienummer of id bij de fabrikant.</summary>
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>JSON-dictionary met credentials, versleuteld via <see cref="Services.CredentialProtector"/>.</summary>
    public string EncryptedCredentials { get; set; } = string.Empty;

    /// <summary>Laatste geslaagde uitlezing (UTC).</summary>
    public DateTimeOffset? LastSeenAt { get; set; }

    /// <summary>Laatste foutmelding bij uitlezen; <c>null</c> als de laatste poging slaagde.</summary>
    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<Reading> Readings { get; set; } = [];
    public List<Alert> Alerts { get; set; } = [];
}

/// <summary>
/// Eén genormaliseerde meting van een apparaat. Tekenconventie:
/// grid positief = afname, negatief = teruglevering; batterij positief = ontladen, negatief = laden.
/// </summary>
public sealed class Reading
{
    public long Id { get; set; }
    public Guid DeviceId { get; set; }
    public Device Device { get; set; } = null!;
    public DateTimeOffset Timestamp { get; set; }

    public double PvPowerW { get; set; }
    public double LoadPowerW { get; set; }
    public double GridPowerW { get; set; }
    public double BatteryPowerW { get; set; }
    public double? BatterySoc { get; set; }
    public double ChargerPowerW { get; set; }

    // Energietellers (cumulatief, in Wh) zoals de fabrikant ze meldt; null als onbekend.
    public double? PvEnergyWh { get; set; }
    public double? ImportEnergyWh { get; set; }
    public double? ExportEnergyWh { get; set; }
    public double? ChargerEnergyWh { get; set; }
    public double? BatteryChargeEnergyWh { get; set; }
    public double? BatteryDischargeEnergyWh { get; set; }
}

/// <summary>Energietarief van een huishouden vanaf een bepaalde datum.</summary>
public sealed class Tariff
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;
    public DateOnly ValidFrom { get; set; }

    /// <summary>Leveringstarief in euro per kWh, all-in.</summary>
    public decimal ImportPricePerKwh { get; set; }

    /// <summary>Terugleververgoeding in euro per kWh (geldt pas na afloop van de saldering).</summary>
    public decimal ExportPricePerKwh { get; set; }

    public bool IsDynamic { get; set; }

    /// <summary>
    /// Terugleverkosten van de leverancier in euro per teruggeleverde kWh (staat op het contract).
    /// Bij een staffel: jaarbedrag gedeeld door de teruggeleverde kWh.
    /// </summary>
    public decimal FeedInCostPerKwh { get; set; }

    /// <summary>Referentietarief publiek laden in euro per kWh.</summary>
    public decimal PublicChargingPricePerKwh { get; set; } = 0.59m;
}

/// <summary>Dagtotalen per huishouden; wordt elke pollronde bijgewerkt.</summary>
public sealed class DailySummary
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;

    /// <summary>Kalenderdag in Nederlandse tijd.</summary>
    public DateOnly Date { get; set; }

    public double PvKwh { get; set; }
    public double SelfUseKwh { get; set; }
    public double ExportKwh { get; set; }
    public double ImportKwh { get; set; }
    public double ChargedKwh { get; set; }
    public double BatteryChargedKwh { get; set; }
    public double BatteryDischargedKwh { get; set; }

    /// <summary>Deel van de batterijlading dat van het net kwam (voor de besparingsregel).</summary>
    public double BatteryChargedFromGridKwh { get; set; }

    public decimal SavedEur { get; set; }

    /// <summary>Tijdstip van de laatst verwerkte meting; nodig om vermogen naar energie te integreren.</summary>
    public DateTimeOffset? LastSampleAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Aanvraag van een installateur via de website ("Doe mee als installateur").</summary>
public sealed class Lead
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }

    /// <summary>Aantal klanten zoals opgegeven op de site.</summary>
    public int? CustomerCount { get; set; }

    /// <summary>Herkomst van het formulier (bijvoorbeeld "installateurs-pagina").</summary>
    public string? Source { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Storing of melding op een apparaat.</summary>
public sealed class Alert
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public Device Device { get; set; } = null!;
    public AlertType Type { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset Since { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    public bool IsOpen => ResolvedAt is null;
}

/// <summary>
/// Logboek: wanneer een medewerker van de installateur gegevens van een huishouden bekeek.
/// De bewoner kan dit zelf inzien (GET /household/access-log).
/// </summary>
public sealed class InstallerAccessLog
{
    public long Id { get; set; }
    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;
    public Guid InstallerId { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Wat er gebeurde, bv. <c>view_detail</c>.</summary>
    public string Action { get; set; } = string.Empty;

    public DateTimeOffset At { get; set; }
}
