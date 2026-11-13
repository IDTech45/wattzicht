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
/// <param name="AllowInstallerAccess">Bewoner geeft toestemming dat de installateur meekijkt (standaard nee).</param>
/// <summary>Aanmelding van een installateur op de website.</summary>
/// <param name="Company">Bedrijfsnaam.</param>
/// <param name="ContactPerson">Naam van de contactpersoon.</param>
/// <param name="Email">E-mailadres waarmee hij inlogt.</param>
/// <param name="Password">Zelf gekozen wachtwoord, minimaal acht tekens.</param>
/// <param name="Phone">Telefoonnummer (optioneel).</param>
/// <param name="City">Plaats (optioneel).</param>
/// <param name="CustomerCount">Hoeveel klanten met zonnepanelen hij ongeveer heeft (optioneel).</param>
public sealed record RegisterInstallerRequest(
    string Company,
    string ContactPerson,
    string Email,
    string Password,
    string? Phone = null,
    string? City = null,
    int? CustomerCount = null);

public sealed record RegisterRequest(
    string Email,
    string Password,
    string HouseholdName,
    string? InstallerSlug = null,
    bool AllowInstallerAccess = false,
    string? Plan = null);

/// <summary>Je eigen wachtwoord wijzigen.</summary>
/// <param name="Huidig">Het wachtwoord van nu; zonder dat verandert er niets.</param>
/// <param name="Nieuw">Het nieuwe wachtwoord.</param>
public sealed record WachtwoordWijzigenRequest(string Huidig, string Nieuw);

/// <summary>Inloggen of aanmelden met een token van Google of Apple.</summary>
/// <param name="Provider">"google" of "apple".</param>
/// <param name="IdToken">Het ondertekende token dat het toestel van die partij kreeg.</param>
/// <param name="HouseholdName">Naam voor de woning als het account nog niet bestaat; leeg wordt "Thuis".</param>
public sealed record ExternalLoginRequest(
    string Provider,
    string IdToken,
    string? HouseholdName = null);

/// <summary>Antwoord op een geslaagde login of registratie.</summary>
/// <param name="Token">JWT bearer-token.</param>
/// <param name="ExpiresAt">Moment waarop het token verloopt (UTC).</param>
/// <param name="Household">Het huishouden van de gebruiker; <c>null</c> voor een installateursaccount.</param>
/// <param name="Installer">White-label gegevens van de installateur (altijd gevuld; het portal gebruikt dit na login).</param>
public sealed record AuthResponse(
    string Token,
    DateTimeOffset ExpiresAt,
    HouseholdDto? Household,
    InstallerBrandingDto? Installer = null,
    // De rol zit al in het token, maar de app en de website lezen dat token niet uit. Zonder deze
    // regel weet de website na het inloggen niet of ze de beheerder of een installateur voor zich
    // heeft, en kan ze hem niet naar het juiste scherm sturen.
    UserRole Role = UserRole.Resident);

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
    DateTimeOffset CreatedAt,
    bool InstallerAccessGranted = false,
    /// <summary>Piekvermogen van de panelen in kWp: van de omvormer, of zoals de bewoner het invulde.</summary>
    double? PvPeakKw = null,
    /// <summary>Het aantal panelen zoals de omvormer het meldt; null als het merk dat niet vertelt.</summary>
    int? PvPanelCount = null,
    /// <summary>
    /// Of we een plaats hebben kunnen vinden. Zonder plaats geen zonverwachting, en dat is precies
    /// wat de app moet kunnen zeggen in plaats van alleen "geen verwachting beschikbaar".
    /// </summary>
    bool HasLocation = false,
    /// <summary>
    /// Wat wij uit de eigen metingen als piekvermogen schatten; null bij te weinig zonnige dagen.
    /// De app biedt dit aan in plaats van de bewoner naar zijn offerte van jaren geleden te sturen.
    /// </summary>
    double? PvPeakKwGeschat = null);

/// <summary>Wat de bewoner over zijn woning kan instellen.</summary>
/// <param name="Name">Naam van het huishouden, bijvoorbeeld Thuis.</param>
/// <param name="PostalCode">Postcode; alleen de vier cijfers en de letters worden gebruikt.</param>
/// <param name="City">Plaats; mag leeg als de postcode er staat.</param>
/// <param name="PvPeakKw">Piekvermogen van de panelen in kWp.</param>
public sealed record UpdateHouseholdRequest(
    string? Name = null,
    string? PostalCode = null,
    string? City = null,
    double? PvPeakKw = null);

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
    bool IsOnline,
    string? LastError = null,
    /// <summary>Of dit merk gestart en gepauzeerd kan worden.</summary>
    bool KanSturen = false,
    /// <summary>Of de bewoner dat ook heeft aangezet. Standaard uit.</summary>
    bool SturenToegestaan = false)
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

