using System.Text.RegularExpressions;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Elke StaticResource in de app moet bestaan.
///
/// Waarom deze test er is: ik verwees naar een kleur "TextDark" die niet bestond. De app bouwde
/// gewoon, want zo'n verwijzing wordt pas opgezocht als het scherm geopend wordt. Gevolg: de
/// instellingen klapten eruit zodra je erop tikte, en dat merk je alleen door de app op een telefoon
/// open te maken.
///
/// Een ontbrekende sleutel is geen smaakkwestie en geen regressie die je moet beredeneren: hij staat
/// er of hij staat er niet. Dus laten we het de bouw vertellen in plaats van de bewoner.
/// </summary>
[Trait("LeestBronbestanden", "ja")]
public sealed class XamlBronnenTests
{
    private static string AppMap()
    {
        var map = new DirectoryInfo(AppContext.BaseDirectory);
        while (map is not null && !Directory.Exists(Path.Combine(map.FullName, "src", "Wattzicht.App")))
        {
            map = map.Parent;
        }

        Assert.NotNull(map);
        return Path.Combine(map!.FullName, "src", "Wattzicht.App");
    }

    [Fact]
    public void Elke_verwijzing_naar_een_bron_bestaat_ook()
    {
        var app = AppMap();
        var bestanden = Directory.GetFiles(app, "*.xaml", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();

        Assert.NotEmpty(bestanden);

        var bekend = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pad in bestanden)
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(pad), "x:Key=\"([A-Za-z0-9_]+)\""))
            {
                bekend.Add(m.Groups[1].Value);
            }
        }

        var ontbreekt = new List<string>();
        foreach (var pad in bestanden)
        {
            // Zonder de commentaarblokken eruit te halen vindt deze test de waarschuwing die in
            // Colors.xaml staat over precies dit probleem, en valt hij over zijn eigen uitleg.
            var inhoud = Regex.Replace(File.ReadAllText(pad), "<!--.*?-->", string.Empty, RegexOptions.Singleline);

            foreach (Match m in Regex.Matches(inhoud, @"StaticResource\s+([A-Za-z0-9_]+)"))
            {
                var sleutel = m.Groups[1].Value;
                if (!bekend.Contains(sleutel))
                {
                    ontbreekt.Add($"{Path.GetFileName(pad)}: {sleutel}");
                }
            }
        }

        Assert.True(ontbreekt.Count == 0,
            "Deze verwijzingen bestaan niet en laten het scherm crashen zodra iemand het opent:\n"
            + string.Join("\n", ontbreekt.Distinct()));
    }
}
