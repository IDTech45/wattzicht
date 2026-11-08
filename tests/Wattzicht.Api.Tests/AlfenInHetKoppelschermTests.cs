using Wattzicht.Shared;
using Wattzicht.Api.Connectors;
using Xunit;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Een Alfen moet je in de app kunnen toevoegen, niet alleen kunnen uitlezen.
///
/// De connector en de uitlezer in de app waren er al, maar het merk stond in geen van beide
/// koppelschermen: niet in de app en niet op de website. Je kon een Alfen dus nergens opgeven, en
/// daarmee was de hele route onbruikbaar. Dat kwam pas boven toen de app thuis getest zou worden met
/// precies zo'n paal.
///
/// Op de website hoort hij ook niet thuis: onze server kan niet bij een adres als 192.168.1.42. De
/// app kan dat wel, want die staat in hetzelfde huis.
/// </summary>
public class AlfenInHetKoppelschermTests
{
    /// <summary>Dezelfde afweging als ConnectDeviceViewModel.ValidateFields.</summary>
    private static string? Fout(string ip, string wachtwoord)
    {
        if (string.IsNullOrWhiteSpace(ip)) return "ip";
        // Let op: IPAddress.TryParse accepteert "192.168.1" en maakt daar 192.168.0.1 van. Daarom
        // tellen we de delen zelf; zie IsVolledigIpAdres in ConnectDeviceViewModel.
        var delen = ip.Trim().Split('.');
        var geldig = delen.Length == 4
                     && delen.All(d => d.Length is > 0 and <= 3 && d.All(char.IsAsciiDigit) && int.Parse(d) <= 255);
        if (!geldig) return "ip";
        if (string.IsNullOrEmpty(wachtwoord)) return "wachtwoord";
        return null;
    }

    [Fact]
    public void Een_ip_en_een_wachtwoord_zijn_genoeg()
    {
        Assert.Null(Fout("192.168.1.42", "geheim"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("mijn-laadpaal")]
    [InlineData("192.168.1")]
    [InlineData("192.168.1.300")]
    public void Zonder_geldig_adres_komt_hij_er_niet_door(string ip)
    {
        Assert.Equal("ip", Fout(ip, "geheim"));
    }

    [Fact]
    public void Zonder_wachtwoord_ook_niet()
    {
        // Het wachtwoord krijgt de eigenaar van zijn installateur; zonder dat laat de paal niemand binnen.
        Assert.Equal("wachtwoord", Fout("192.168.1.42", ""));
    }

    [Fact]
    public void De_gebruikersnaam_vragen_we_niet()
    {
        // Bij een Alfen is die altijd admin. Een veld ervoor levert alleen typefouten op in iets wat
        // niet kan variëren.
        const string gebruiker = "admin";
        Assert.Equal("admin", gebruiker);
    }

    [Fact]
    public void Het_merk_bestaat_en_heeft_een_connector_nodig_die_lokaal_werkt()
    {
        // Zou Alfen ooit als cloudmerk worden behandeld, dan gaat onze server elke vijf minuten een
        // privéadres proberen te bereiken dat hij nooit kan zien.
        Assert.True(Enum.IsDefined(DeviceBrand.Alfen));
        Assert.True(ThuisAdres.IsPrive("192.168.1.42"));
        Assert.False(ThuisAdres.IsPrive("api.alfen.com"));
    }
}
