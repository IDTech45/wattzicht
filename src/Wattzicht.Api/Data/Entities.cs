using Wattzicht.Api.Services;
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

    /// <summary>Naam van de contactpersoon die zich aanmeldde.</summary>
    public string? ContactPerson { get; set; }

    /// <summary>Plaats, zodat het beheerpaneel een aanmelding kan plaatsen.</summary>
    public string? City { get; set; }

    /// <summary>Aantal klanten met zonnepanelen zoals bij de aanmelding opgegeven.</summary>
    public int? CustomerCount { get; set; }

    /// <summary>
    /// Wanneer Wattzicht deze installateur goedkeurde, of <c>null</c> zolang de aanmelding open staat.
    ///
    /// Een installateur kan zichzelf aanmelden, en dan staat hij met een paar klikken in het systeem.
    /// Zonder goedkeuring mag zijn code niet werken bij het aanmelden van bewoners: anders schrijft
    /// iemand zich in als "installateur" en haalt hij klanten naar zijn eigen portal. Hij kan intussen
    /// wel inloggen en zijn eigen gegevens invullen; hij ziet alleen nog geen klanten.
    /// </summary>
    public DateTimeOffset? ApprovedAt { get; set; }

    /// <summary>
    /// Wat deze installateur betaalt: <c>free</c> of <c>per_household</c>.
    ///
    /// Triple Energie staat op <c>free</c> en betaalt niets; die levert in ruil klanten. Hun bewoners
    /// krijgen twaalf maanden Plus cadeau. Bij <c>per_household</c> loopt het abonnement van de
    /// bewoner door zolang hij klant is, en zien we per maand wat er te factureren valt.
    /// </summary>
    public string BillingMode { get; set; } = InstallerBillingMode.Free;

    /// <summary>Prijs per actief huishouden per maand, exclusief btw.</summary>
    public decimal PricePerHousehold { get; set; } = 1.25m;

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

    /// <summary>
    /// Welk abonnement de bewoner koos: <c>installer</c> (zijn installateur betaalt), <c>monthly</c>
    /// of <c>yearly</c>. Dit legt alleen de keuze vast; er wordt nog niets afgeschreven, want er is
    /// nog geen betaalpartij aangesloten.
    /// </summary>
    public string Plan { get; set; } = "installer";

    /// <summary>Wanneer die keuze gemaakt is.</summary>
    public DateTimeOffset? PlanChosenAt { get; set; }

    /// <summary>
    /// Wanneer deze woning bij LaadCash is aangemeld. LaadCash meldt dit terug via de bestaande
    /// koppeling; zie docs/LAADCASH-KOPPELING.md. Zodra dit gevuld is stopt alle promotie en laat de
    /// tegel de echte opbrengst zien.
    /// </summary>
    public DateTimeOffset? LaadcashLinkedAt { get; set; }

    /// <summary>
    /// De bewoner wil er niets meer over horen. Dan nooit meer promoten, ook niet over een jaar.
    /// Iemand die "niet meer tonen" kiest en het toch weer ziet, zet de meldingen helemaal uit.
    /// </summary>
    public bool LaadcashOptOut { get; set; }

    /// <summary>
    /// De energieleverancier van deze woning, zoals die in supplier_tariffs staat. Alleen Plus
    /// rekent hiermee; zonder Plus geldt het landelijke gemiddelde.
    /// </summary>
    public string? SupplierName { get; set; }

    /// <summary>Locatie voor weerdata (afgeleid van de plaats; geen exact adres).</summary>
    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    /// <summary>Pushmelding bij een storing (standaard aan).</summary>
    public bool NotifyAlerts { get; set; } = true;

    /// <summary>Pushmelding als het huis flink teruglevert (standaard aan).</summary>
    public bool NotifySurplus { get; set; } = true;

    /// <summary>Laatste storingsmelding; max. één per 2 uur.</summary>
    public DateTimeOffset? LastAlertPushAt { get; set; }

    /// <summary>Laatste overschot-melding; voorkomt dat we vaker dan eens per paar uur sturen.</summary>
    public DateTimeOffset? LastSurplusPushAt { get; set; }

    /// <summary>
    /// Pushmelding vlak voordat het slimme laadmoment begint (standaard aan).
    ///
    /// De adviezen stonden er al, maar alleen voor wie de app toevallig opende. Een tip dat je
    /// tussen twee en drie uur het goedkoopst laadt is niets waard als je hem om vijf uur leest.
    /// </summary>
    public bool NotifySmartMoment { get; set; } = true;

    /// <summary>Laatste laadmoment-melding; hoogstens één per dag, zie PushPolicy.</summary>
    public DateTimeOffset? LastSmartMomentPushAt { get; set; }

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

    /// <summary>Zonnestraling die dag op de locatie (kWh/m², Open-Meteo); voor de prestatiecheck.</summary>
    public double? IrradianceKwhM2 { get; set; }

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