/// <summary>
/// Eén meting die de app zelf op het thuisnetwerk heeft opgehaald, bijvoorbeeld van een P1-meter.
/// De server kan zo'n kastje niet bereiken, dus de telefoon doet het en stuurt dit door.
/// </summary>
/// <param name="Raw">Het antwoord van het kastje zoals het was. De server doet de omzetting, zodat
/// een oudere app niet met een eigen uitleg van de velden aan komt zetten.</param>
/// <param name="MeasuredAt">Wanneer de app het uitlas. Mag niet in de toekomst liggen.</param>
public sealed record LocalReadingRequest(
    System.Text.Json.JsonElement Raw,
    DateTimeOffset? MeasuredAt = null);

/// <summary>Eén paneel van een installatie (GET /devices/{id}/panels).</summary>
/// <param name="Serial">Het serienummer van de micro-omvormer; het enige wat ze uit elkaar houdt.</param>
/// <param name="Nummer">Volgnummer in de lijst, zodat er iets te noemen valt dat geen serienummer is.</param>
/// <param name="Watt">Wat dit paneel als laatste meldde.</param>
/// <param name="MaxWatt">Het hoogste dat dit paneel ooit meldde; de maat waarlangs we hem leggen.</param>
/// <param name="GemeldOp">Wanneer dat was.</param>
/// <param name="BlijftAchter">Of dit paneel duidelijk minder doet dan de rest op dit moment.</param>
public sealed record PaneelDto(
    string Serial,
    int Nummer,
    double Watt,
    double MaxWatt,
    DateTimeOffset? GemeldOp,
    bool BlijftAchter);

/// <summary>Alle panelen van één omvormer, met wat er over het geheel te zeggen valt.</summary>
/// <param name="Panelen">De panelen, op volgorde van opbrengst; de beste bovenaan.</param>
/// <param name="TotaalWatt">Wat ze samen op dit moment maken.</param>
/// <param name="BijgewerktOp">Wanneer de app dit voor het laatst van de omvormer ophaalde.</param>
/// <param name="Achterblijvers">Hoeveel panelen er duidelijk minder doen dan de rest.</param>
public sealed record PanelenDto(
    IReadOnlyList<PaneelDto> Panelen,
    double TotaalWatt,
    DateTimeOffset? BijgewerktOp,
    int Achterblijvers);

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
/// <param name="HasBattery">Of deze woning een thuisbatterij heeft.</param>
/// <param name="HasCharger">Of deze woning een laadpaal heeft.</param>
public sealed record LiveSnapshotDto(
    double PvPowerW,
    double LoadPowerW,
    double GridPowerW,
    double BatteryPowerW,
    double? BatterySoc,
    double ChargerPowerW,
    DateTimeOffset? UpdatedAt,
    bool HasBattery = false,
    bool HasCharger = false)
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
/// <summary>Eén bedrag dat we in een contract of afrekening vonden, met de regel waar het uit komt.</summary>
/// <param name="EuroPerKwh">Het bedrag per kilowattuur.</param>
/// <param name="Regel">De regel uit het document; zo kan de bewoner zien wat wij gelezen hebben.</param>
public sealed record TariefVondstDto(decimal EuroPerKwh, string Regel);

/// <summary>
/// Wat we uit een energiecontract of jaarafrekening konden halen.
///
/// Dit is een voorstel en geen instelling: niets wordt opgeslagen tot de bewoner het overneemt. Elke
/// leverancier schrijft zijn afrekening anders, en een bedrag dat er ongemerkt naast zit maakt elke
/// besparing op elk scherm verkeerd.
/// </summary>
/// <param name="Levering">Wat je per kilowattuur betaalt.</param>
/// <param name="Teruglevering">Wat je per teruggeleverde kilowattuur krijgt.</param>
/// <param name="Terugleverkosten">Wat teruglevering per kilowattuur kost.</param>
/// <param name="Energiebelasting">Apart vermelde energiebelasting; dan is de leveringsprijs nog niet compleet.</param>
/// <param name="PrijsIsAllIn">Of de leveringsprijs volgens het document al inclusief belastingen is.</param>
public sealed record TariefVoorstelDto(
    TariefVondstDto? Levering,
    TariefVondstDto? Teruglevering,
    TariefVondstDto? Terugleverkosten,
    TariefVondstDto? Energiebelasting,
    bool PrijsIsAllIn);

/// <summary>Eén maand uit het overzicht van de leverancier, naast wat wij zelf maten.</summary>
/// <param name="Maand">De eerste dag van die maand.</param>
/// <param name="AfnameKwh">Wat de leverancier zegt dat je van het net haalde.</param>
/// <param name="TerugleveringKwh">Wat je volgens hem teruglerverde; null als het er niet bij stond.</param>
/// <param name="OnzeAfnameKwh">Wat wij in die maand zelf maten; null als we die maand niets hebben.</param>
public sealed record LeverancierMaandDto(
    DateOnly Maand,
    double AfnameKwh,
    double? TerugleveringKwh,
    double? OnzeAfnameKwh);

/// <summary>Uitkomst van het inlezen van een maandoverzicht van de leverancier.</summary>
public sealed record VerbruikImportDto(IReadOnlyList<LeverancierMaandDto> Maanden);

