using Wattzicht.Api.Services;
using Xunit;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Een laadbeurt herkennen, en zeggen hoeveel daarvan van je eigen dak kwam.
///
/// Wattzicht vertelde vooraf wanneer je het voordeligst laadt en hoorde daarna nooit meer iets. Het
/// laadvermogen verdween in een grafiek. Dit is het stuk dat na afloop één bericht mogelijk maakt,
/// en daarmee het enige getal dat iemand doorstuurt aan een ander.
///
/// De splitsing gaat via de netkant en niet via het huisverbruik, want of de auto daar al in zit
/// hangt af van het merk dat de meting levert. Trekt de paal c watt en staat het net op g watt
/// afname, dan kwam hoogstens min(c, g) van het net.
/// </summary>
public class LaadsessieDetectorTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Een reeks metingen van vijf minuten uit elkaar, zonder afsluiting.</summary>
    private static List<Meetpunt> Rauw(params (double Paal, double Net)[] punten) =>
        [.. punten.Select((p, i) => new Meetpunt(Start.AddMinutes(5 * i), p.Paal, p.Net, true))];

    /// <summary>
    /// Dezelfde reeks, met een kwartier stilstand erachter. Zonder die staart is de laadbeurt nog
    /// bezig en hoort de detector hem terecht niet te melden; dat is apart getest.
    /// </summary>
    private static List<Meetpunt> Reeks(params (double Paal, double Net)[] punten)
    {
        var reeks = Rauw(punten);
        var laatste = reeks[^1].Tijd;
        for (var i = 1; i <= 4; i++) reeks.Add(new Meetpunt(laatste.AddMinutes(5 * i), 0, 300, true));
        return reeks;
    }

    [Fact]
    public void Een_uur_laden_op_elf_kilowatt_levert_elf_kilowattuur()
    {
        // Dertien metingen van vijf minuten is precies een uur.
        var reeks = Reeks([.. Enumerable.Repeat((11000.0, 11000.0), 13)]);

        var sessie = Assert.Single(LaadsessieDetector.Vind(reeks));

        Assert.Equal(11.0, sessie.Kwh, 1);
        Assert.Equal(Start, sessie.Start);
        Assert.Equal(Start.AddHours(1), sessie.Eind);
    }

    [Fact]
    public void Laden_terwijl_je_teruglevert_telt_helemaal_als_eigen_zon()
    {
        // De panelen maken meer dan het huis en de auto samen opmaken: er gaat stroom het net óp,
        // dus er kwam niets van het net.
        var reeks = Reeks([.. Enumerable.Repeat((3000.0, -1500.0), 13)]);

        var sessie = Assert.Single(LaadsessieDetector.Vind(reeks));

        Assert.Equal(3.0, sessie.Kwh, 1);
        Assert.Equal(3.0, sessie.ZonKwh, 1);
        Assert.Equal(1.0, sessie.ZonAandeel, 2);
    }

    [Fact]
    public void Laden_in_het_donker_telt_helemaal_als_net()
    {
        var reeks = Reeks([.. Enumerable.Repeat((7400.0, 7900.0), 13)]);

        var sessie = Assert.Single(LaadsessieDetector.Vind(reeks));

        Assert.Equal(0, sessie.ZonKwh, 2);
        Assert.Equal(0, sessie.ZonAandeel, 2);
    }

    [Fact]
    public void Half_zon_half_net_wordt_ook_half_geteld()
    {
        // De auto trekt 4 kW en er komt 1 kW van het net: 3 kW komt van eigen opwek.
        var reeks = Reeks([.. Enumerable.Repeat((4000.0, 1000.0), 13)]);

        var sessie = Assert.Single(LaadsessieDetector.Vind(reeks));

        Assert.Equal(4.0, sessie.Kwh, 1);
        Assert.Equal(3.0, sessie.ZonKwh, 1);
        Assert.Equal(0.75, sessie.ZonAandeel, 2);
    }

    [Fact]
    public void Het_huis_dat_meer_trekt_dan_de_auto_maakt_de_auto_nog_niet_groener()
    {
        // Afname 9 kW terwijl de auto er 4 trekt: alles wat de auto kreeg kwam van het net, ook al
        // gaat de rest van de afname naar de wasmachine.
        var reeks = Reeks([.. Enumerable.Repeat((4000.0, 9000.0), 13)]);

        Assert.Equal(0, Assert.Single(LaadsessieDetector.Vind(reeks)).ZonKwh, 2);
    }

    [Fact]
    public void Een_korte_pauze_blijft_dezelfde_beurt()
    {
        // Auto's stoppen even met laden, bijvoorbeeld om de accu te balanceren. Dat is geen tweede beurt.
        var reeks = Reeks(
            (7000, 7000), (7000, 7000), (7000, 7000),
            (0, 500), (0, 500),
            (7000, 7000), (7000, 7000), (7000, 7000));

        Assert.Single(LaadsessieDetector.Vind(reeks));
    }

    [Fact]
    public void Na_een_lange_stilte_is_het_een_nieuwe_beurt()
    {
        var vroeg = Enumerable.Range(0, 13).Select(i => new Meetpunt(Start.AddMinutes(5 * i), 7000, 7000, true));
        var laat = Enumerable.Range(0, 13).Select(i => new Meetpunt(Start.AddHours(6).AddMinutes(5 * i), 7000, 7000, true));
        var staart = Enumerable.Range(1, 4).Select(i => new Meetpunt(Start.AddHours(7).AddMinutes(5 * i), 0, 300, true));

        Assert.Equal(2, LaadsessieDetector.Vind([.. vroeg, .. laat, .. staart]).Count);
    }

    [Fact]
    public void Een_beurt_die_nog_loopt_melden_we_niet()
    {
        // Anders komt dezelfde beurt elke ronde opnieuw voorbij, en krijgt de bewoner om het kwartier
        // een melding over een auto die nog aan de stekker staat.
        var nogBezig = Rauw([.. Enumerable.Repeat((11000.0, 11000.0), 13)]);

        Assert.Empty(LaadsessieDetector.Vind(nogBezig));
        // Pas als de paal een kwartier stilstaat is hij afgerond.
        Assert.Single(LaadsessieDetector.Vind(Reeks([.. Enumerable.Repeat((11000.0, 11000.0), 13)])));
    }

    [Fact]
    public void Standby_is_geen_laden()
    {
        var reeks = Reeks([.. Enumerable.Repeat((60.0, 400.0), 30)]);

        Assert.Empty(LaadsessieDetector.Vind(reeks));
    }

    [Fact]
    public void Een_paar_ons_stroom_is_geen_bericht_waard()
    {
        // Twee minuten laden omdat iemand de stekker verkeerd om hield.
        var reeks = Reeks((5000, 5000), (5000, 5000), (0, 300), (0, 300), (0, 300), (0, 300));

        Assert.Empty(LaadsessieDetector.Vind(reeks));
    }

    [Fact]
    public void Een_gat_in_de_metingen_wordt_geen_laadbeurt_van_een_uur()
    {
        // De server stond stil of de telefoon lag in een la. Wat daartussen gebeurde weten we niet,
        // en met alleen de beginwaarde zou hier drie uur laden uitkomen.
        var reeks = new List<Meetpunt>
        {
            new(Start, 11000, 11000, true),
            new(Start.AddHours(3), 11000, 11000, true),
            new(Start.AddHours(3).AddMinutes(5), 0, 300, true),
            new(Start.AddHours(3).AddMinutes(10), 0, 300, true),
            new(Start.AddHours(3).AddMinutes(15), 0, 300, true),
            new(Start.AddHours(3).AddMinutes(20), 0, 300, true),
        };

        Assert.Empty(LaadsessieDetector.Vind(reeks));
    }

    [Fact]
    public void Zonder_netmeting_zeggen_we_niets_over_de_herkomst()
    {
        // Wie geen P1-meter of CT-klemmen heeft, krijgt wel zijn kilowatturen te zien maar geen
        // zonaandeel. Nul tonen zou betekenen "alles van het net", en dat weten we niet.
        var reeks = Enumerable.Range(0, 13)
            .Select(i => new Meetpunt(Start.AddMinutes(5 * i), 7000, 0, false))
            .ToList();
        for (var i = 1; i <= 4; i++) reeks.Add(new Meetpunt(Start.AddHours(1).AddMinutes(5 * i), 0, 0, false));

        var sessie = Assert.Single(LaadsessieDetector.Vind(reeks));

        Assert.False(sessie.HerkomstBekend);
        Assert.Equal(7.0, sessie.Kwh, 1);
        Assert.Equal(0, sessie.ZonAandeel);
    }

    [Fact]
    public void Metingen_door_elkaar_worden_eerst_op_tijd_gezet()
    {
        var reeks = Reeks([.. Enumerable.Repeat((11000.0, 11000.0), 13)]);
        reeks.Reverse();

        Assert.Single(LaadsessieDetector.Vind(reeks));
    }
}

