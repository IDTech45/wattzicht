namespace Wattzicht.Shared;

// ---------------------------------------------------------------------------
// Authenticatie
// ---------------------------------------------------------------------------

/// <summary>Inloggegevens van een gebruiker.</summary>
/// <param name="Email">E-mailadres (hoofdletterongevoelig).</param>
/// <param name="Password">Wachtwoord in platte tekst (alleen over TLS).</param>
public sealed record LoginRequest(string Email, string Password);

/// <summary>Registratie van een nieuw huishouden met eerste gebruiker.</summary>
/// <param name="Email">E-mailadres van de bewoner.</param>
/// <param name="Password">Gekozen wachtwoord (minimaal 8 tekens).</param>
/// <param name="HouseholdName">Naam van het huishouden, bijvoorbeeld "Familie Jansen".</param>
/// <param name="InstallerSlug">Optionele code van de installateur (white-label koppeling).</param>
public sealed record RegisterRequest(
    string Email,
    string Password,
    string HouseholdName,
    string? InstallerSlug = null);

/// <summary>Antwoord op een geslaagde login of registratie.</summary>
/// <param name="Token">JWT bearer-token.</param>
/// <param name="ExpiresAt">Moment waarop het token verloopt (UTC).</param>
/// <param name="Household">Het huishouden van de gebruiker; <c>null</c> voor een installateursaccount.</param>
/// <param name="Installer">White-label gegevens van de installateur (altijd gevuld; het portal gebruikt dit na login).</param>
public sealed record AuthResponse(
    string Token,
    DateTimeOffset ExpiresAt,
    HouseholdDto? Household,
    InstallerBrandingDto? Installer = null);

/// <summary>Ingelogde gebruiker met rol en huishouden.</summary>
public sealed record MeDto(
    Guid UserId,
    string Email,
    UserRole Role,
    HouseholdDto? Household,
    InstallerBrandingDto? Installer);

// ---------------------------------------------------------------------------
// Huishouden, installateur, apparaten
// ---------------------------------------------------------------------------

/// <summary>Een huishouden (één woning met apparaten).</summary>
/// <param name="Id">Unieke id.</param>
/// <param name="Name">Weergavenaam.</param>
/// <param name="PostalCode">Postcode (optioneel, voor zonverwachting later).</param>
/// <param name="City">Plaats (optioneel).</param>
/// <param name="InstallerId">Installateur waar dit huishouden bij hoort.</param>
/// <param name="InstallerName">Naam van die installateur (white-label).</param>
/// <param name="CreatedAt">Aanmaakmoment (UTC).</param>
public sealed record HouseholdDto(
    Guid Id,
    string Name,
    string? PostalCode,
    string? City,
    Guid InstallerId,
    string InstallerName,
    DateTimeOffset CreatedAt);

/// <summary>White-label instellingen van een installateur, zoals de app ze toont.</summary>
/// <param name="Id">Unieke id.</param>
/// <param name="Name">Bedrijfsnaam.</param>
/// <param name="Slug">Korte code voor in links en registratie.</param>
/// <param name="LogoDataUrl">Logo als <c>data:image/...;base64,</c>-URL of https-link; <c>null</c> zonder logo.</param>
/// <param name="PrimaryColor">Primaire kleur als hex, bijvoorbeeld <c>#0F172A</c>.</param>
/// <param name="AccentColor">Accentkleur als hex, bijvoorbeeld <c>#F59E0B</c>.</param>
/// <param name="QuoteUrl">Link naar de offertepagina van de installateur.</param>
/// <param name="Phone">Telefoonnummer voor bewoners (optioneel).</param>
/// <param name="Email">Contact-e-mailadres voor bewoners (optioneel).</param>
public sealed record InstallerBrandingDto(
    Guid Id,
    string Name,
    string Slug,
    string? LogoDataUrl,
    string PrimaryColor,
    string AccentColor,
    string? QuoteUrl,
    string? Phone = null,
    string? Email = null)
{
    /// <summary>Alias van <see cref="LogoDataUrl"/> voor clients die <c>logoUrl</c> verwachten.</summary>
    public string? LogoUrl => LogoDataUrl;
}

/// <summary>White-label instellingen zoals het portal ze toont en bewerkt (GET/PUT /installer/branding).</summary>
/// <param name="Name">Bedrijfsnaam.</param>
/// <param name="LogoDataUrl">Logo als <c>data:</c>-URL of https-link; <c>null</c> als er geen logo is.</param>
/// <param name="PrimaryColor">Primaire kleur als hex.</param>
/// <param name="QuoteUrl">Link naar de offertepagina.</param>
/// <param name="AccentColor">Accentkleur als hex (optioneel; standaard blijft staan).</param>
public sealed record BrandingDto(
    string Name,
    string? LogoDataUrl,
    string PrimaryColor,
    string? QuoteUrl,
    string? AccentColor = null);