/// <summary>Wat één batterijmaat bij deze woning zou opleveren.</summary>
/// <param name="Kwh">De maat van de batterij.</param>
/// <param name="PerJaarEur">Per jaar, met het tarief dat nu geldt.</param>
/// <param name="PerJaarNaSalderingEur">Per jaar, met de tarieven zoals ze vanaf 2027 gelden.</param>
/// <param name="VerplaatstPerDagKwh">Hoeveel er gemiddeld per dag door de batterij zou gaan.</param>
public sealed record BatterijMaatDto(double Kwh, decimal PerJaarEur, decimal PerJaarNaSalderingEur, double VerplaatstPerDagKwh);

/// <summary>
/// Het batterijadvies, gerekend met de eigen gemeten dagen van deze woning.
/// </summary>
/// <param name="Maten">Per maat wat het zou schelen; leeg als er te weinig dagen zijn.</param>
/// <param name="Dagen">Over hoeveel gemeten dagen dit gaat; eerlijk erbij, want daar hangt alles aan.</param>
/// <param name="HeeftAlBatterij">Of er al een batterij gekoppeld is; dan is dit niet interessant.</param>
public sealed record BatterijAdviesDto(IReadOnlyList<BatterijMaatDto> Maten, int Dagen, bool HeeftAlBatterij);

/// <summary>Een apparaat een eigen naam geven.</summary>
/// <param name="Naam">De naam zoals de bewoner hem wil zien, bijvoorbeeld "Dak voorkant".</param>
public sealed record ApparaatHernoemenRequest(string Naam);

/// <summary>Verzoek om een laadpaal te starten of te pauzeren.</summary>
/// <param name="Start">True is laden, false is pauzeren.</param>
public sealed record LaadopdrachtRequest(bool Start);

/// <summary>Verzoek om het sturen van een paal aan of uit te zetten.</summary>
public sealed record SturenRequest(bool Toestaan);

/// <summary>Uitkomst van "nu uitlezen".</summary>
/// <param name="Gelukt">Apparaten die een meting opleverden.</param>
/// <param name="Mislukt">Apparaten die niet te bereiken waren of een fout gaven.</param>
/// <param name="Reden">De laatste foutmelding, als die er is; anders leeg.</param>
public sealed record UitleesResultaatDto(int Gelukt, int Mislukt, string? Reden);

/// <summary>Uitkomst van het inlezen van een export uit het portaal van een fabrikant.</summary>
/// <param name="Dagen">Aantal dagen dat erbij is gekomen.</param>
/// <param name="Overgeslagen">Dagen die we al zelf gemeten hadden; die laten we met rust.</param>
/// <param name="Van">Eerste dag uit het bestand.</param>
/// <param name="Tot">Laatste dag uit het bestand.</param>
/// <param name="TotaalKwh">Opwek in het hele bestand, om te kunnen controleren of het klopt.</param>
public sealed record HistorieImportDto(
    int Dagen,
    int Overgeslagen,
    DateOnly Van,
    DateOnly Tot,
    double TotaalKwh);

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
/// <param name="FeedInCostEur">Terugleverkosten in euro per teruggeleverde kWh; <c>null</c> bij opslaan = vorige waarde houden.</param>
public sealed record TariffDto(
    decimal ImportTariffEur,
    decimal ExportTariffEur,
    bool IsDynamic,
    DateOnly? ValidFrom = null,
    decimal? PublicChargingTariffEur = null,
    decimal? FeedInCostEur = null);

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
    DateTimeOffset? LastSeen,
    PerformanceStatus Performance = PerformanceStatus.Unknown,
    IReadOnlyList<OpportunityDto>? Opportunities = null);

/// <summary>Waarom een installatie aandacht nodig heeft.</summary>
public enum AandachtSoort
{
    /// <summary>Geen enkel apparaat heeft recent nog iets gestuurd.</summary>
    Stil = 0,

    /// <summary>De panelen leveren minder dan het weer van die dagen toeliet.</summary>
    LeverT = 1,
}

/// <summary>
/// Eén installatie die aandacht nodig heeft, voor het installateursportaal.
/// </summary>
/// <param name="HouseholdId">De woning.</param>
/// <param name="Naam">Naam van het huishouden.</param>
/// <param name="Plaats">Plaats, als die bekend is.</param>
/// <param name="Soort">Stil of levert te weinig.</param>
/// <param name="LaatstGezien">Wanneer er voor het laatst iets binnenkwam.</param>
/// <param name="PercentVanVerwacht">Bij "levert te weinig": hoeveel procent van wat het weer toeliet.</param>
/// <param name="Toelichting">Eén zin die zegt wat er aan de hand is.</param>
public sealed record AandachtDto(
    Guid HouseholdId,
    string Naam,
    string? Plaats,
    AandachtSoort Soort,
    DateTimeOffset? LaatstGezien,
    int? PercentVanVerwacht,
    string? Toelichting);

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
    IReadOnlyList<DailySummaryDto> LastDays,
    PerformanceDto? Performance = null,
    IReadOnlyList<OpportunityDto>? Opportunities = null);

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

