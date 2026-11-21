using System.Reflection;
using Wattzicht.Api.Data;
using Xunit;

namespace Wattzicht.Api.Tests;

/// <summary>
/// De goedkeuring van een installateur mag nooit vanzelf gebeuren.
///
/// Een installateur kan zich zelf aanmelden. Zolang Wattzicht hem niet heeft goedgekeurd mag zijn code
/// niet werken bij het aanmelden van bewoners, want anders schrijft iemand zich in als "installateur"
/// en trekt hij klanten naar zijn eigen portal.
///
/// Dat stond er, en toch werkte het niet. Toen de kolom ApprovedAt erbij kwam, kreeg alles wat er al
/// stond een stempel: terecht, dat was bestaand werk. Alleen draait die lijst met schemastappen bij
/// elke start van de API, dus kreeg élke wachtende aanmelding bij de eerstvolgende deploy alsnog een
/// stempel. Op 21-11-2026 viel het op doordat een installateur die bewust niet goedgekeurd was er
/// binnen een minuut een goedkeuringsdatum bij had staan.
///
/// Deze test bewaakt de grens die dat stopt. Hij leest de stap zelf, want de fout zat niet in de
/// bedoeling maar in de letterlijke SQL.
/// </summary>
public class GoedkeuringInstallateurTests
{
    private static string[] Stappen() =>
        (string[])typeof(SchemaUpgrades)
            .GetField("Steps", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

    [Fact]
    public void De_stap_die_installateurs_goedkeurt_geldt_alleen_voor_wat_er_al_stond()
    {
        var stap = Stappen().Single(s => s.Contains("UPDATE installers") && s.Contains("ApprovedAt"));

        Assert.Contains("\"CreatedAt\" <", stap);
    }

    /// <summary>
    /// Geen enkele schemastap mag een goedkeuring uitdelen zonder grens. Zou iemand later een tweede
    /// variant toevoegen, dan valt hij hier om.
    /// </summary>
    [Fact]
    public void Geen_enkele_stap_keurt_alles_zonder_datum_goed()
    {
        var onbegrensd = Stappen()
            .Where(s => s.Contains("UPDATE installers") && s.Contains("ApprovedAt") && !s.Contains("\"CreatedAt\" <"))
            .ToList();

        Assert.Empty(onbegrensd);
    }
}