/// <summary>Aanvraag van een installateur via het formulier op de website (POST /leads).</summary>
/// <param name="Name">Naam van de contactpersoon.</param>
/// <param name="Company">Bedrijfsnaam.</param>
/// <param name="Email">E-mailadres.</param>
/// <param name="Phone">Telefoonnummer (optioneel).</param>
/// <param name="CustomerCount">Aantal klanten (optioneel).</param>
/// <param name="Source">Waar het formulier stond, bijvoorbeeld "installateurs-pagina" (optioneel).</param>
public sealed record LeadRequest(
    string Name,
    string Company,
    string Email,
    string? Phone = null,
    int? CustomerCount = null,
    string? Source = null);

/// <summary>
/// Wijziging van de white-label instellingen (PUT /installer/branding). Niet-<c>null</c> velden veranderen;
/// uitzondering: <see cref="LogoDataUrl"/> <c>null</c> of leeg wist het logo (het portal stuurt altijd het hele object).
/// </summary>
public sealed record UpdateBrandingRequest(
    string? Name,
    string? LogoDataUrl,
    string? PrimaryColor,
    string? AccentColor,
    string? QuoteUrl,
    string? Phone = null,
    string? Email = null);

/// <summary>Een gekoppeld apparaat.</summary>
/// <param name="Id">Unieke id.</param>
/// <param name="Type">Soort apparaat.</param>
/// <param name="Brand">Merk.</param>
/// <param name="Name">Weergavenaam, bijvoorbeeld "Growatt MIN 3600".</param>
/// <param name="ExternalId">Id of serienummer bij de fabrikant.</param>
/// <param name="LastSeenAt">Laatste geslaagde meting (UTC); <c>null</c> als nog nooit uitgelezen.</param>
/// <param name="IsOnline">Of de laatste meting korter dan 30 minuten geleden is.</param>
public sealed record DeviceDto(
    Guid Id,
    DeviceType Type,
    DeviceBrand Brand,
    string Name,
    string ExternalId,
    DateTimeOffset? LastSeenAt,
    bool IsOnline)
{
    /// <summary>Alias van <see cref="LastSeenAt"/> (de app leest <c>lastSeen</c>).</summary>
    public DateTimeOffset? LastSeen => LastSeenAt;
}

/// <summary>Verzoek om een apparaat te koppelen.</summary>
/// <param name="Brand">Merk; bepaalt de connector.</param>
/// <param name="Type">Soort apparaat.</param>
/// <param name="Credentials">
/// Merkafhankelijke gegevens, bijvoorbeeld <c>token</c> voor Growatt
/// of <c>host</c>, <c>username</c>, <c>password</c> voor Sessy.
/// Worden versleuteld opgeslagen.
/// </param>
/// <param name="Name">Optionele eigen naam voor het apparaat.</param>
public sealed record ConnectDeviceRequest(
    DeviceBrand Brand,
    DeviceType Type,
    Dictionary<string, string> Credentials,
    string? Name = null);

// ---------------------------------------------------------------------------
// Metingen en dashboard
// ---------------------------------------------------------------------------

/// <summary>Actuele energiestromen van een huishouden (som van alle apparaten).</summary>
/// <param name="PvPowerW">Zonopwek in watt.</param>
/// <param name="LoadPowerW">Huisverbruik in watt.</param>
/// <param name="GridPowerW">Netvermogen in watt; positief = afname, negatief = teruglevering.</param>
/// <param name="BatteryPowerW">Batterijvermogen in watt; positief = ontladen, negatief = laden.</param>
/// <param name="BatterySoc">Laadtoestand batterij in procent (0–100); <c>null</c> zonder batterij.</param>
/// <param name="ChargerPowerW">Laadpaalvermogen in watt.</param>
/// <param name="UpdatedAt">Tijdstip van de meest recente meting (UTC).</param>
public sealed record LiveSnapshotDto(
    double PvPowerW,
    double LoadPowerW,
    double GridPowerW,
    double BatteryPowerW,
    double? BatterySoc,
    double ChargerPowerW,
    DateTimeOffset? UpdatedAt)
{
    /// <summary>Alias van <see cref="UpdatedAt"/> voor het portal (leest <c>timestamp</c>).</summary>
    public DateTimeOffset? Timestamp => UpdatedAt;
}