/// <summary>Hoe de dag eruitziet, voor de kaart op het startscherm.</summary>
/// <param name="Headline">Eén regel over vandaag, bijvoorbeeld "Vandaag veel zon".</param>
/// <param name="Body">Wat je er concreet mee kunt, met het bedrag of het aantal kWh erin.</param>
/// <param name="ExpectedSolarKwh">Wat de panelen vandaag naar verwachting maken.</param>
/// <param name="Kind">zon, prijs of niets; waarop deze tip gebaseerd is.</param>
/// <param name="From">Begin van het aangeraden venster.</param>
/// <param name="To">Einde van dat venster.</param>
/// <param name="SavingEur">Wat het ongeveer scheelt; nul als er niets te verdienen valt.</param>
public sealed record DayTipDto(
    string Headline,
    string Body,
    double ExpectedSolarKwh,
    string Kind,
    DateTimeOffset? From,
    DateTimeOffset? To,
    decimal SavingEur);

/// <summary>Slim-sturen-advies voor vandaag (GET /advice).</summary>
/// <param name="Date">Dag in Nederlandse tijd.</param>
/// <param name="IsDynamic">Of het huishouden een dynamisch contract heeft.</param>
/// <param name="Prices">Uurprijzen vanaf nu (vandaag en, na ±13:00, morgen).</param>
/// <param name="Cheapest">Goedkoopste 3 uur vanaf nu.</param>
/// <param name="MostExpensive">Duurste 3 uur vanaf nu.</param>
/// <param name="Tips">Tips, hoogste besparing eerst.</param>
/// <param name="TotalSavingEur">Som van de besparingen.</param>
/// <param name="PriceSource">Bron van de prijzen, of <c>null</c> als die niet beschikbaar waren.</param>
/// <param name="ExpectedSolarKwh">Wat je panelen vandaag naar verwachting maken; 0 zonder verwachting.</param>
/// <param name="SunFrom">Begin van het zonnigste blok van vandaag.</param>
/// <param name="SunTo">Einde daarvan.</param>
/// <param name="SolarSource">Waar die verwachting vandaan komt, of <c>null</c> als we er geen hebben.</param>
public sealed record AdviceDto(
    DateOnly Date,
    bool IsDynamic,
    List<HourPriceDto> Prices,
    PriceWindowDto? Cheapest,
    PriceWindowDto? MostExpensive,
    List<AdviceTipDto> Tips,
    decimal TotalSavingEur,
    string? PriceSource,
    double ExpectedSolarKwh = 0,
    DateTimeOffset? SunFrom = null,
    DateTimeOffset? SunTo = null,
    string? SolarSource = null);

// ---------------------------------------------------------------------------
// Toestemming installateur (meekijken) en inzagelog
// ---------------------------------------------------------------------------

/// <summary>Of de installateur mag meekijken bij dit huishouden.</summary>
/// <param name="Granted">Toestemming aan of uit.</param>
/// <param name="ChangedAt">Laatst gewijzigd (UTC).</param>
/// <param name="InstallerName">Naam van de installateur waar het om gaat.</param>
public sealed record InstallerAccessDto(bool Granted, DateTimeOffset? ChangedAt, string InstallerName);

/// <summary>Waar de bewoner naartoe moet om Enphase toestemming te geven.</summary>
/// <param name="Url">De toestemmingspagina van Enphase, met onze client-id en het terugkeeradres erin.</param>
/// <param name="RedirectUri">Het terugkeeradres dat erin zit; exact dit moet straks mee bij het koppelen.</param>
public sealed record EnphaseAuthorizeUrlDto(string Url, string RedirectUri);

/// <summary>Vraag om de Enphase-toestemmingspagina.</summary>
/// <param name="RedirectUri">Gewenst terugkeeradres; leeg = het eerste adres van onze lijst.</param>
public sealed record EnphaseAuthorizeUrlQuery(string? RedirectUri);

/// <summary>Toestemming aan- of uitzetten (PUT /household/installer-access).</summary>
public sealed record UpdateInstallerAccessRequest(bool Granted);

/// <summary>Eén keer dat de installateur gegevens bekeek.</summary>
/// <param name="At">Moment (UTC).</param>
/// <param name="Action">Wat er bekeken werd, bv. <c>view_detail</c>.</param>
/// <param name="Who">E-mailadres van de medewerker.</param>
public sealed record AccessLogEntryDto(DateTimeOffset At, string Action, string? Who);

// ---------------------------------------------------------------------------
// Terugleveren: wat kost het en wat scheelt zelf gebruiken
// ---------------------------------------------------------------------------

