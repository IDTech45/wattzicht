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
builder.Services.AddHttpClient(OpenMeteoWeatherProvider.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Wattzicht/0.1");
});
builder.Services.AddSingleton<IWeatherProvider, OpenMeteoWeatherProvider>();
builder.Services.AddHostedService<WeatherWorker>();
// Brengt het laadadvies naar de bewoner in plaats van andersom; zie SmartMomentWorker.
builder.Services.AddHostedService<SmartMomentWorker>();

// Dagelijks: verlopen abonnementen afsluiten en waarschuwen dat een proefperiode bijna om is.
builder.Services.AddHostedService<SubscriptionWorker>();

// Herinneringen van de LaadCash-campagne (push en mail). Staat niet achter Plus.
builder.Services.AddHostedService<LaadcashPromoWorker>();
builder.Services.AddScoped<InsightsService>();

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

app.MapHealthChecks("/health").AllowAnonymous();
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
