using System.Text.Json;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests;

/// <summary>
/// De dagverwachting van Open-Meteo: temperatuur, zonuren en wat voor dag het is.
///
/// Waarom dit getest wordt: de WMO-code kent honderd waarden en wij tekenen er zeven iconen bij.
/// Een code die in de verkeerde bak valt levert een zonnetje op een regendag, en dat is precies het
/// soort fout waarna niemand het icoon nog vertrouwt.
///
/// En de randen van de verwachting: de laatste dag mist soms een veld. Een halve dag tonen is erger
/// dan hem weglaten, want nul graden leest als een meting.
/// </summary>
public sealed class DagverwachtingTests
{
    private static JsonElement Json(string tekst) => JsonDocument.Parse(tekst).RootElement;

    [Fact]
    public void Twee_dagen_met_alle_velden_komen_er_allebei_uit()
    {
        var dagen = OpenMeteoWeatherProvider.ParseDaily(Json("""
            {"daily":{
              "time":["2026-11-14","2026-11-15"],
              "weather_code":[0,61],
              "temperature_2m_max":[9.4,7.1],
              "temperature_2m_min":[3.2,4.8],
              "sunshine_duration":[18000,1800]}}
            """));

        Assert.Equal(2, dagen.Count);
        Assert.Equal(new DateOnly(2026, 11, 14), dagen[0].Datum);
        Assert.Equal(WeerSoort.Zonnig, dagen[0].Soort);
        Assert.Equal(9.4, dagen[0].TempMaxC);
        Assert.Equal(5, dagen[0].ZonUren);
        Assert.Equal(WeerSoort.Regen, dagen[1].Soort);
        Assert.Equal(0.5, dagen[1].ZonUren);
    }

    [Fact]
    public void Een_dag_zonder_temperatuur_slaan_we_over()
    {
        var dagen = OpenMeteoWeatherProvider.ParseDaily(Json("""
            {"daily":{
              "time":["2026-11-14","2026-11-15"],
              "weather_code":[3,3],
              "temperature_2m_max":[9.4,null],
              "temperature_2m_min":[3.2,4.8],
              "sunshine_duration":[0,0]}}
            """));

        Assert.Single(dagen);
        Assert.Equal(new DateOnly(2026, 11, 14), dagen[0].Datum);
    }

    [Fact]
    public void Zonder_daily_blok_krijgen_we_een_lege_lijst_en_geen_fout()
    {
        Assert.Empty(OpenMeteoWeatherProvider.ParseDaily(Json("""{"error":true}""")));
        Assert.Empty(OpenMeteoWeatherProvider.ParseDaily(Json("""{"daily":{}}""")));
    }

    [Fact]
    public void Ontbrekende_zonuren_worden_nul_en_niet_niets()
    {
        // Zonuren zijn versiering bij de temperatuur; ontbreken ze, dan is de dag nog bruikbaar.
        var dagen = OpenMeteoWeatherProvider.ParseDaily(Json("""
            {"daily":{
              "time":["2026-11-14"],
              "weather_code":[2],
              "temperature_2m_max":[9.4],
              "temperature_2m_min":[3.2]}}
            """));

        Assert.Single(dagen);
        Assert.Equal(0, dagen[0].ZonUren);
        Assert.Equal(WeerSoort.HalfBewolkt, dagen[0].Soort);
    }

    [Theory]
    [InlineData(0, WeerSoort.Zonnig)]
    [InlineData(1, WeerSoort.Zonnig)]
    [InlineData(2, WeerSoort.HalfBewolkt)]
    [InlineData(3, WeerSoort.Bewolkt)]
    [InlineData(45, WeerSoort.Mist)]
    [InlineData(48, WeerSoort.Mist)]
    [InlineData(51, WeerSoort.Regen)]
    [InlineData(65, WeerSoort.Regen)]
    [InlineData(67, WeerSoort.Regen)]
    [InlineData(80, WeerSoort.Regen)]
    [InlineData(82, WeerSoort.Regen)]
    [InlineData(71, WeerSoort.Sneeuw)]
    [InlineData(77, WeerSoort.Sneeuw)]
    [InlineData(86, WeerSoort.Sneeuw)]
    [InlineData(95, WeerSoort.Onweer)]
    [InlineData(99, WeerSoort.Onweer)]
    public void Elke_weercode_valt_in_de_juiste_bak(int code, WeerSoort verwacht)
    {
        Assert.Equal(verwacht, OpenMeteoWeatherProvider.Soort(code));
    }

    [Fact]
    public void Een_code_die_we_niet_kennen_wordt_bewolkt_en_geen_zon()
    {
        // Als Open-Meteo er ooit een code bij doet, hoort hij aan de veilige kant te vallen: een
        // onbekende dag "zonnig" noemen is een belofte die we niet kunnen nakomen.
        Assert.Equal(WeerSoort.Bewolkt, OpenMeteoWeatherProvider.Soort(4));
        Assert.Equal(WeerSoort.Bewolkt, OpenMeteoWeatherProvider.Soort(-1));
    }
}