/// <summary>Overzicht teruglevering en terugleverkosten (GET /feed-in).</summary>
/// <param name="FeedInCostPerKwh">Ingestelde terugleverkosten in euro per kWh (0 = niet ingevuld).</param>
/// <param name="MonthExportKwh">Teruggeleverd deze maand.</param>
/// <param name="MonthCostEur">Terugleverkosten deze maand.</param>
/// <param name="YearExportKwh">Teruglevering per jaar (laatste 365 dagen, of geschat uit minder dagen).</param>
/// <param name="YearIsEstimate">Of het jaargetal is doorgerekend uit minder dan een jaar data.</param>
/// <param name="YearCostEur">Terugleverkosten per jaar met het huidige tarief.</param>
/// <param name="SelfUseWorthNowPerKwh">Wat elke kWh zelf gebruiken in plaats van terugleveren nu scheelt.</param>
/// <param name="SelfUseWorth2027PerKwh">Hetzelfde vanaf 2027, als de saldering stopt.</param>
/// <param name="YearValue2027Eur">Wat het vanaf 2027 per jaar scheelt als je al die stroom zelf gebruikt.</param>
/// <param name="LiveExportW">Wat je nu teruglevert in watt (0 als je niets teruglevert of er geen verse meting is).</param>
public sealed record FeedInDto(
    decimal FeedInCostPerKwh,
    double MonthExportKwh,
    decimal MonthCostEur,
    double YearExportKwh,
    bool YearIsEstimate,
    decimal YearCostEur,
    decimal SelfUseWorthNowPerKwh,
    decimal SelfUseWorth2027PerKwh,
    decimal YearValue2027Eur,
    double LiveExportW);

// ---------------------------------------------------------------------------
// Pushmeldingen
// ---------------------------------------------------------------------------

/// <summary>Toestel aanmelden voor pushmeldingen (POST /push/devices). De app stuurt dit bij elke start.</summary>
/// <param name="Token">FCM-registratietoken van dit toestel.</param>
/// <param name="Platform">"android" of "ios".</param>
public sealed record RegisterPushDeviceRequest(string Token, string? Platform = null);

/// <summary>Welke pushmeldingen de bewoner wil krijgen.</summary>
/// <param name="Alerts">Bij een storing.</param>
/// <param name="Surplus">Als het huis flink teruglevert (max. eens per 3 uur, tussen 9 en 20 uur).</param>
/// <param name="Alerts">Melding bij een storing.</param>
/// <param name="Surplus">Tip als je veel teruglevert.</param>
/// <param name="Monthly">Eén bericht per maand met wat die maand heeft gedaan.</param>
public sealed record NotificationSettingsDto(bool Alerts, bool Surplus, bool Monthly = true);

// ---------------------------------------------------------------------------
// Duurzaamheid, prestatiecheck en kansen
// ---------------------------------------------------------------------------

/// <summary>Levert de installatie wat het weer toeliet?</summary>
/// <param name="Status">Ok, Low (structureel te weinig) of Unknown (te weinig data).</param>
/// <param name="PercentOfExpected">Opwek van de laatste zonnige dagen als % van wat deze installatie normaal haalt bij dat weer.</param>
/// <param name="LostKwh7Days">Geschat gemist in de laatste 7 dagen.</param>
/// <param name="LostEur7Days">Wat dat ongeveer kostte.</param>
/// <param name="Message">Uitleg in één zin.</param>
public sealed record PerformanceDto(
    PerformanceStatus Status,
    int? PercentOfExpected,
    double LostKwh7Days,
    decimal LostEur7Days,
    string Message);

/// <summary>Duurzaamheid dit jaar (GET /sustainability).</summary>
/// <param name="Year">Kalenderjaar.</param>
/// <param name="PvKwh">Opgewekt dit jaar.</param>
/// <param name="Co2AvoidedKg">Vermeden CO₂ (opwek × uitstoot van stroom uit het net; schatting).</param>
/// <param name="SelfSufficiencyPercent">Deel van je verbruik dat uit eigen zon (en batterij) kwam.</param>
/// <param name="SelfConsumptionPercent">Deel van je opwek dat je zelf gebruikte in plaats van terugleverde.</param>
/// <param name="Performance">Prestatiecheck van de panelen.</param>
public sealed record SustainabilityDto(
    int Year,
    double PvKwh,
    double Co2AvoidedKg,
    int SelfSufficiencyPercent,
    int SelfConsumptionPercent,
    PerformanceDto Performance);

/// <summary>Een kans voor de installateur (en besparing voor de klant).</summary>
/// <param name="Kind">Soort kans.</param>
/// <param name="Title">Korte titel, bv. "Thuisbatterij".</param>
/// <param name="Detail">Waarom, in één zin.</param>
/// <param name="YearValueEur">Wat het de klant per jaar oplevert (vanaf 2027), als dat te schatten is.</param>
public sealed record OpportunityDto(OpportunityKind Kind, string Title, string Detail, decimal? YearValueEur);

/// <summary>Bericht van de installateur aan de klant; komt als pushmelding binnen.</summary>
/// <param name="Text">Max. 300 tekens.</param>
public sealed record InstallerMessageRequest(string Text);

/// <summary>Resultaat van een actie op afstand.</summary>
/// <param name="Ok">Gelukt.</param>
/// <param name="Message">Uitleg voor in het portal.</param>
public sealed record InstallerActionResult(bool Ok, string Message);

// ---------------------------------------------------------------------------------------------
// Beheer (Wattzicht zelf). Eén installateur ziet alleen zijn eigen klanten; de beheerder ziet de
// hele keten: welke installateur welke huishoudens heeft, of hun apparaten data leveren, en wat
// dat aan abonnement oplevert. Bewust aparte DTO's: wat de beheerder nodig heeft (omzet, alle
// tenants) hoort niet in het installateursportal terecht te komen.
// ---------------------------------------------------------------------------------------------

