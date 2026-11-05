using Wattzicht.Api.Connectors;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Wie leest het apparaat uit, de server of de app?
///
/// Dat hangt af van het adres. Een Sessy-thuisbatterij stond bij ons als cloudkoppeling, terwijl hij
/// bij vrijwel iedereen op 192.168.1.x hangt. Onze server probeerde dat elke vijf minuten en kon per
/// definitie niets anders dan mislukken, bij elke klant, elke ronde. Alleen een adres dat van buiten
/// bestaat hoort bij de server.
/// </summary>
public sealed class ThuisAdresTests
{
    [Theory]
    [InlineData("192.168.1.50")]
    [InlineData("192.168.178.12")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.3.9")]
    [InlineData("172.31.255.1")]
    [InlineData("169.254.4.4")]          // DHCP liet het afweten
    [InlineData("127.0.0.1")]
    [InlineData("localhost")]
    [InlineData("sessy-1234.local")]
    [InlineData("http://192.168.1.50")]  // een hele URL mag ook
    [InlineData("192.168.1.50:8080")]    // en een poort erachter
    [InlineData("fd00::1")]              // eigen IPv6-netwerk
    [InlineData("fe80::1")]
    public void Dit_bestaat_alleen_binnen_het_huis(string host)
        => Assert.True(ThuisAdres.IsPrive(host), host);

    [Theory]
    [InlineData("sessy.mijnhuis.nl")]
    [InlineData("84.25.11.9")]
    [InlineData("172.32.0.1")]           // net buiten 172.16/12
    [InlineData("172.15.0.1")]
    [InlineData("193.168.1.1")]          // lijkt op 192.168 maar is het niet
    [InlineData("2001:1af8::1")]
    [InlineData("https://sessy.mijnhuis.nl:443")]
    public void Hier_kan_de_server_wel_bij(string host)
        => Assert.False(ThuisAdres.IsPrive(host), host);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Zonder_adres_nemen_we_niets_aan(string? host)
        => Assert.False(ThuisAdres.IsPrive(host));
}
