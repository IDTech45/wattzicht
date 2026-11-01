using Microsoft.Extensions.Configuration;
using Wattzicht.Api.Services;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Wat een installateur per maand kost.
///
/// Op het beheerpaneel stond eerder alle huishoudens maal twee euro. Dat is op twee punten verkeerd:
/// de eerste vijftig klanten zijn gratis, en een huishouden dat zich aanmeldde maar nooit een
/// apparaat koppelde is geen actieve klant. Triple Energie met één klant zag zo twee euro omzet
/// staan die niemand kon factureren, en dat is precies het getal waarop je een bedrijf stuurt.
/// </summary>
public sealed class BillingTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(50, 0)]
    [InlineData(51, 1)]
    [InlineData(230, 180)]
    public void De_eerste_vijftig_klanten_zijn_gratis(int actief, int verwachtFacturabel)
    {
        Assert.Equal(verwachtFacturabel, Billing.Facturabel(actief, Billing.StandaardGratis));
    }

    [Fact]
    public void Triple_Energie_met_een_klant_levert_nul_euro_op()
    {
        Assert.Equal(0m, Billing.PerMaand(actieveKlanten: 1, gratis: 50, prijsPerKlant: 2m));
    }

    [Fact]
    public void Boven_de_drempel_wordt_er_per_klant_gerekend()
    {
        // 80 actieve klanten: 30 boven de drempel, maal twee euro.
        Assert.Equal(60m, Billing.PerMaand(actieveKlanten: 80, gratis: 50, prijsPerKlant: 2m));
    }

    [Fact]
    public void Een_afwijkende_afspraak_kan_zonder_gratis_klanten()
    {
        Assert.Equal(160m, Billing.PerMaand(actieveKlanten: 80, gratis: 0, prijsPerKlant: 2m));
    }

    [Fact]
    public void Onzin_in_de_invoer_levert_nooit_een_negatief_bedrag()
    {
        Assert.Equal(0, Billing.Facturabel(-5, 50));
        Assert.Equal(0m, Billing.PerMaand(80, -10, -3m));
    }

    [Fact]
    public void Zonder_instellingen_gelden_vijftig_gratis_en_twee_euro()
    {
        var leeg = new ConfigurationBuilder().Build();

        Assert.Equal(50, Billing.GratisUitConfig(leeg));
        Assert.Equal(2m, Billing.PrijsUitConfig(leeg));
    }

    [Fact]
    public void Instellingen_worden_overgenomen()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Billing:FreeHouseholdsPerInstaller"] = "10",
            ["Billing:PricePerHouseholdEur"] = "3.50",
        }).Build();

        Assert.Equal(10, Billing.GratisUitConfig(config));
        Assert.Equal(3.50m, Billing.PrijsUitConfig(config));
    }

    [Fact]
    public void Een_onleesbare_instelling_valt_terug_op_de_standaard()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Billing:FreeHouseholdsPerInstaller"] = "veel",
            ["Billing:PricePerHouseholdEur"] = "gratis",
        }).Build();

        Assert.Equal(50, Billing.GratisUitConfig(config));
        Assert.Equal(2m, Billing.PrijsUitConfig(config));
    }

    [Fact]
    public void Een_duizendscheiding_wordt_niet_als_prijs_geaccepteerd()
    {
        // "1.234" als duizendscheiding bedoeld zou anders 1234 euro per klant worden. Een maandtarief
        // heeft geen duizendscheiding nodig, dus liever terugvallen dan er duizend keer naast zitten.
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Billing:PricePerHouseholdEur"] = "1 234",
        }).Build();

        Assert.Equal(2m, Billing.PrijsUitConfig(config));
    }

    [Fact]
    public void Een_prijs_met_een_komma_mag_ook()
    {
        // Een euro-bedrag met een komma typen is normaal; dan mag het overzicht niet stil terugvallen
        // op twee euro. InvariantCulture leest een punt, dus de komma wordt eerst omgezet.
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Billing:PricePerHouseholdEur"] = "2,50",
        }).Build();

        Assert.Equal(2.50m, Billing.PrijsUitConfig(config));
    }
}
