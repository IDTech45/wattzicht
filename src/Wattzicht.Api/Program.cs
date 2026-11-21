using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Wattzicht.Api.Connectors;
using Wattzicht.Api.Data;
using Wattzicht.Api.Endpoints;
using Wattzicht.Api.Services;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// ---------------------------------------------------------------------------
// Foutmonitoring (Sentry). Opt-in: zonder DSN blijft de SDK helemaal uit, dus lokaal en in tests
// gebeurt er niets. Waarom het er moet zijn: zonder dit hoor je pas dat er iets stuk is als een
// bewoner klaagt, en de meesten klagen niet, die stoppen gewoon met kijken.
// Geen persoonsgegevens mee: geen IP's, geen namen (AVG), en geen performance-tracing.
// ---------------------------------------------------------------------------
builder.WebHost.UseSentry(o =>
{
    o.Dsn = configuration["Sentry:Dsn"] ?? string.Empty;
    o.Environment = configuration["Sentry:Environment"]
        ?? (builder.Environment.IsDevelopment() ? "development" : "production");
    o.SendDefaultPii = false;
    o.TracesSampleRate = 0.0;
    // Logregels van Error en hoger worden meldingen; de rest is context bij zo'n melding. Daardoor
    // komt ook de rondewachter binnen, want die meldt stilte als een fout.
    o.MinimumEventLevel = LogLevel.Error;
    o.MinimumBreadcrumbLevel = LogLevel.Information;
});

// ---------------------------------------------------------------------------
// Configuratie: env-variabelen overschrijven appsettings (JWT_SECRET, WZ_PROTECTION_KEY, ConnectionStrings__Default).
// ---------------------------------------------------------------------------
var connectionString = configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default ontbreekt.");

var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
jwtOptions.Secret = configuration["JWT_SECRET"] ?? jwtOptions.Secret;

var protectionKey = configuration["WZ_PROTECTION_KEY"] ?? configuration["DataProtection:Key"] ?? string.Empty;

// ---------------------------------------------------------------------------
// Services
// ---------------------------------------------------------------------------
builder.Services.AddSingleton(TimeProvider.System);

// Scheiding tussen klanten: de context leest per request wie er ingelogd is (zie TenantContext).
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<TenantContext>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(3)));

// Data Protection: sleutelring in Postgres, versleuteld met WZ_PROTECTION_KEY.
var masterKey = new ProtectionMasterKey(protectionKey);
builder.Services.AddSingleton(masterKey);
// Expliciet een fabriek en geen AddSingleton<T>(): de decryptor heeft twee constructors (één met de
// hoofdsleutel, één met IServiceProvider omdat Data Protection hem zo zelf aanmaakt) en DI kan dan
// niet kiezen. In Production viel dat niet op, want daar staat ValidateOnBuild uit; in Development
// startte de API helemaal niet meer met "The following constructors are ambiguous".
builder.Services.AddSingleton(sp => new ProtectionKeyXmlDecryptor(sp.GetRequiredService<ProtectionMasterKey>()));
builder.Services.AddDataProtection()
    .SetApplicationName("wattzicht")
    .PersistKeysToDbContext<AppDbContext>();
builder.Services.Configure<KeyManagementOptions>(options =>
    options.XmlEncryptor = new ProtectionKeyXmlEncryptor(masterKey));
builder.Services.AddSingleton<CredentialProtector>();

// Plus: één plek die bepaalt of een woning Plus heeft, en één plek die abonnementen wegschrijft.
builder.Services.AddScoped<EntitlementService>();
builder.Services.AddScoped<SubscriptionWriter>();
builder.Services.AddScoped<LaadcashPromotie>();
builder.Services.AddScoped<Aanbiedingen>();
builder.Services.AddScoped<TariefBron>();

// Aankopen controleren bij de store. Zonder sleutels staan ze uit en kennen ze niets toe; liever
// niemand Plus dan iemand Plus op een bonnetje dat niet nagekeken is.
builder.Services.AddHttpClient(AppleStoreVerifier.HttpClientName, client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Store:Apple:BaseUrl"] ?? AppleStoreVerifier.ProductionBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddHttpClient(GoogleStoreVerifier.HttpClientName, client =>
{
    client.BaseAddress = new Uri(GoogleStoreVerifier.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddSingleton<IStoreVerifier, AppleStoreVerifier>();
builder.Services.AddSingleton<IStoreVerifier, GoogleStoreVerifier>();

// Auth
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<PasswordHasher>();

// Inloggen met Google of Apple. De client-id's zijn geen geheimen (ze zitten in elke app-build),
// maar ze moeten wel kloppen: ze bepalen voor wie een token bedoeld mag zijn. Staan ze er niet, dan
// zijn die knoppen uit en vertelt /auth/providers dat ook, zodat de app ze niet toont.
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return new ExternalAuthSettings
    {
        GoogleClientIds = config["ExternalAuth:GoogleClientIds"],
        AppleClientIds = config["ExternalAuth:AppleClientIds"],
    };
});
builder.Services.AddSingleton<ExternalAuthService>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenService(jwtOptions, TimeProvider.System).ValidationParameters;
        options.MapInboundClaims = false; // 'sub' en 'email' blijven hun korte naam houden
    });