/// <summary>
/// Een telefoon waarop de app staat, zodat we er een pushmelding heen kunnen sturen (Firebase Cloud Messaging).
/// Het token hoort bij één installatie; opnieuw installeren geeft een nieuw token. Daarom is het token uniek
/// en niet het huishouden: iemand met telefoon en tablet heeft twee rijen.
/// </summary>
public sealed class PushDevice
{
    public Guid Id { get; set; }
    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;

    /// <summary>Registratietoken van dit toestel bij Google.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>"android" of "ios".</summary>
    public string Platform { get; set; } = "android";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}

/// <summary>
/// Eén abonnementsperiode van één woning: waar Plus vandaan komt en hoe lang het loopt.
///
/// Per woning is er hoogstens één rij actief. Dat staat ook als index in de database (zie
/// SchemaUpgrades), want twee actieve rijen zou betekenen dat twee partijen voor dezelfde woning
/// betalen, en dat is precies wat we nooit willen. Bij een bronwissel loopt de oude rij netjes uit
/// en begint de nieuwe daarna; afgelopen periodes blijven staan als geschiedenis.
/// </summary>
public sealed class Subscription
{
    public Guid Id { get; set; }

    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;

    /// <summary>trial, installer, apple, google of manual; zie <c>SubscriptionSource</c>.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Het nummer waaronder dit abonnement bij de bron bekend staat: het originele transactie-id bij
    /// Apple, de purchase token bij Google. Leeg bij een proefperiode of bij de installateur.
    /// Hierop herkennen we een melding van de store als een verlenging van een bestaande rij.
    /// </summary>
    public string? ExternalId { get; set; }

    /// <summary>Bij bron "installer": welke installateur betaalt.</summary>
    public Guid? InstallerId { get; set; }

    /// <summary>active, cancelled of expired; zie <c>SubscriptionStatus</c>.</summary>
    public string Status { get; set; } = string.Empty;

    public DateTimeOffset StartsAt { get; set; }

    /// <summary>Einde van deze periode, of <c>null</c> bij een doorlopend abonnement.</summary>
    public DateTimeOffset? EndsAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Of er al gewaarschuwd is dat de proefperiode bijna afloopt. Eén melding per proefperiode; zonder
    /// deze vlag zou de dagelijkse controle hem zeven dagen achter elkaar sturen.
    /// </summary>
    public DateTimeOffset? TrialReminderSentAt { get; set; }
}

/// <summary>
/// Eén keer dat we iemand iets over een campagne hebben laten zien, en wat hij deed.
///
/// Alle timing wordt hieruit afgeleid: of de popup al is geweest, hoeveel pushmeldingen er al uit
/// zijn en wanneer de laatste was. Zo kan hetzelfde bericht nooit twee keer komen, ook niet als er
/// twee servers draaien of een taak opnieuw begint.
/// </summary>
public sealed class PromoEvent
{
    public Guid Id { get; set; }

    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = null!;

    /// <summary>Welke campagne; nu alleen "laadcash".</summary>
    public string Campaign { get; set; } = string.Empty;

    /// <summary>popup, tile, push of email.</summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>shown, clicked, dismissed of optout.</summary>
    public string Action { get; set; } = string.Empty;

    public DateTimeOffset SentAt { get; set; }
}

/// <summary>
/// Wat één energieleverancier vanaf een datum betaalt voor teruglevering, en wat hij ervoor rekent.
///
/// Met de hand gevuld: deze bedragen staan in de voorwaarden van elke leverancier en veranderen een
/// paar keer per jaar, en er is geen bron die ze allemaal betrouwbaar bij elkaar zet. Een datum per
/// rij, zodat een oude berekening blijft kloppen: wie in maart terugleverde moet het tarief van
/// maart houden, ook als de leverancier het in juni verhoogt.
///
/// De rij met naam "standaard" is het landelijke gemiddelde dat Gratis krijgt.
/// </summary>
public sealed class SupplierTariff
{
    public Guid Id { get; set; }

    /// <summary>Naam van de leverancier, of "standaard" voor het landelijke gemiddelde.</summary>
    public string Supplier { get; set; } = string.Empty;

    /// <summary>Vanaf welke dag dit tarief geldt.</summary>
    public DateOnly ValidFrom { get; set; }

    /// <summary>Wat de leverancier per teruggeleverde kWh vergoedt.</summary>
    public decimal ExportPricePerKwh { get; set; }

    /// <summary>Wat de leverancier per teruggeleverde kWh in rekening brengt.</summary>
    public decimal FeedInCostPerKwh { get; set; }

    /// <summary>Waar dit vandaan komt, zodat het na te lopen is.</summary>
    public string? Source { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
