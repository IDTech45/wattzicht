using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests;

/// <summary>
/// De tip van vandaag en de zonverwachting in het advies.
///
/// Het probleem dat dit oplost: de adviesmotor ging uit van een vast venster van twaalf tot drie, en
/// zei daardoor op een grijze novemberdag precies hetzelfde als op een heldere dag in juni. Dat is
/// geen advies maar een spreuk, en iemand die er twee keer naast zit gelooft de rest ook niet meer.
///
/// Nu komen het venster en de verwachte opbrengst uit de echte uurverwachting. Zonder verwachting
/// (geen plaats bekend, weerdienst onbereikbaar) valt alles terug op de middag, zoals het was.
/// </summary>
public sealed class DagTipTests
{
    private static readonly DateOnly Vandaag = new(2027, 6, 15);
    private static readonly DateTimeOffset Ochtend = new(2027, 6, 15, 6, 0, 0, TimeSpan.Zero);

    // ---- Het venster -------------------------------------------------------

    [Fact]
    public void Het_venster_volgt_de_zon_en_niet_de_klok()
    {
        // Een dag waarop het pas laat in de middag opklaart: de piek ligt tussen 15 en 18 uur lokaal.
        var invoer = Invoer(Zon(new Dictionary<int, double>
        {
            [9] = 50, [10] = 60, [11] = 70, [12] = 80, [13] = 90, [14] = 200,
            [15] = 600, [16] = 650, [17] = 620, [18] = 200,
        }));

        var (from, to) = AdviceEngine.ZonnigsteVenster(invoer, Vandaag);

        Assert.Equal(15, DutchTime.ToLocal(from).Hour);
        Assert.Equal(18, DutchTime.ToLocal(to).Hour);
    }

    [Fact]
    public void Zonder_verwachting_blijft_het_de_middag()
    {
        var invoer = Invoer(forecast: []);

        var (from, to) = AdviceEngine.ZonnigsteVenster(invoer, Vandaag);

        Assert.Equal(12, DutchTime.ToLocal(from).Hour);
        Assert.Equal(15, DutchTime.ToLocal(to).Hour);
    }

    [Fact]
    public void Uren_die_al_voorbij_zijn_tellen_niet_mee()
    {
        // Om vier uur 's middags heeft een advies voor tien uur 's ochtends geen zin meer.
        var invoer = Invoer(
            Zon(new Dictionary<int, double> { [9] = 700, [10] = 800, [11] = 750, [16] = 300, [17] = 280, [18] = 250 }),
            nu: new DateTimeOffset(2027, 6, 15, 14, 0, 0, TimeSpan.Zero));

        var (from, _) = AdviceEngine.ZonnigsteVenster(invoer, Vandaag);

        Assert.True(from >= invoer.NowUtc);
        Assert.Equal(16, DutchTime.ToLocal(from).Hour);
    }

    // ---- De tip ------------------------------------------------------------

    [Fact]
    public void Een_zonnige_dag_levert_een_tip_met_het_aantal_kilowatturen()
    {
        var invoer = Invoer(ZonnigeDag(), heeftLaadpaal: true);

        var tip = DagTip.Bouw(invoer, Vandaag, AdviceEngine.Build(invoer));

        Assert.NotNull(tip);
        Assert.Equal("zon", tip!.Kind);
        Assert.Contains("zonnig", tip.Headline.ToLowerInvariant());
        Assert.Contains("kWh", tip.Body);
        Assert.Contains("auto", tip.Body);
        Assert.True(tip.ExpectedSolarKwh > DagTip.ZonnigVanafKwh);
    }

    [Fact]
    public void Een_grijze_dag_zonder_prijsverschil_levert_geen_tip()
    {
        // Dit is het punt. Vroeger kwam er ook op zo'n dag een advies over de middaguren.
        var invoer = Invoer(Zon(new Dictionary<int, double> { [12] = 30, [13] = 35, [14] = 30 }));

        Assert.Null(DagTip.Bouw(invoer, Vandaag, AdviceEngine.Build(invoer)));
    }

