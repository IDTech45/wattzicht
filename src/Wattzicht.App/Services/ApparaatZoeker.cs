using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Wattzicht.App.Services;

/// <summary>
/// Zoekt apparaten op het wifi-netwerk waar de telefoon op zit.
///
/// Waarom dit bestaat: we vroegen mensen het IP-adres van hun gateway of dongle in te tikken. Dat
/// staat nergens op het apparaat zelf; je moet ervoor in je router kijken, en daar haakt iemand af.
/// De telefoon zit op hetzelfde netwerk, dus die kan het gewoon zelf opzoeken.
///
/// Hoe: we nemen het eigen adres van de telefoon, lopen de 254 adressen van dat subnet langs en
/// vragen elk apparaat om een adres dat alleen dit soort apparaat kent. De Envoy van Enphase
/// antwoordt op /info met XML waar "envoy" in staat; een HomeWizard P1 antwoordt op /api met zijn
/// product_type. Alles tegelijk met een korte tijdslimiet, anders duurt het minuten.
///
/// Dit vervangt geen koppeling: we vinden alleen het adres. Inloggen of een token blijft nodig,
/// want dat is precies wat het apparaat beschermt.
/// </summary>
public sealed class ApparaatZoeker
{
    /// <summary>Kort, want een apparaat dat er is antwoordt binnen een halve seconde op zijn eigen netwerk.</summary>
    private static readonly TimeSpan Geduld = TimeSpan.FromMilliseconds(700);

    /// <summary>Niet alle 254 tegelijk: dan loopt de wifi van een telefoon vast.</summary>
    private const int Tegelijk = 32;

    public sealed record Gevonden(string Adres, string Soort);

    /// <summary>
    /// Het subnet waar de telefoon op zit, bijvoorbeeld "192.168.1". Geeft null als er geen wifi is
    /// of alleen mobiel internet; zoeken heeft dan geen zin.
    /// </summary>
    public static string? EigenSubnet()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

            foreach (var adres in nic.GetIPProperties().UnicastAddresses)
            {
                if (adres.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (IPAddress.IsLoopback(adres.Address)) continue;

                var b = adres.Address.GetAddressBytes();
                var prive = b[0] == 10
                            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                            || (b[0] == 192 && b[1] == 168);
                if (prive)
                {
                    return $"{b[0]}.{b[1]}.{b[2]}";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Zoekt een Enphase IQ Gateway.
    ///
    /// Eerst op naam. Een Envoy meldt zich op het netwerk als envoy.local, en als de router of de
    /// telefoon dat oplost is het antwoord er binnen een seconde. Lukt dat niet, dan pas het hele
    /// subnet langs; dat werkt altijd maar duurt wat langer.
    /// </summary>
    public async Task<Gevonden?> ZoekEnphaseAsync(CancellationToken ct = default)
    {
        static bool IsEnvoy(string inhoud) => inhoud.Contains("envoy", StringComparison.OrdinalIgnoreCase);

        foreach (var naam in new[] { "envoy.local", "envoy" })
        {
            if (await AntwoordtAsync(naam, "/info", IsEnvoy, ct))
            {
                return new Gevonden(await NaarIpAsync(naam, ct), "Enphase IQ Gateway");
            }
        }

        return await ZoekAsync("/info", IsEnvoy, "Enphase IQ Gateway", ct);
    }

    /// <summary>Zoekt een HomeWizard P1-dongle; ook die meldt zich met een naam op het netwerk.</summary>
    public async Task<Gevonden?> ZoekP1Async(CancellationToken ct = default)
    {
        static bool IsP1(string inhoud) => inhoud.Contains("HWE-P1", StringComparison.OrdinalIgnoreCase)
                                           || inhoud.Contains("product_type", StringComparison.OrdinalIgnoreCase);

        if (await AntwoordtAsync("p1meter.local", "/api", IsP1, ct))
        {
            return new Gevonden(await NaarIpAsync("p1meter.local", ct), "P1-meter");
        }

        return await ZoekAsync("/api", IsP1, "P1-meter", ct);
    }

    /// <summary>
    /// Zet een naam als envoy.local om naar het nummer erachter, en geeft anders de naam terug.
    ///
    /// We vinden het apparaat op zijn naam, maar we bewaren liever het nummer. Zo'n naam werkt alleen
    /// zolang het toestel hem kan opzoeken, en dat gaat op een ander netwerk of na een herstart van de
    /// router net zo makkelijk weer mis. Het nummer is bovendien wat mensen verwachten te zien staan
    /// in een veld dat om een IP-adres vraagt.
    /// </summary>
    private static async Task<string> NaarIpAsync(string naam, CancellationToken ct)
    {
        try
        {
            var adressen = await Dns.GetHostAddressesAsync(naam, ct);
            var vier = adressen.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            return vier?.ToString() ?? naam;
        }
        catch (Exception)
        {
            // De naam werkt wel (we kregen net antwoord), alleen het opzoeken niet. Dan maar de naam.
            return naam;
        }
    }

    private async Task<Gevonden?> ZoekAsync(string pad, Func<string, bool> herkent, string soort, CancellationToken ct)
    {
        var subnet = EigenSubnet();
        if (subnet is null) return null;

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Gevonden? gevonden = null;

        for (var begin = 1; begin <= 254 && gevonden is null && !stop.IsCancellationRequested; begin += Tegelijk)
        {
            var blok = Enumerable.Range(begin, Math.Min(Tegelijk, 255 - begin))
                .Select(async n =>
                {
                    var adres = $"{subnet}.{n}";
                    if (await AntwoordtAsync(adres, pad, herkent, stop.Token))
                    {
                        return adres;
                    }

                    return null;
                })
                .ToList();

            foreach (var treffer in await Task.WhenAll(blok))
            {
                if (treffer is not null)
                {
                    gevonden = new Gevonden(treffer, soort);
                    break;
                }
            }
        }

        // Zodra we er een hebben de rest afbreken; doorzoeken kost alleen accu.
        await stop.CancelAsync();
        return gevonden;
    }

    private static async Task<bool> AntwoordtAsync(string adres, string pad, Func<string, bool> herkent, CancellationToken ct)
    {
        // Een eigen client per poging met een eigen tijdslimiet: één traag apparaat mag de rest niet
        // ophouden. Certificaten negeren we, want deze kastjes dragen een zelfondertekend certificaat
        // op een IP-adres en daar valt niets aan te controleren.
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        using var client = new HttpClient(handler) { Timeout = Geduld };

        foreach (var schema in new[] { "http", "https" })
        {
            try
            {
                using var antwoord = await client.GetAsync($"{schema}://{adres}{pad}", ct);
                if (!antwoord.IsSuccessStatusCode) continue;

                var inhoud = await antwoord.Content.ReadAsStringAsync(ct);
                if (herkent(inhoud)) return true;
            }
            catch (Exception)
            {
                // Niets daar, of iets heel anders. Dat is het normale geval bij 253 van de 254.
            }
        }

        return false;
    }
}
