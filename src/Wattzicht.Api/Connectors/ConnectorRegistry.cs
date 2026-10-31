using Wattzicht.Shared;

namespace Wattzicht.Api.Connectors;

/// <summary>Geeft per merk de juiste connector. Merken zonder connector (Deye, Alfen, Zaptec, HomeWizard) geven <c>null</c>.</summary>
public sealed class ConnectorRegistry
{
    private readonly IReadOnlyDictionary<DeviceBrand, IDeviceConnector> _connectors;

    public ConnectorRegistry(IEnumerable<IDeviceConnector> connectors)
    {
        _connectors = connectors.ToDictionary(c => c.Brand);
    }

    /// <summary>Merken die nu gekoppeld kunnen worden.</summary>
    public IReadOnlyCollection<DeviceBrand> SupportedBrands => _connectors.Keys.ToArray();

    public bool IsSupported(DeviceBrand brand) => _connectors.ContainsKey(brand);

    public IDeviceConnector? Get(DeviceBrand brand) =>
        _connectors.TryGetValue(brand, out var connector) ? connector : null;

    /// <summary>Als <see cref="Get"/>, maar gooit een duidelijke fout als het merk nog niet ondersteund wordt.</summary>
    public IDeviceConnector GetRequired(DeviceBrand brand) =>
        Get(brand) ?? throw new ConnectorException($"Merk {brand} wordt nog niet ondersteund.");
}

/// <summary>DI-registratie van alle connectors.</summary>
public static class ConnectorServiceCollectionExtensions
{
    public static IServiceCollection AddDeviceConnectors(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient(GrowattConnector.HttpClientName, client =>
        {
            var baseUrl = configuration["Connectors:Growatt:BaseUrl"] ?? GrowattConnector.DefaultBaseUrl;
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Wattzicht/0.1");
        });

        services.AddHttpClient(SessyConnector.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Wattzicht/0.1");
        });

        services.AddHttpClient(EnphaseConnector.HttpClientName, client =>
        {
            var baseUrl = configuration["Connectors:Enphase:BaseUrl"] ?? EnphaseConnector.DefaultBaseUrl;
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Wattzicht/0.1");
        });

        services.AddHttpClient(SolaxConnector.HttpClientName, client =>
        {
            var baseUrl = configuration["Connectors:Solax:BaseUrl"] ?? SolaxConnector.DefaultBaseUrl;
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Wattzicht/0.1");
        });

        services.AddSingleton<IDeviceConnector, GrowattConnector>();
        services.AddSingleton<IDeviceConnector, SolaxConnector>();
        services.AddSingleton<IDeviceConnector, SessyConnector>();
        services.AddSingleton(new EnphaseOptions(
            configuration["Connectors:Enphase:ClientId"],
            configuration["Connectors:Enphase:ClientSecret"],
            configuration["Connectors:Enphase:ApiKey"]));
        services.AddSingleton<IDeviceConnector, EnphaseConnector>();
        services.AddSingleton<IDeviceConnector, DemoConnector>();
        services.AddSingleton<ConnectorRegistry>();

        return services;
    }
}
