using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Wattzicht.Api.Services;

/// <summary>
/// De omweg langs Enphase, zodat hij ook vanuit de app kan.
///
/// Enphase laat je alleen terugkeren op een adres dat in hun developer-app staat, en dat is een
/// webadres. Een app kan daar niet op wachten. Tot nu toe kon de cloudkoppeling daarom alleen op de
/// website, en dan moet iemand met zijn telefoon in de hand eerst ergens anders inloggen om iets te
/// doen wat in de app thuishoort.
///
/// Daarom dit: de app vraagt hier een kaartje aan, een onvoorspelbare tekenreeks die bij zijn woning
/// hoort. Dat kaartje gaat als <c>state</c> mee naar Enphase en komt ongewijzigd terug op onze
/// webpagina. Die pagina hoeft dan niemand in te loggen: aan het kaartje zien wij bij welke woning
/// deze toestemming hoort.
///
/// Een kaartje is tien minuten geldig en vervalt zodra het gebruikt is. Daarmee is het geen sleutel
/// tot een woning maar een afspraak voor één handeling: wie het onderschept kan er hoogstens een
/// Enphase-account aan die woning hangen, en alleen binnen die tien minuten.
/// </summary>
public sealed class EnphaseKoppelsessie(TimeProvider clock)
{
    /// <summary>Hoe lang een kaartje geldig blijft.</summary>
    public static readonly TimeSpan Geldig = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, (Guid Woning, string RedirectUri, string? ClientId, DateTimeOffset Tot)> _open = new();

    /// <summary>Een nieuw kaartje voor deze woning.</summary>
    public string Maak(Guid woning, string redirectUri, string? eigenClientId)
    {
        Opruimen();

        var kaartje = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        _open[kaartje] = (woning, redirectUri, eigenClientId, clock.GetUtcNow() + Geldig);
        return kaartje;
    }

    /// <summary>
    /// Het kaartje inwisselen. Geeft null als het niet bestaat, al gebruikt is of verlopen.
    /// Eenmalig: een tweede poging met hetzelfde kaartje levert niets op.
    /// </summary>
    public (Guid Woning, string RedirectUri, string? ClientId)? Verzilver(string? kaartje)
    {
        Opruimen();

        if (string.IsNullOrWhiteSpace(kaartje)) return null;
        if (!_open.TryRemove(kaartje, out var rij)) return null;
        if (rij.Tot < clock.GetUtcNow()) return null;

        return (rij.Woning, rij.RedirectUri, rij.ClientId);
    }

    private void Opruimen()
    {
        var nu = clock.GetUtcNow();
        foreach (var paar in _open)
        {
            if (paar.Value.Tot < nu) _open.TryRemove(paar.Key, out _);
        }
    }
}