/// <summary>Energie en besparing over één dag of een opgetelde periode.</summary>
/// <param name="Date">De dag (bij een periode: de eerste dag).</param>
/// <param name="PvKwh">Opgewekt door zon.</param>
/// <param name="SelfUseKwh">Zelf verbruikte zonnestroom.</param>
/// <param name="ExportKwh">Teruggeleverd aan het net.</param>
/// <param name="ImportKwh">Afgenomen van het net.</param>
/// <param name="ChargedKwh">Geladen in de auto via de laadpaal.</param>
/// <param name="BatteryChargedKwh">In de batterij geladen.</param>
/// <param name="BatteryDischargedKwh">Uit de batterij ontladen.</param>
/// <param name="SavedEur">Bespaard bedrag in euro.</param>
public sealed record DailySummaryDto(
    DateOnly Date,
    double PvKwh,
    double SelfUseKwh,
    double ExportKwh,
    double ImportKwh,
    double ChargedKwh,
    double BatteryChargedKwh,
    double BatteryDischargedKwh,
    decimal SavedEur);

/// <summary>Een storing of melding op een apparaat.</summary>
/// <param name="Id">Unieke id.</param>
/// <param name="DeviceId">Apparaat waar de melding bij hoort.</param>
/// <param name="DeviceName">Naam van dat apparaat.</param>
/// <param name="Type">Soort melding.</param>
/// <param name="Message">Korte uitleg in gewone taal.</param>
/// <param name="Since">Sinds wanneer (UTC).</param>
/// <param name="ResolvedAt">Wanneer opgelost (UTC); <c>null</c> zolang de melding open staat.</param>
public sealed record AlertDto(
    Guid Id,
    Guid DeviceId,
    string DeviceName,
    AlertType Type,
    string Message,
    DateTimeOffset Since,
    DateTimeOffset? ResolvedAt);

/// <summary>Alles wat het startscherm van de app nodig heeft.</summary>
/// <param name="Live">Actuele energiestromen.</param>
/// <param name="Today">Vandaag.</param>
/// <param name="ThisMonth">Deze maand (opgeteld).</param>
/// <param name="ThisYear">Dit jaar (opgeteld).</param>
/// <param name="Alerts">Open meldingen.</param>
public sealed record DashboardDto(
    LiveSnapshotDto Live,
    DailySummaryDto Today,
    DailySummaryDto ThisMonth,
    DailySummaryDto ThisYear,
    IReadOnlyList<AlertDto> Alerts);

// ---------------------------------------------------------------------------
// Tarieven
// ---------------------------------------------------------------------------

/// <summary>Energietarief van een huishouden (GET/POST/PUT /tariff).</summary>
/// <param name="ImportTariffEur">Leveringstarief in euro per kWh (inclusief btw en energiebelasting).</param>
/// <param name="ExportTariffEur">Terugleververgoeding in euro per kWh (geldt na afloop van de saldering).</param>
/// <param name="IsDynamic">Of het een dynamisch contract is (uurprijzen); v0.1 rekent dan met het gemiddelde.</param>
/// <param name="ValidFrom">Vanaf welke dag dit tarief geldt; <c>null</c> bij opslaan = vandaag.</param>
/// <param name="PublicChargingTariffEur">Referentietarief publiek laden in euro per kWh (standaard 0,59).</param>
public sealed record TariffDto(
    decimal ImportTariffEur,
    decimal ExportTariffEur,
    bool IsDynamic,
    DateOnly? ValidFrom = null,
    decimal? PublicChargingTariffEur = null);

// ---------------------------------------------------------------------------
// Installateursportal
// ---------------------------------------------------------------------------

/// <summary>Eén huishouden in het overzicht van de installateur (GET /installer/households geeft hier een lijst van).</summary>
/// <param name="Id">Unieke id.</param>
/// <param name="CustomerName">Naam van het huishouden/de klant.</param>
/// <param name="City">Plaats (optioneel).</param>
/// <param name="PostalCode">Postcode (optioneel).</param>
/// <param name="Devices">Gekoppelde apparaten.</param>
/// <param name="Status">Samengevatte status.</param>
/// <param name="OpenAlerts">Aantal open meldingen.</param>
/// <param name="TodayPvKwh">Zonopwek vandaag.</param>
/// <param name="LastSeen">Laatste meting van welk apparaat dan ook (UTC).</param>
public sealed record InstallerHouseholdDto(
    Guid Id,
    string CustomerName,
    string? City,
    string? PostalCode,
    IReadOnlyList<DeviceDto> Devices,
    HouseholdStatus Status,
    int OpenAlerts,
    double TodayPvKwh,
    DateTimeOffset? LastSeen);

/// <summary>Overzicht met tellers voor het installateursportal (GET /installer/overview).</summary>
/// <param name="Installer">Eigen white-label instellingen.</param>
/// <param name="Households">Alle huishoudens van deze installateur met status.</param>
/// <param name="TotalOnline">Aantal huishoudens zonder problemen.</param>
/// <param name="TotalFault">Aantal huishoudens met open melding.</param>
/// <param name="TotalNoData">Aantal huishoudens zonder recente data.</param>
public sealed record InstallerOverviewDto(
    InstallerBrandingDto Installer,
    IReadOnlyList<InstallerHouseholdDto> Households,
    int TotalOnline,
    int TotalFault,
    int TotalNoData);