/// <summary>
/// Het bericht dat na afloop de deur uit gaat.
///
/// De volgorde is bewust: eerst wat er gebeurde, dan pas wat je er de volgende keer mee kunt. Wie net
/// geladen heeft wil weten wat het opleverde en niet meteen een nieuwe les. En zonder netmeting
/// zwijgen we over de herkomst, want nul tonen zou "alles van het net" betekenen terwijl we het
/// gewoon niet weten.
/// </summary>
public class LaadsessieBerichtTests
{
    private static readonly DateTimeOffset Nu = new(2026, 11, 9, 20, 0, 0, TimeSpan.Zero);

    private static Laadsessie Sessie(double kwh, double zon, bool bekend = true) =>
        new(Nu.AddHours(-2), Nu, kwh, zon, bekend);

    [Fact]
    public void Veel_eigen_zon_krijgt_een_eigen_kop_en_geen_les()
    {
        var (titel, tekst) = PushPolicy.ChargeSessionMessage(Sessie(32.4, 19.8), zonnigUurMorgen: 13);

        Assert.Equal("Geladen op je eigen zon", titel);
        Assert.Contains("32,4 kWh", tekst);
        Assert.Contains("19,8 kWh", tekst);
        Assert.Contains("61 procent", tekst);
        Assert.DoesNotContain("Morgen", tekst);
    }

