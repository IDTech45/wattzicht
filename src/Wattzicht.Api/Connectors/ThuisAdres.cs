using System.Net;

namespace Wattzicht.Api.Connectors;

/// <summary>
/// Staat dit apparaat op iemands eigen netwerk, of is het van buitenaf te bereiken?
///
/// Het verschil bepaalt wie het uitleest. Een adres als 192.168.1.50 bestaat alleen binnen dat ene
/// huis; onze server loopt daar gegarandeerd op stuk, hoe vaak hij het ook probeert. De telefoon van
/// de bewoner staat er wél op zodra hij thuis is, dus die haalt het op en stuurt het door.
///
/// Een naam of een openbaar adres laten we juist aan de server: wie een poort heeft opengezet of een
/// VPN gebruikt is beter af met een server die elke vijf minuten kijkt, dan met een app die alleen
/// meet als iemand hem toevallig opent.
/// </summary>
public static class ThuisAdres
{
    /// <summary>Is dit een adres dat alleen binnen het huis van de bewoner bestaat?</summary>
    public static bool IsPrive(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;

        var schoon = host.Trim();
        // Een volledige URL mag ook; dan hebben we alleen de hostnaam nodig. Alleen bij een echt
        // schema kijken: "fd00::1" is een geldig IPv6-adres, maar Uri leest daar een schema "fd00" in
        // en geeft een lege hostnaam terug. Daar liep de eerste versie op stuk.
        if (schoon.Contains("//", StringComparison.Ordinal)
            && Uri.TryCreate(schoon, UriKind.Absolute, out var uri)
            && !string.IsNullOrEmpty(uri.Host))
        {
            schoon = uri.Host;
        }
        // En een poort erachter telt niet mee.
        var dubbelePunt = schoon.LastIndexOf(':');
        if (dubbelePunt > 0 && !schoon.Contains('[') && schoon.Count(c => c == ':') == 1)
            schoon = schoon[..dubbelePunt];
        schoon = schoon.Trim('[', ']');

        // Namen die de router zelf uitdeelt, zoals sessy-1234.local of die-ene-pc.home.
        if (schoon.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || schoon.EndsWith(".home", StringComparison.OrdinalIgnoreCase)
            || schoon.EndsWith(".lan", StringComparison.OrdinalIgnoreCase)
            || schoon.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(schoon, out var ip))
        {
            // Een gewone naam (sessy.mijnhuis.nl) wijst naar buiten; daar kan de server bij.
            return false;
        }

        if (IPAddress.IsLoopback(ip)) return true;

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] switch
            {
                10 => true,                                  // 10.0.0.0/8
                172 => b[1] >= 16 && b[1] <= 31,             // 172.16.0.0/12
                192 => b[1] == 168,                          // 192.168.0.0/16
                169 => b[1] == 254,                          // 169.254.0.0/16, als DHCP het liet afweten
                _ => false,
            };
        }

        // IPv6: fc00::/7 is het eigen netwerk, fe80::/10 is alleen de eigen kabel.
        var v6 = ip.GetAddressBytes();
        return (v6[0] & 0xFE) == 0xFC || (v6[0] == 0xFE && (v6[1] & 0xC0) == 0x80);
    }
}
