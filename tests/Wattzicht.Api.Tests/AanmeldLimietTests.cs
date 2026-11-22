using Xunit;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Aanmelden en inloggen stonden helemaal open.
///
/// Tussen 12 en 15 november 2026 zijn er zo 139 huishoudens aangemaakt: Engelse namen, een
/// gmail-adres met vijf cijfers erachter, geen plaats, geen apparaat, de klok rond en ook om vier
/// uur 's nachts. De begrenzing die al in de code zat gold alleen voor het leadformulier.
///
/// Er zat een tweede gat onder. Achter Traefik komt elk verzoek binnen vanaf het Docker-netwerk, dus
/// zonder ForwardedHeaders is RemoteIpAddress voor iedereen hetzelfde adres. Een limiet "per IP"
/// begrenst dan in werkelijkheid alle bezoekers bij elkaar, en verbruikt één bot de beurten van een
/// echte klant. Die twee horen bij elkaar, dus bewaakt deze test ze samen.
/// </summary>
[Trait("LeestBronbestanden", "ja")]
public sealed class AanmeldLimietTests
{
    private static string Lees(params string[] pad)
    {
        var hier = new DirectoryInfo(AppContext.BaseDirectory);
        while (hier is not null && !File.Exists(Path.Combine(hier.FullName, "Wattzicht.sln")))
            hier = hier.Parent;
        Assert.NotNull(hier);
        return File.ReadAllText(Path.Combine(new[] { hier!.FullName }.Concat(pad).ToArray()));
    }

    [Fact]
    public void Aanmelden_en_inloggen_zijn_begrensd()
    {
        var bron = Lees("src", "Wattzicht.Api", "Endpoints", "AuthEndpoints.cs");
        var login = bron.IndexOf("MapPost(\"/login\"", StringComparison.Ordinal);

        var aanmelden = bron[bron.IndexOf("MapPost(\"/register\"", StringComparison.Ordinal)..login];
        Assert.Contains("RequireRateLimiting(AanmeldLimiet)", aanmelden, StringComparison.Ordinal);
        Assert.Contains("RequireRateLimiting(InlogLimiet)", bron[login..], StringComparison.Ordinal);
    }

    [Fact]
    public void Het_echte_bezoekersadres_komt_binnen()
    {
        var bron = Lees("src", "Wattzicht.Api", "Program.cs");

        Assert.Contains("ForwardedHeadersOptions", bron, StringComparison.Ordinal);
        Assert.Contains("app.UseForwardedHeaders()", bron, StringComparison.Ordinal);

        // Alles vertrouwen is net zo onveilig als niets lezen: dan kan iedereen zelf een
        // X-Forwarded-For meesturen en is de begrenzing per IP een formaliteit.
        Assert.Contains("o.KnownProxies.Clear()", bron, StringComparison.Ordinal);
    }
}