builder.Services.AddAuthorization(options => options.AddWattzichtPolicies());

// Domein
builder.Services.AddDeviceConnectors(configuration);
builder.Services.AddSingleton<SavingsCalculator>();
builder.Services.AddScoped<AlertService>();
builder.Services.AddScoped<SummaryService>();
builder.Services.AddScoped<HouseholdQueries>();
builder.Services.AddHttpClient(EnergyZeroPriceProvider.HttpClientName, client =>
{
    client.BaseAddress = new Uri(configuration["Advice:PriceBaseUrl"] ?? EnergyZeroPriceProvider.DefaultBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Wattzicht/0.1");
});
builder.Services.AddSingleton<IPriceProvider, EnergyZeroPriceProvider>();
builder.Services.AddScoped<IPushSender, FcmPushSender>();
builder.Services.AddSingleton<PollingWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PollingWorker>());
// Hartslag van de uitleesronde, plus een wachter die alarm slaat als die stilvalt. Los van de ronde
// zelf: een proces dat zichzelf bewaakt meldt niets meer zodra het niet meer start.
builder.Services.AddSingleton<Hartslag>();
builder.Services.AddHttpClient(RondeWachter.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Wattzicht/0.1");
});
builder.Services.AddHostedService<RondeWachter>();
builder.Services.AddHttpClient(OpenMeteoWeatherProvider.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Wattzicht/0.1");
});
builder.Services.AddSingleton<IWeatherProvider, OpenMeteoWeatherProvider>();
builder.Services.AddHostedService<WeatherWorker>();
// Brengt het laadadvies naar de bewoner in plaats van andersom; zie SmartMomentWorker.
builder.Services.AddHostedService<SmartMomentWorker>();
// De avond ervoor: morgen is het zonnig, zet je auto dan aan de lader. Zie ZonMorgenWorker.
builder.Services.AddHostedService<ZonMorgenWorker>();
// Waarschuwt als de panelen minder leveren dan het weer toeliet; zie PrestatieWorker.
builder.Services.AddHostedService<PrestatieWorker>();
// Eén bericht per maand met wat die maand heeft gedaan; zie MaandberichtWorker.
builder.Services.AddHostedService<MaandberichtWorker>();
// De andere helft van datzelfde advies: na afloop vertellen wat de laadbeurt heeft gedaan.
builder.Services.AddHostedService<LaadsessieWorker>();

// Dagelijks: verlopen abonnementen afsluiten en waarschuwen dat een proefperiode bijna om is.
builder.Services.AddHostedService<SubscriptionWorker>();

// Herinneringen van de LaadCash-campagne (push en mail). Staat niet achter Plus.
builder.Services.AddHostedService<LaadcashPromoWorker>();

// Dagelijks: wat een bewaartermijn heeft en eroverheen is, gaat weg.
builder.Services.AddHostedService<OpruimWorker>();

// Eenmalig na het starten: dagen die op nul bleven staan invullen uit de tellerstanden.
builder.Services.AddHostedService<OpwekHerstelWorker>();

// En wat daarvoor ligt bij de fabrikant ophalen: wie al een jaar panelen heeft, hoort geen lege
// grafiek te zien omdat hij pas vorige week koppelde.
builder.Services.AddHostedService<EnphaseGeschiedenisWorker>();

// De geladen kilowatturen bij LaadCash ophalen, voor woningen die daar klant zijn.
builder.Services.AddHttpClient(LaadcashLaaddata.HttpClientName);
builder.Services.AddScoped<LaadcashLaaddata>();
builder.Services.AddHostedService<LaadcashLaaddataWorker>();

// De omweg langs Enphase vanuit de app: kaartjes die tien minuten geldig zijn. In het geheugen, want
// het is een afspraak voor één handeling en geen gegeven dat een herstart hoort te overleven.
builder.Services.AddSingleton<EnphaseKoppelsessie>();
builder.Services.AddScoped<InsightsService>();
builder.Services.AddScoped<AandachtService>();

// CORS voor de Vue-frontend en lokale ontwikkeling
var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["https://wattzicht.nl", "https://www.wattzicht.nl"];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

// Rate limiting voor anonieme formulieren (leads): 5 per minuut per IP.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(LeadEndpoints.RateLimitPolicy, http =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// JSON: enums als tekst, camelCase (standaard voor minimal API's)
builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Enums die het portal in kleine letters verwacht eerst; daarna de algemene (PascalCase) voor de rest.
    // Een converter in Options gaat vóór het [JsonConverter]-attribuut op het type, dus expliciet hier.
    options.SerializerOptions.Converters.Add(new Wattzicht.Shared.LowerCaseEnumConverter<Wattzicht.Shared.HouseholdStatus>());
    options.SerializerOptions.Converters.Add(new Wattzicht.Shared.LowerCaseEnumConverter<Wattzicht.Shared.PerformanceStatus>());
    options.SerializerOptions.Converters.Add(new Wattzicht.Shared.LowerCaseEnumConverter<Wattzicht.Shared.OpportunityKind>());
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("postgres");

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Wattzicht API",
        Version = "v0.1",
        Description = "Zie wat je bespaart: zonnepanelen, thuisbatterij en laadpaal in één app."
    });

    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Plak hier het JWT-token uit /auth/login.",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };
    options.AddSecurityDefinition("Bearer", scheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = [] });

    var xml = Path.Combine(AppContext.BaseDirectory, "Wattzicht.Api.xml");
    if (File.Exists(xml))
    {
        options.IncludeXmlComments(xml);
    }

    var sharedXml = Path.Combine(AppContext.BaseDirectory, "Wattzicht.Shared.xml");
    if (File.Exists(sharedXml))
    {
        options.IncludeXmlComments(sharedXml);
    }
});