/// <summary>Eén installateur op het beheeroverzicht (GET /admin/installers).</summary>
/// <param name="Id">Unieke id.</param>
/// <param name="Name">Bedrijfsnaam.</param>
/// <param name="Slug">Korte code uit links en registratie.</param>
/// <param name="Households">Aantal huishoudens, ook zonder toestemming.</param>
/// <param name="HouseholdsWithAccess">Daarvan: hoeveel de installateur mag inzien.</param>
/// <param name="Devices">Aantal gekoppelde apparaten.</param>
/// <param name="DevicesWithProblem">Apparaten met een foutmelding bij het laatste uitlezen.</param>
/// <param name="LastDataAt">Wanneer er voor het laatst data van deze installateur binnenkwam.</param>
/// <param name="MonthlyRevenueEur">Wat deze installateur per maand oplevert bij het huidige tarief.</param>
/// <param name="CreatedAt">Sinds wanneer hij meedoet.</param>
public sealed record AdminInstallerDto(
    Guid Id,
    string Name,
    string Slug,
    int Households,
    int HouseholdsWithAccess,
    int Devices,
    int DevicesWithProblem,
    DateTimeOffset? LastDataAt,
    decimal MonthlyRevenueEur,
    DateTimeOffset CreatedAt,
    int ActiveHouseholds = 0,
    int BillableHouseholds = 0,
    int HouseholdsWithoutDevice = 0,
    int NewLast30Days = 0,
    DateTimeOffset? ApprovedAt = null,
    string? ContactPerson = null,
    string? City = null,
    string? Phone = null,
    string? ContactEmail = null,
    int? CustomerCount = null);

/// <summary>Eén huishouden op het beheeroverzicht, met de installateur erbij (GET /admin/households).</summary>
/// <param name="Id">Unieke id.</param>
/// <param name="Name">Naam van het huishouden.</param>
/// <param name="City">Plaats.</param>
/// <param name="InstallerId">Bij welke installateur het hoort.</param>
/// <param name="InstallerName">Naam van die installateur.</param>
/// <param name="Devices">Aantal apparaten.</param>
/// <param name="DevicesWithProblem">Apparaten met een foutmelding.</param>
/// <param name="OpenAlerts">Openstaande storingsmeldingen.</param>
/// <param name="LastSeen">Laatste geslaagde uitlezing.</param>
/// <param name="InstallerAccessGranted">Of de bewoner de installateur laat meekijken.</param>
/// <param name="CreatedAt">Sinds wanneer.</param>
public sealed record AdminHouseholdDto(
    Guid Id,
    string Name,
    string? City,
    Guid InstallerId,
    string InstallerName,
    int Devices,
    int DevicesWithProblem,
    int OpenAlerts,
    DateTimeOffset? LastSeen,
    bool InstallerAccessGranted,
    DateTimeOffset CreatedAt);

/// <summary>Cijfers bovenaan het beheerpaneel (GET /admin/overview).</summary>
/// <param name="Installers">Aantal installateurs.</param>
/// <param name="Households">Aantal huishoudens.</param>
/// <param name="HouseholdsWithAccess">Huishoudens die hun installateur laten meekijken.</param>
/// <param name="Devices">Gekoppelde apparaten.</param>
/// <param name="DevicesWithProblem">Apparaten met een foutmelding.</param>
/// <param name="DevicesSilent24h">Apparaten die al een etmaal niets leverden.</param>
/// <param name="OpenAlerts">Openstaande storingsmeldingen.</param>
/// <param name="PricePerHouseholdEur">Het maandtarief waarmee gerekend wordt.</param>
/// <param name="MonthlyRevenueEur">Huishoudens maal dat tarief.</param>
/// <param name="Installers_">Per installateur dezelfde cijfers.</param>
public sealed record AdminOverviewDto(
    int Installers,
    int Households,
    int HouseholdsWithAccess,
    int Devices,
    int DevicesWithProblem,
    int DevicesSilent24h,
    int OpenAlerts,
    decimal PricePerHouseholdEur,
    decimal MonthlyRevenueEur,
    IReadOnlyList<AdminInstallerDto> Installers_,
    int FreeHouseholdsPerInstaller = 0,
    int ActiveHouseholds = 0,
    int BillableHouseholds = 0,
    int HouseholdsWithoutDevice = 0,
    int HouseholdsWithDataToday = 0,
    int NewLast7Days = 0,
    int NewLast30Days = 0,
    int OpenLeads = 0,
    IReadOnlyList<AdminBrandDto>? Brands = null,
    int PendingInstallers = 0,
    int Accounts = 0,
    int AppInstalls = 0,
    int AppInstallsAndroid = 0,
    int AppInstallsIos = 0,
    int PlanViaInstaller = 0,
    int PlanMonthly = 0,
    int PlanYearly = 0);

