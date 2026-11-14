using System.Text.RegularExpressions;
using Wattzicht.Shared.Taal;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Fouten in de app die de compiler niet ziet en een gebruiker wel.
///
/// Waarom deze tests de bronbestanden lezen in plaats van de app aan te roepen: de testronde heeft
/// geen MAUI erin, en dat hoeft ook niet. Alle vier de fouten hieronder zijn te zien aan de tekst
/// van het bestand, en alle vier zijn ze al een keer op een telefoon ontdekt in plaats van hier:
///
/// Een kleur die in C# wordt opgevraagd maar niet in Colors.xaml staat. De app geeft dan grijs
/// terug en niemand merkt het, behalve dat een stip of een balk de verkeerde kleur heeft.
///
/// Een route in GoToAsync die niet in de Shell bestaat. Je tikt op de knop en er gebeurt niets;
/// het verzoek lukt, dus er is ook geen foutmelding.
///
/// Een tekst voor de schermlezer die vast in het Nederlands staat. Die leest alleen een
/// schermlezer voor, dus is dit de enige plek waar het ooit opvalt.
///
/// Een {0} in een tekst waar geen waarde voor meegegeven wordt. Dat is geen leeg veld maar een
/// FormatException, en dus een scherm dat eruit klapt zodra iemand het opent.
/// </summary>
[Trait("LeestBronbestanden", "ja")]
public sealed class AppBronnenTests
{
    [Fact]
    public void Elke_kleur_die_de_code_opvraagt_staat_in_Colors_xaml()
    {
        var bekend = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pad in Xaml())
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(pad), "x:Key=\"([A-Za-z0-9_]+)\""))
            {
                bekend.Add(m.Groups[1].Value);
            }
        }

        Assert.NotEmpty(bekend);

        // ThemeColors.Get valt terug op grijs als de sleutel niet bestaat. Dat is met opzet (een
        // app die crasht op een kleur is erger), en juist daarom moet het hier gecontroleerd
        // worden: in de app zie je het verschil tussen grijs en "bestaat niet" niet.
        var patroon = new Regex(
            @"\b(?:ThemeColors\.Get|ThemeColors\.Themed|Kleur|Thema|Zacht|Res|ThemeRes)\(\s*((?:""[^""]*""\s*,?\s*)+)\)");

        var ontbreekt = new List<string>();
        var gevonden = 0;
        foreach (var pad in Cs())
        {
            foreach (Match m in patroon.Matches(File.ReadAllText(pad)))
            {
                foreach (Match sleutel in Regex.Matches(m.Groups[1].Value, @"""([^""]*)"""))
                {
                    gevonden++;
                    if (!bekend.Contains(sleutel.Groups[1].Value))
                    {
                        ontbreekt.Add($"{Path.GetFileName(pad)}: {sleutel.Groups[1].Value}");
                    }
                }
            }
        }

        // Als dit nul is, is de controle stilgevallen doordat de helpers anders gaan heten.
        Assert.True(gevonden > 10, $"Maar {gevonden} kleursleutels gevonden; staat het patroon nog goed?");
        Assert.True(ontbreekt.Count == 0,
            "Deze kleuren bestaan niet en geven in de app stilletjes grijs:\n" + string.Join("\n", ontbreekt.Distinct()));
    }

    [Fact]
    public void Elke_route_in_de_code_bestaat_in_de_Shell()
    {
        var shell = File.ReadAllText(Path.Combine(AppMap(), "AppShell.xaml"));
        var codeAchter = File.ReadAllText(Path.Combine(AppMap(), "AppShell.xaml.cs"));

        // De paden die de Shell zelf kent: Route="x" in de XAML, aan elkaar geregen zoals Shell ze
        // samenstelt. Het pad van een tab is dus //main/home en niet //home.
        var paden = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(shell, @"Route=""([a-z0-9\-]+)"""))
        {
            paden.Add(m.Groups[1].Value);
        }

        Assert.Contains("main", paden);

        var geldig = new HashSet<string>(StringComparer.Ordinal);
        foreach (var hoofd in paden)
        {
            geldig.Add($"//{hoofd}");
            foreach (var sub in paden)
            {
                geldig.Add($"//{hoofd}/{sub}");
            }
        }

        // En de losse schermen die op de stapel gepusht worden.
        foreach (Match m in Regex.Matches(codeAchter, @"Routing\.RegisterRoute\(nameof\((\w+)\)"))
        {
            geldig.Add(m.Groups[1].Value);
        }

        // De constanten uit AppShell.Routes: daar komt bijna alle navigatie uit, dus hier zit de
        // winst. Stond //settings als constante, dan zou dat hier opvallen in plaats van op een
        // toestel waar de knop niets doet.
        var fout = new List<string>();
        var constanten = 0;
        foreach (Match m in Regex.Matches(codeAchter, @"public const string \w+ = ""([^""]+)"";"))
        {
            constanten++;
            if (!geldig.Contains(m.Groups[1].Value))
            {
                fout.Add($"AppShell.Routes: {m.Groups[1].Value}");
            }
        }

        // En een los pad dat iemand toch rechtstreeks in een GoToAsync zet. ".." is een stap terug.
        foreach (var pad in Cs())
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(pad), @"GoToAsync\(\s*""([^""{]+)"""))
            {
                var route = m.Groups[1].Value;
                if (route == "..") continue;

                if (!geldig.Contains(route))
                {
                    fout.Add($"{Path.GetFileName(pad)}: {route}");
                }
            }
        }

        Assert.True(constanten >= 5, $"Maar {constanten} routeconstanten gevonden; staat het patroon nog goed?");
        Assert.True(fout.Count == 0,
            "Deze routes bestaan niet in de Shell; de knop doet dan niets:\n" + string.Join("\n", fout.Distinct()));
    }

    [Fact]
    public void Geen_vaste_tekst_voor_de_schermlezer()
    {
        // Een merknaam hoort niet vertaald te worden; de rest wel.
        var mag = new[] { "Wattzicht", "Wattzicht-logo", "LaadCash" };

        var fout = new List<string>();
        foreach (var pad in Xaml())
        {
            var inhoud = Regex.Replace(File.ReadAllText(pad), "<!--.*?-->", string.Empty, RegexOptions.Singleline);
            foreach (Match m in Regex.Matches(inhoud, @"SemanticProperties\.(?:Description|Hint)=""([^""]*)"""))
            {
                var waarde = m.Groups[1].Value;
                if (waarde.StartsWith('{') || mag.Contains(waarde)) continue;

                fout.Add($"{Path.GetFileName(pad)}: {waarde}");
            }
        }

        Assert.True(fout.Count == 0,
            "Deze teksten leest alleen een schermlezer voor en staan vast in het Nederlands:\n"
            + string.Join("\n", fout));
    }

    [Fact]
    public void Elke_pagina_met_bindingen_heeft_een_x_DataType()
    {
        // Zonder x:DataType worden de bindingen niet gecompileerd en blijft een verschrijving een
        // leeg label: te zien op een toestel, niet in de bouw.
        var fout = new List<string>();
        foreach (var pad in Xaml())
        {
            var inhoud = File.ReadAllText(pad);
            if (!inhoud.Contains("{Binding ", StringComparison.Ordinal)) continue;
            if (inhoud.Contains("x:DataType", StringComparison.Ordinal)) continue;

            fout.Add(Path.GetFileName(pad));
        }

        Assert.True(fout.Count == 0,
            "Deze bestanden binden zonder x:DataType:\n" + string.Join("\n", fout));
    }

    [Fact]
    public void Elke_string_Format_geeft_genoeg_waarden_mee()
    {
        var patroon = new Regex(@"string\.Format\(\s*Taal\.T\(""([^""]+)""\)\s*,");
        var fout = new List<string>();
        var gevonden = 0;

        foreach (var pad in Cs())
        {
            var inhoud = File.ReadAllText(pad);
            foreach (Match m in patroon.Matches(inhoud))
            {
                gevonden++;
                var sleutel = m.Groups[1].Value;
                if (!TekstenNl.Alles.TryGetValue(sleutel, out var tekst))
                {
                    fout.Add($"{Path.GetFileName(pad)}: {sleutel} bestaat niet");
                    continue;
                }

                var nodig = Plaatshouders(tekst);
                var meegegeven = AantalArgumenten(inhoud, m.Index + m.Length);
                if (meegegeven < nodig)
                {
                    fout.Add($"{Path.GetFileName(pad)}: {sleutel} vraagt {nodig} waarden, krijgt {meegegeven}");
                }
            }
        }

        Assert.True(gevonden > 20, $"Maar {gevonden} aanroepen gevonden; staat het patroon nog goed?");
        Assert.True(fout.Count == 0,
            "Hier gooit string.Format zodra het scherm opengaat:\n" + string.Join("\n", fout));
    }

    /// <summary>Hoeveel waarden de tekst nodig heeft: de hoogste {n} plus een.</summary>
    private static int Plaatshouders(string tekst)
    {
        var hoogste = -1;
        foreach (Match m in Regex.Matches(tekst, @"\{(\d+)\}"))
        {
            hoogste = Math.Max(hoogste, int.Parse(m.Groups[1].Value));
        }

        return hoogste + 1;
    }

    /// <summary>
    /// De argumenten tellen vanaf de komma na de sleutel tot de sluitende haak van string.Format.
    /// Komma's binnen haakjes of binnen tekst tussen aanhalingstekens tellen niet mee.
    /// </summary>
    private static int AantalArgumenten(string inhoud, int vanaf)
    {
        var aantal = 1;
        var diepte = 0;
        var inTekst = false;

        for (var i = vanaf; i < inhoud.Length; i++)
        {
            var c = inhoud[i];

            if (inTekst)
            {
                if (c == '\\') i++;
                else if (c == '"') inTekst = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    inTekst = true;
                    break;
                case '(' or '[' or '{':
                    diepte++;
                    break;
                case ')' when diepte == 0:
                    return aantal;
                case ')' or ']' or '}':
                    diepte--;
                    break;
                case ',' when diepte == 0:
                    aantal++;
                    break;
            }
        }

        return aantal;
    }

    private static IEnumerable<string> Xaml() => Bestanden("*.xaml");

    private static IEnumerable<string> Cs() => Bestanden("*.cs");

    private static List<string> Bestanden(string patroon)
    {
        var lijst = Directory.GetFiles(AppMap(), patroon, SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();

        Assert.NotEmpty(lijst);
        return lijst;
    }

    private static string AppMap()
    {
        var map = new DirectoryInfo(AppContext.BaseDirectory);
        while (map is not null && !File.Exists(Path.Combine(map.FullName, "Wattzicht.sln")))
        {
            map = map.Parent;
        }

        Assert.NotNull(map);
        return Path.Combine(map!.FullName, "src", "Wattzicht.App");
    }
}