    [Fact]
    public void Weinig_eigen_zon_krijgt_de_verwachting_van_morgen_erbij()
    {
        // Hier is iets te winnen, en pas hier is het weer relevant: voor wat er al gebeurd is hebben
        // we de meting, niet de verwachting.
        var (titel, tekst) = PushPolicy.ChargeSessionMessage(Sessie(30, 3), zonnigUurMorgen: 13);

        Assert.Equal("Je auto is klaar", titel);
        Assert.Contains("Morgen staat de zon rond 13:00", tekst);
    }

    [Fact]
    public void Zonder_zonnige_dag_morgen_sturen_we_niemand_voor_niets_naar_buiten()
    {
        var (_, tekst) = PushPolicy.ChargeSessionMessage(Sessie(30, 3), zonnigUurMorgen: null);

        Assert.DoesNotContain("Morgen", tekst);
        Assert.Contains("30 kWh", tekst);
    }

    [Fact]
    public void Zonder_netmeting_noemen_we_alleen_de_kilowatturen()
    {
        var (titel, tekst) = PushPolicy.ChargeSessionMessage(Sessie(21.5, 0, bekend: false), zonnigUurMorgen: 13);

        Assert.Equal("Je auto is klaar", titel);
        Assert.Equal("Er ging 21,5 kWh in.", tekst);
        Assert.DoesNotContain("dak", tekst);
    }

    [Fact]
    public void Midden_in_de_nacht_storen_we_niemand()
    {
        var huishouden = new Wattzicht.Api.Data.Household { NotifyChargeSession = true };
        var nacht = new DateTimeOffset(2026, 11, 9, 3, 0, 0, TimeSpan.Zero);

        Assert.False(PushPolicy.MaySendChargeSession(huishouden, nacht));
        Assert.True(PushPolicy.MaySendChargeSession(huishouden, Nu));
    }

    [Fact]
    public void Wie_het_niet_wil_krijgt_het_niet()
    {
        var huishouden = new Wattzicht.Api.Data.Household { NotifyChargeSession = false };

        Assert.False(PushPolicy.MaySendChargeSession(huishouden, Nu));
    }
}