/// <summary>
/// Eén merk over alle installateurs heen (onderdeel van GET /admin/overview).
///
/// Dit is het antwoord op de enige vraag die de bouwvolgorde bepaalt: welk merk hangt er echt bij
/// klanten, en werkt het. Een merk met veel apparaten en veel fouten is dringender dan een merk dat
/// nog nergens hangt.
/// </summary>
/// <param name="Brand">Merknaam.</param>
/// <param name="Devices">Aantal gekoppelde apparaten van dit merk.</param>
/// <param name="WithProblem">Daarvan met een foutmelding.</param>
/// <param name="Households">Aantal huishoudens waar dit merk hangt.</param>
/// <param name="Supported">Of wij dit merk kunnen uitlezen.</param>
public sealed record AdminBrandDto(
    string Brand,
    int Devices,
    int WithProblem,
    int Households,
    bool Supported);

/// <summary>Een promotie is getoond, aangeklikt of weggeklikt.</summary>
/// <param name="Channel">popup, tile, push of email.</param>
/// <param name="Action">shown, clicked, dismissed of optout.</param>
public sealed record PromoActionRequest(string Channel, string Action);

/// <summary>LaadCash meldt dat een woning zich heeft aangemeld.</summary>
/// <param name="HomeId">De woning, zoals die in de deeplink stond.</param>
/// <param name="LinkedAt">Wanneer; leeg betekent nu.</param>
public sealed record LaadcashLinkedRequest(Guid HomeId, DateTimeOffset? LinkedAt);

/// <summary>Een aankoop uit de store die gecontroleerd moet worden.</summary>
/// <param name="Platform">apple of google.</param>
/// <param name="Receipt">
/// Bij Apple het originele transactienummer, bij Google de purchase token. Wat de app van de store
/// kreeg; wij vragen het daar zelf na.
/// </param>
public sealed record VerifyPurchaseRequest(string Platform, string Receipt);

/// <summary>Uitkomst van een gecontroleerde aankoop.</summary>
/// <param name="Entitlement">De stand van zaken voor deze woning ná de aankoop.</param>
/// <param name="StartsLater">Of het nieuwe abonnement pas begint als een lopende periode afloopt.</param>
/// <param name="Message">Eén regel voor op het scherm.</param>
public sealed record VerifyPurchaseResultDto(object Entitlement, bool StartsLater, string Message);

/// <summary>Een melding van Apple (App Store Server Notifications v2).</summary>
/// <param name="SignedPayload">De ondertekende inhoud.</param>
public sealed record AppleNotificationRequest(string? SignedPayload);

/// <summary>Eén bericht uit Google Pub/Sub.</summary>
/// <param name="Data">De inhoud, base64.</param>
/// <param name="MessageId">Nummer van het bericht.</param>
public sealed record GooglePubSubMessage(string? Data, string? MessageId);

/// <summary>Een melding van Google Play (Real-time Developer Notifications via Pub/Sub).</summary>
/// <param name="Message">Het bericht.</param>
public sealed record GoogleNotificationRequest(GooglePubSubMessage? Message);

/// <summary>Wat een installateur deze maand kost (GET /api/installer/billing).</summary>
/// <param name="BillingMode">free of per_household.</param>
/// <param name="PricePerHouseholdEur">Prijs per actief huishouden per maand, exclusief btw.</param>
/// <param name="ActiveHouseholds">Huishoudens met een lopend abonnement via deze installateur.</param>
/// <param name="MonthlyAmountEur">Het bedrag; nul bij free.</param>
/// <param name="Note">Eén regel uitleg voor op het scherm.</param>
public sealed record InstallerBillingDto(
    string BillingMode,
    decimal PricePerHouseholdEur,
    int ActiveHouseholds,
    decimal MonthlyAmountEur,
    string Note);

/// <summary>Eén abonnement zoals de app het toont (GET /api/plans).</summary>
/// <param name="Key">Vaste sleutel: free, plus_monthly of plus_yearly.</param>
/// <param name="Name">Naam voor op het scherm.</param>
/// <param name="PriceEur">Prijs inclusief btw in euro.</param>
/// <param name="Period">forever, month of year.</param>
/// <param name="StoreProductId">Het product-id bij Apple en Google; <c>null</c> bij gratis.</param>
/// <param name="Description">Eén regel uitleg.</param>
public sealed record PlanDto(
    string Key,
    string Name,
    decimal PriceEur,
    string Period,
    string? StoreProductId,
    string Description);

/// <summary>De abonnementen plus de stand van zaken (GET /api/plans).</summary>
/// <param name="PlusEnabled">Of Plus al open staat. Tot 1 januari 2027 niet.</param>
/// <param name="Plans">De abonnementen.</param>
/// <param name="TrialDays">Hoeveel dagen de proefperiode duurt.</param>
public sealed record PlansDto(bool PlusEnabled, IReadOnlyList<PlanDto> Plans, int TrialDays);

/// <summary>Goedkeuren of intrekken van een installateursaanmelding.</summary>
/// <param name="Approved">True = goedkeuren, false = goedkeuring intrekken.</param>
public sealed record AdminApprovalRequest(bool Approved);

