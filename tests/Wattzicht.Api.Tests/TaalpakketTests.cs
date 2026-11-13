using System.Text.RegularExpressions;
using Wattzicht.Shared.Taal;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Het taalpakket: Nederlands, Engels, Turks.
///
/// Een vergeten vertaling is onzichtbaar. De build slaagt, de tests slagen, en pas een Turkse
/// gebruiker ziet één Nederlandse regel tussen de zijne staan. Dat is precies het soort fout dat
/// maanden blijft zitten, dus wordt hier geteld in plaats van gehoopt.
///
/// Hetzelfde geldt voor een sleutel die in de XAML staat maar in geen enkel pakket: dan staat er
/// letterlijk "meer.uitlogen" op het scherm. Daarom leest de laatste test de XAML van de app en
/// controleert hij elke sleutel die daar gebruikt wordt.
/// </summary>
public sealed class TaalpakketTests
{
    [Fact]
    public void Alle_drie_de_talen_hebben_dezelfde_sleutels()
    {
        var nl = TekstenNl.Alles.Keys.ToHashSet();
        var en = TekstenEn.Alles.Keys.ToHashSet();
        var tr = TekstenTr.Alles.Keys.ToHashSet();

        Assert.Empty(nl.Except(en));  // in het Nederlands, niet in het Engels
        Assert.Empty(en.Except(nl));  // in het Engels, maar nergens gebruikt
        Assert.Empty(nl.Except(tr));
        Assert.Empty(tr.Except(nl));
    }

    [Fact]
    public void Geen_enkele_tekst_is_leeg()
    {
        foreach (var code in Teksten.Codes)
        {
            foreach (var (sleutel, tekst) in Teksten.Voor(code))
            {
                Assert.False(string.IsNullOrWhiteSpace(tekst), $"{code}: {sleutel} is leeg");
            }
        }
    }

    [Fact]
    public void Een_onbekende_taal_geeft_Nederlands()
    {
        // Een oude telefoon die "de" meestuurt hoort de app te krijgen, niet een leeg scherm.
        Assert.Equal(TekstenNl.Alles["meer.uitloggen"], Teksten.T("de", "meer.uitloggen"));
        Assert.Equal(TekstenNl.Alles["meer.uitloggen"], Teksten.T(null, "meer.uitloggen"));
    }

    [Fact]
    public void Een_onbekende_sleutel_geeft_de_sleutel_terug_en_niet_leeg()
    {
        // Zichtbaar fout is beter dan onzichtbaar leeg: een leeg label ziet eruit als opmaak en
        // gaat jaren mee.
        Assert.Equal("meer.bestaat-niet", Teksten.T("nl", "meer.bestaat-niet"));
        Assert.Equal(string.Empty, Teksten.T("nl", ""));
    }

    [Fact]
    [Trait(Bronnen, "ja")]
    public void Elke_sleutel_in_de_XAML_bestaat_ook_echt()
    {
        var map = AppMap();
        var xaml = Directory.GetFiles(map, "*.xaml", SearchOption.AllDirectories);
        Assert.NotEmpty(xaml);

        var gebruikt = new SortedSet<string>();
        foreach (var bestand in xaml)
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(bestand), @"\{t:Ts?\s+([A-Za-z0-9\.\-]+)\}"))
            {
                gebruikt.Add(m.Groups[1].Value);
            }
        }

        // Als deze leeg is, is de controle stilgevallen (bijvoorbeeld doordat de extensie anders
        // heet) en zegt een groene test niets meer.
        Assert.NotEmpty(gebruikt);

        var ontbreekt = gebruikt.Where(s => !TekstenNl.Alles.ContainsKey(s)).ToList();
        Assert.Empty(ontbreekt);
    }

    [Fact]
    [Trait(Bronnen, "ja")]
    public void Elke_tekst_in_het_pakket_wordt_ook_ergens_gebruikt()
    {
        // Andersom: een sleutel die nergens meer voorkomt is dode tekst die bij elke vertaling weer
        // meegenomen wordt. Dit kijkt naar de hele src-map: een deel van de teksten wordt door de
        // server opgebouwd (de tips, de dagtip, het laadplan) en komt dus niet in de app voor. De
        // pakketten zelf slaan we over, anders bewijst elke sleutel zijn eigen bestaan.
        var map = SrcMap();
        var inhoud = string.Join('\n', Directory
            .GetFiles(map, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Taal{Path.DirectorySeparatorChar}"))
            .Where(f => f.EndsWith(".xaml") || f.EndsWith(".cs"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(File.ReadAllText));

        var ongebruikt = TekstenNl.Alles.Keys
            .Where(s => !inhoud.Contains(s, StringComparison.Ordinal))
            .ToList();

        Assert.Empty(ongebruikt);
    }

    /// <summary>
    /// Deze twee tests lezen de bronbestanden van de app en de server.
    ///
    /// Dat kan niet overal: de API-image wordt gebouwd met alleen de server en de tests erin, en
    /// draait daar deze hele testronde. De Dockerfile sluit dit kenmerk daarom uit; de gewone
    /// testronde en de CI-stap "api" hebben de hele repo en voeren het wel uit. Uitsluiten in de
    /// Dockerfile is met opzet: dat staat er zichtbaar bij, in plaats van een test die zichzelf
    /// stilletjes overslaat en voor altijd groen blijft.
    /// </summary>
    private const string Bronnen = "LeestBronbestanden";

    /// <summary>De map van de app.</summary>
    private static string AppMap() => Path.Combine(SrcMap(), "Wattzicht.App");

    /// <summary>De src-map: de app en de server, want in beide staan sleutels.</summary>
    private static string SrcMap()
    {
        var map = new DirectoryInfo(AppContext.BaseDirectory);
        while (map is not null && !File.Exists(Path.Combine(map.FullName, "Wattzicht.sln")))
        {
            map = map.Parent;
        }

        Assert.NotNull(map);
        var src = Path.Combine(map!.FullName, "src");
        Assert.True(Directory.Exists(src), $"De src-map is niet gevonden onder {map.FullName}.");
        return src;
    }
}