var app = builder.Build();

// ---------------------------------------------------------------------------
// Database: migraties toepassen (of aanmaken zonder migraties in dev) en seeden
// ---------------------------------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var hasMigrations = db.Database.GetMigrations().Any();
    if (hasMigrations)
    {
        await db.Database.MigrateAsync();
    }
    else
    {
        // Nog geen migraties in de repo: schema direct uit het model (v0.1). Daarna: dotnet ef migrations add Init.
        await db.Database.EnsureCreatedAsync();
        await SchemaUpgrades.ApplyAsync(db);
    }
}

// De woning zonder installateur. Zie SysteemInstallateur: wie zich aanmeldt zonder code van een
// installateur hoort bij niemand, en hing tot nu toe onder het eerste bedrijf in de lijst.
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Installateur");
    try
    {
        await SysteemInstallateur.EnsureAsync(db, TimeProvider.System);
        var losgekoppeld = await SysteemInstallateur.LosseWoningenAsync(db);
        if (losgekoppeld > 0)
            log.LogInformation("{Aantal} woningen zonder installateurscode losgekoppeld van hun installateur.", losgekoppeld);
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Woningen zonder installateur losmaken mislukt.");
    }
}

// Besparing van bestaande dagen opnieuw uitrekenen. Nodig na de regel voor woningen zonder meting in
// de meterkast: die stonden op 0,00 euro terwijl de zon wel degelijk iets opleverde. Rekent alleen
// opnieuw uit wat er al ligt en schrijft alleen als er iets verandert.
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var summaries = scope.ServiceProvider.GetRequiredService<SummaryService>();
    var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Besparing");
    try
    {
        var woningen = await db.Households.Select(h => h.Id).ToListAsync();
        var gewijzigd = 0;
        foreach (var woning in woningen)
        {
            await summaries.RecalculateSavingsAsync(woning, DateOnly.MinValue, CancellationToken.None);
            gewijzigd += await db.SaveChangesAsync();
        }
        if (gewijzigd > 0) log.LogInformation("Besparing opnieuw uitgerekend over {Dagen} dagen.", gewijzigd);
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Besparing opnieuw uitrekenen mislukt.");
    }
}

if (configuration.GetValue("Seed:Enabled", true))
{
    await DbSeeder.SeedAsync(app.Services);
}

// ---------------------------------------------------------------------------
// Pipeline
// ---------------------------------------------------------------------------
// De website praat via nginx met /api/... en de app rechtstreeks zonder prefix: beide werken.
// UseRouting expliciet ná UsePathBase, anders voegt WebApplication routing vóór de path base in.
app.UsePathBase("/api");
app.UseRouting();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment() || configuration.GetValue("Swagger:Enabled", false))
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Wattzicht API v0.1");
        options.DocumentTitle = "Wattzicht API";
    });
}

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Legt vast in welke taal deze bewoner de app gebruikt, zodat een pushmelding uit een
// achtergrondtaak dezelfde taal spreekt. Na de autorisatie, want het heeft zijn woning nodig.
app.UseTaalBijhouden();

app.MapHealthChecks("/health").AllowAnonymous();
// Draait de uitleesronde nog? /health zegt alleen dat de webserver antwoordt, en dat bleef kloppen
// terwijl de ronde stil kon liggen. Hier staat hoe lang geleden de laatste ronde klaar was, zodat
// een bewakingsdienst (of een mens) het van buitenaf kan zien zonder in de logs te duiken.
app.MapGet("/health/ronde", (Hartslag hartslag) =>
{
    var stand = hartslag.Stand();
    return Results.Ok(new
    {
        laatsteRonde = stand.LaatsteRonde,
        stilteMinuten = Math.Round(stand.Stilte.TotalMinutes, 1),
        huishoudens = stand.Huishoudens,
        apparatenOk = stand.ApparatenOk,
        apparatenMislukt = stand.ApparatenMislukt,
    });
}).AllowAnonymous().ExcludeFromDescription();
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.MapAuthEndpoints();
app.MapHouseholdEndpoints();
app.MapInstallerEndpoints();
app.MapAdminEndpoints();
app.MapLeadEndpoints();
app.MapAdviceEndpoints();
app.MapPushEndpoints();
app.MapPlanEndpoints();
app.MapSubscriptionEndpoints();
app.MapLaadcashEndpoints();

app.Run();

/// <summary>Zichtbaar maken voor WebApplicationFactory in tests.</summary>
public partial class Program;
