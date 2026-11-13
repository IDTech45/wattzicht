using Microsoft.AspNetCore.Http;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests;

/// <summary>
/// De taal van een verzoek, en wat de server daarmee doet.
///
/// De tips en de dagtip komen van de server, want ze worden met gemeten waarden opgebouwd. Als de
/// taal daar niet aankomt staat er een Nederlandse tip onder een Turks scherm, en dat is precies
/// het soort halve vertaling dat opvalt bij de gebruiker en niet bij ons.
/// </summary>
public sealed class TaalVanVerzoekTests
{
    private static HttpRequest Verzoek(string? acceptLanguage)
    {
        var http = new DefaultHttpContext();
        if (acceptLanguage is not null)
        {
            http.Request.Headers.AcceptLanguage = acceptLanguage;
        }

        return http.Request;
    }

    [Theory]
    [InlineData("nl", "nl")]
    [InlineData("en", "en")]
    [InlineData("tr", "tr")]
    [InlineData("tr-TR", "tr")]
    [InlineData("en-GB,en;q=0.9", "en")]
    [InlineData("TR", "tr")]
    public void De_gevraagde_taal_wordt_herkend(string kop, string verwacht) =>
        Assert.Equal(verwacht, Verzoek(kop).Taal());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("de")]
    [InlineData("zh-CN,zh;q=0.9")]
    [InlineData("rommel")]
    public void Zonder_of_onbekende_taal_is_Nederlands(string? kop) =>
        Assert.Equal("nl", Verzoek(kop).Taal());

    [Fact]
    public void Een_taal_die_we_kennen_wint_van_een_die_we_niet_kennen()
    {
        // Een telefoon stuurt vaak een rij mee. Staat onze taal er verderop in, dan pakken we die.
        Assert.Equal("tr", Verzoek("de-DE,de;q=0.9,tr;q=0.8").Taal());
    }

    [Fact]
    public void Een_Turkse_aanvrager_krijgt_Turkse_tips()
    {
        // Vrijdag 30 oktober 2026, 09:00 Nederlandse tijd; de goedkope uren liggen daarna.
        var nu = new DateTimeOffset(2026, 10, 30, 8, 0, 0, TimeSpan.Zero);
        var middernacht = new DateTimeOffset(2026, 10, 29, 23, 0, 0, TimeSpan.Zero);

        var prijzen = Enumerable.Range(0, 24)
            .Select(h => new HourPrice(
                middernacht.AddHours(h),
                h is >= 12 and < 15 ? -0.02m : h is >= 18 and < 21 ? 0.30m : 0.10m))
            .ToList();

        AdviceInput Invoer(string? taal) => new(
            nu, prijzen,
            HasPv: false, HasBattery: false, HasCharger: true,
            IsDynamic: true,
            new TariffRates(0.30m, 0.08m),
            AvgDailyChargedKwh: 10,
            AvgDailyExportKwh: 0,
            TypicalBatteryKwh: 0,
            SurchargePerKwh: 0.13m,
            Taal: taal);

        var nederlands = AdviceEngine.Build(Invoer("nl"));
        var turks = AdviceEngine.Build(Invoer("tr"));

        var nlTip = Assert.Single(nederlands.Tips, t => t.Kind == AdviceTipKind.Charger);
        var trTip = Assert.Single(turks.Tips, t => t.Kind == AdviceTipKind.Charger);

        Assert.Contains("Laad de auto", nlTip.Title);
        Assert.Contains("şarj edin", trTip.Title);

        // Hetzelfde advies, alleen een andere taal: het venster en het bedrag horen niet te schuiven.
        Assert.Equal(nlTip.From, trTip.From);
        Assert.Equal(nlTip.SavingEur, trTip.SavingEur);
    }
}