/// <summary>Een demo-aanvraag van de website (GET /admin/leads).</summary>
/// <param name="Id">Unieke id.</param>
/// <param name="Name">Naam van de aanvrager.</param>
/// <param name="Company">Bedrijfsnaam.</param>
/// <param name="Email">E-mailadres.</param>
/// <param name="Phone">Telefoonnummer, als het is ingevuld.</param>
/// <param name="CustomerCount">Opgegeven aantal klanten met zonnepanelen.</param>
/// <param name="Source">Van welk formulier hij kwam.</param>
/// <param name="CreatedAt">Wanneer hij binnenkwam.</param>
/// <param name="IsCustomer">Of er al een installateur met dit e-mailadres bestaat.</param>
public sealed record AdminLeadDto(
    Guid Id,
    string Name,
    string Company,
    string Email,
    string? Phone,
    int? CustomerCount,
    string? Source,
    DateTimeOffset CreatedAt,
    bool IsCustomer);

/// <summary>Een document waar het inlezen niets uit haalde (GET /admin/failed-imports).</summary>
/// <param name="Id">Regelnummer.</param>
/// <param name="FileName">De bestandsnaam, als aanwijzing welke leverancier het is.</param>
/// <param name="Text">De uitgelezen tekst, afgekapt. Leeg betekent: geen tekstlaag, dus een scan of een foto.</param>
/// <param name="CreatedAt">Wanneer het geprobeerd werd.</param>
/// <param name="ExpiresAt">Wanneer deze regel vanzelf wordt opgeruimd.</param>
public sealed record AdminOnleesbaarDto(
    Guid Id,
    string FileName,
    string Text,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

/// <summary>Wie keek er bij wie mee (GET /admin/access-log).</summary>
/// <param name="At">Wanneer.</param>
/// <param name="InstallerName">Welke installateur.</param>
/// <param name="HouseholdName">Bij welk huishouden.</param>
/// <param name="Action">Wat hij deed.</param>
public sealed record AdminAccessLogDto(
    DateTimeOffset At,
    string InstallerName,
    string HouseholdName,
    string Action);

/// <summary>Eén uur uit het laadplan (GET /charge-plan).</summary>
/// <param name="StartUtc">Begin van het uur.</param>
/// <param name="Kwh">Hoeveel er dat uur in de auto gaat.</param>
/// <param name="SolarKwh">Daarvan rechtstreeks van het eigen dak.</param>
/// <param name="CostEur">Wat dat uur kost.</param>
public sealed record ChargeSlotDto(DateTimeOffset StartUtc, double Kwh, double SolarKwh, decimal CostEur);

/// <summary>
/// Wanneer laad je je auto het voordeligst (GET /charge-plan)?
///
/// Niet hetzelfde als "het goedkoopste uur": de planner weegt de uurprijs tegen de verwachte zon op
/// het eigen dak. Een kilowattuur van je eigen panelen kost alleen de terugleververgoeding die je
/// misloopt, en dat is vrijwel altijd minder dan stroom van het net.
/// </summary>
/// <param name="Slots">De voorgestelde uren, op tijd.</param>
/// <param name="TotalKwh">Hoeveel er geladen wordt.</param>
/// <param name="CostEur">Wat het plan kost.</param>
/// <param name="CostIfStartNowEur">Wat nu meteen beginnen zou kosten.</param>
/// <param name="SavingEur">Het verschil tussen die twee.</param>
/// <param name="SolarKwh">Hoeveel ervan van het eigen dak komt.</param>
/// <param name="Feasible">Of het binnen de opgegeven tijd past.</param>
/// <param name="Explanation">Eén zin om in de app te tonen.</param>
/// <param name="PriceSource">Bron van de uurprijzen, of null zonder prijzen.</param>
public sealed record ChargePlanDto(
    IReadOnlyList<ChargeSlotDto> Slots,
    double TotalKwh,
    decimal CostEur,
    decimal CostIfStartNowEur,
    decimal SavingEur,
    double SolarKwh,
    bool Feasible,
    string Explanation,
    string? PriceSource);

/// <summary>Wat de app moet tonen over LaadCash.</summary>
/// <param name="Show">Of er iets te tonen valt.</param>
/// <param name="ShowPopup">Of de eenmalige popup nog open mag; daarna alleen de tegel.</param>
/// <param name="EstimatedYearlyEur">Het geschatte bedrag per jaar, afgerond op tientallen.</param>
/// <param name="ChargedKwhPerYear">Waarop dat bedrag gebaseerd is.</param>
/// <param name="Title">Titel voor de tegel.</param>
/// <param name="Body">Tekst voor de tegel.</param>
/// <param name="PopupTitle">Titel voor de popup.</param>
/// <param name="PopupBody">Tekst voor de popup.</param>
/// <param name="DeepLink">Waar de knop heen gaat, met woning en e-mailadres erin.</param>
/// <param name="Linked">Of deze woning al bij LaadCash zit.</param>
/// <param name="EarnedThisYearEur">De echte opbrengst, zodra er een koppeling is.</param>
public sealed record LaadcashPromoDto(
    bool Show,
    bool ShowPopup,
    decimal EstimatedYearlyEur,
    double ChargedKwhPerYear,
    string? Title,
    string? Body,
    string? PopupTitle,
    string? PopupBody,
    string? DeepLink,
    bool Linked,
    decimal? EarnedThisYearEur);