/// <summary>Detail van één huishouden voor de installateur (GET /installer/households/{id}).</summary>
/// <param name="Id">Unieke id.</param>
/// <param name="CustomerName">Naam.</param>
/// <param name="City">Plaats.</param>
/// <param name="PostalCode">Postcode.</param>
/// <param name="Email">E-mailadres van de (eerste) bewoner.</param>
/// <param name="Devices">Apparaten.</param>
/// <param name="Status">Status.</param>
/// <param name="OpenAlerts">Aantal open meldingen.</param>
/// <param name="TodayPvKwh">Zonopwek vandaag.</param>
/// <param name="LastSeen">Laatste meting.</param>
/// <param name="Live">Actuele energiestromen; <c>null</c> zonder metingen.</param>
/// <param name="Alerts">Meldingen van de laatste 30 dagen, open én opgelost.</param>
/// <param name="Today">Samenvatting van vandaag.</param>
/// <param name="Month">Samenvatting van deze maand.</param>
/// <param name="LastDays">Dagsamenvattingen van de laatste 14 dagen.</param>
public sealed record InstallerHouseholdDetailDto(
    Guid Id,
    string CustomerName,
    string? City,
    string? PostalCode,
    string? Email,
    IReadOnlyList<DeviceDto> Devices,
    HouseholdStatus Status,
    int OpenAlerts,
    double TodayPvKwh,
    DateTimeOffset? LastSeen,
    LiveSnapshotDto? Live,
    IReadOnlyList<AlertDto> Alerts,
    DailySummaryDto Today,
    DailySummaryDto Month,
    IReadOnlyList<DailySummaryDto> LastDays);

// ---------------------------------------------------------------------------
// Slim sturen (advies op basis van uurprijzen en zon)
// ---------------------------------------------------------------------------

/// <summary>Stroomprijs voor één uur.</summary>
/// <param name="Start">Begin van het uur (UTC).</param>
/// <param name="MarketEur">Kale marktprijs (day-ahead, incl. btw) in euro per kWh; kan negatief zijn.</param>
/// <param name="AllInEur">Geschatte prijs die een klant met dynamisch contract betaalt (markt + belasting + opslag).</param>
public sealed record HourPriceDto(DateTimeOffset Start, decimal MarketEur, decimal AllInEur);

/// <summary>Een aaneengesloten blok uren.</summary>
/// <param name="From">Begin (UTC).</param>
/// <param name="To">Einde (UTC, exclusief).</param>
/// <param name="AverageAllInEur">Gemiddelde all-in prijs in dat blok.</param>
public sealed record PriceWindowDto(DateTimeOffset From, DateTimeOffset To, decimal AverageAllInEur);

/// <summary>Eén concrete tip.</summary>
/// <param name="Kind">Soort tip (bepaalt de kleur in de app).</param>
/// <param name="Title">Korte opdracht, bv. "Laad de auto 12:00–15:00".</param>
/// <param name="Detail">Waarom, in één regel.</param>
/// <param name="From">Begin van het advies-blok (UTC), als er een tijd bij hoort.</param>
/// <param name="To">Einde van het advies-blok (UTC).</param>
/// <param name="SavingEur">Wat het vandaag oplevert in euro (0 als het nu nog niets scheelt).</param>
/// <param name="Note">Extra uitleg, bv. "Vanaf 2027 € 1,20 per dag".</param>
public sealed record AdviceTipDto(
    AdviceTipKind Kind,
    string Title,
    string Detail,
    DateTimeOffset? From,
    DateTimeOffset? To,
    decimal SavingEur,
    string? Note = null);

/// <summary>Slim-sturen-advies voor vandaag (GET /advice).</summary>
/// <param name="Date">Dag in Nederlandse tijd.</param>
/// <param name="IsDynamic">Of het huishouden een dynamisch contract heeft.</param>
/// <param name="Prices">Uurprijzen vanaf nu (vandaag en, na ±13:00, morgen).</param>
/// <param name="Cheapest">Goedkoopste 3 uur vanaf nu.</param>
/// <param name="MostExpensive">Duurste 3 uur vanaf nu.</param>
/// <param name="Tips">Tips, hoogste besparing eerst.</param>
/// <param name="TotalSavingEur">Som van de besparingen.</param>
/// <param name="PriceSource">Bron van de prijzen, of <c>null</c> als die niet beschikbaar waren.</param>
public sealed record AdviceDto(
    DateOnly Date,
    bool IsDynamic,
    List<HourPriceDto> Prices,
    PriceWindowDto? Cheapest,
    PriceWindowDto? MostExpensive,
    List<AdviceTipDto> Tips,
    decimal TotalSavingEur,
    string? PriceSource);