    [Fact]
    public void Zonder_zonnepanelen_geen_zon_tip()
    {
        var invoer = Invoer(ZonnigeDag(), heeftPv: false);

        var tip = DagTip.Bouw(invoer, Vandaag, AdviceEngine.Build(invoer));

        Assert.True(tip is null || tip.Kind != "zon");
    }

    [Fact]
    public void Zonder_laadpaal_of_batterij_gaat_de_tip_over_de_was()
    {
        var invoer = Invoer(ZonnigeDag(), heeftLaadpaal: false, heeftBatterij: false);

        var tip = DagTip.Bouw(invoer, Vandaag, AdviceEngine.Build(invoer));

        Assert.NotNull(tip);
        Assert.Contains("was", tip!.Body);
        Assert.DoesNotContain("auto", tip.Body);
    }

    [Fact]
    public void In_2026_staat_erbij_dat_het_nog_weinig_scheelt()
    {
        // Met saldering levert zelf gebruiken in plaats van terugleveren niets op. Dat eerlijk zeggen
        // is beter dan een bedrag noemen dat deze bewoner dit jaar niet gaat zien.
        var in2026 = new DateOnly(2026, 6, 15);
        var invoer = Invoer(ZonnigeDag(dag: in2026), nu: new DateTimeOffset(2026, 6, 15, 6, 0, 0, TimeSpan.Zero));

        var tip = DagTip.Bouw(invoer, in2026, AdviceEngine.Build(invoer));

        Assert.NotNull(tip);
        Assert.Equal(0m, tip!.SavingEur);
        Assert.Contains("2027", tip.Body);
    }

    [Fact]
    public void In_2027_staat_er_een_bedrag_in()
    {
        var invoer = Invoer(ZonnigeDag(), heeftLaadpaal: true);

        var tip = DagTip.Bouw(invoer, Vandaag, AdviceEngine.Build(invoer));

        Assert.NotNull(tip);
        Assert.True(tip!.SavingEur > 0m);
    }

    // ---- Hulpstukken -------------------------------------------------------

    private static AdviceInput Invoer(
        IReadOnlyList<HourlyIrradiance> forecast,
        bool heeftPv = true,
        bool heeftLaadpaal = false,
        bool heeftBatterij = false,
        DateTimeOffset? nu = null) =>
        new(
            NowUtc: nu ?? Ochtend,
            Prices: [],
            HasPv: heeftPv,
            HasBattery: heeftBatterij,
            HasCharger: heeftLaadpaal,
            IsDynamic: false,
            Rates: new TariffRates(0.28m, 0.05m),
            AvgDailyChargedKwh: 10,
            AvgDailyExportKwh: 8,
            TypicalBatteryKwh: 5,
            Forecast: forecast,
            PvPeakKw: 4.0);

    /// <summary>Straling per lokaal uur van de dag, omgezet naar UTC-uren.</summary>
    private static IReadOnlyList<HourlyIrradiance> Zon(Dictionary<int, double> perLokaalUur, DateOnly? dag = null)
    {
        var begin = DutchTime.StartOfLocalDayUtc(dag ?? Vandaag);
        return perLokaalUur
            .OrderBy(p => p.Key)
            .Select(p => new HourlyIrradiance(begin.AddHours(p.Key), p.Value))
            .ToList();
    }

    /// <summary>Een heldere zomerdag: ruim boven de drempel voor "zonnig".</summary>
    private static IReadOnlyList<HourlyIrradiance> ZonnigeDag(DateOnly? dag = null) =>
        Zon(new Dictionary<int, double>
        {
            [7] = 100, [8] = 220, [9] = 380, [10] = 520, [11] = 640, [12] = 720,
            [13] = 750, [14] = 700, [15] = 600, [16] = 450, [17] = 300, [18] = 150,
        }, dag);
}
