using Wattzicht.Api.Services;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Een export uit het portaal van een fabrikant inlezen.
///
/// Dit is het soort functie dat je niet twee keer kunt testen met het echte bestand: elk portaal
/// exporteert anders, en dezelfde fabrikant doet het per land weer net iets anders. Daarom staat
/// hier per variant een regel: punt of komma als decimaalteken, puntkomma of komma als
/// scheidingsteken, Wh of kWh, per uur of per dag, met of zonder rommel boven de kopregel.
///
/// En net zo belangrijk: wat er moet gebeuren als het niet lukt. Een bestand dat we niet snappen
/// hoort te vertellen welke kolommen er wél in staan. Anders is het enige wat iemand weet
/// "ongeldig bestand", en dan kan niemand er iets mee, ook wij niet.
/// </summary>
public sealed class HistorieImportTests
{
    private static readonly DateOnly Vandaag = new(2026, 11, 13);

    [Fact]
    public void Een_dagexport_in_wattuur()
    {
        var csv = """
            Date,Energy Produced (Wh)
            2026-05-01,13500
            2026-05-02,9800
            """;

        var uit = HistorieImport.Lees(csv, Vandaag);

        Assert.True(uit.Gelukt, uit.Fout);
        Assert.Equal(2, uit.Dagen.Count);
        Assert.Equal(new DateOnly(2026, 5, 1), uit.Dagen[0].Datum);
        Assert.Equal(13.5, uit.Dagen[0].Kwh, 3);
        Assert.Equal(9.8, uit.Dagen[1].Kwh, 3);
    }

    [Fact]
    public void Een_export_in_kilowattuur_wordt_niet_nog_eens_gedeeld()
    {
        var csv = """
            Datum;Opwek (kWh)
            01-05-2026;13,5
            """;

        var uit = HistorieImport.Lees(csv, Vandaag);

        Assert.True(uit.Gelukt, uit.Fout);
        Assert.Equal(13.5, Assert.Single(uit.Dagen).Kwh, 3);
    }

    [Fact]
    public void Uurregels_worden_per_dag_opgeteld()
    {
        // Enphase exporteert standaard per kwartier of per uur. Vijf uur van 2 kWh is één dag van 10.
        var csv = """
            Date/Time,Energy Produced (Wh)
            2026-06-21 10:00:00 +0200,2000
            2026-06-21 11:00:00 +0200,2000
            2026-06-21 12:00:00 +0200,2000
            2026-06-21 13:00:00 +0200,2000
            2026-06-21 14:00:00 +0200,2000
            2026-06-22 12:00:00 +0200,500
            """;

        var uit = HistorieImport.Lees(csv, Vandaag);

        Assert.Equal(2, uit.Dagen.Count);
        Assert.Equal(10, uit.Dagen[0].Kwh, 3);
        Assert.Equal(0.5, uit.Dagen[1].Kwh, 3);
    }

    [Fact]
    public void Regels_boven_de_kopregel_worden_overgeslagen()
    {
        // Enphase zet de naam van het systeem en de periode boven de tabel.
        var csv = """
            Enphase Energy
            Systeem: Dogan, Enschede
            Periode: 1 mei 2026 - 2 mei 2026

            Date,Energy Produced (Wh)
            2026-05-01,13500
            """;

        var uit = HistorieImport.Lees(csv, Vandaag);

        Assert.True(uit.Gelukt, uit.Fout);
        Assert.Equal(13.5, Assert.Single(uit.Dagen).Kwh, 3);
    }

    [Fact]
    public void Duizendscheidingen_in_beide_notaties()
    {
        var csv = """
            Date,Energy Produced (Wh)
            2026-05-01,"1.234,5"
            2026-05-02,"1,234.5"
            2026-05-03,"12 345"
            """;

        var uit = HistorieImport.Lees(csv, Vandaag);

        Assert.Equal(3, uit.Dagen.Count);
        Assert.Equal(1.2345, uit.Dagen[0].Kwh, 4);
        Assert.Equal(1.2345, uit.Dagen[1].Kwh, 4);
        Assert.Equal(12.345, uit.Dagen[2].Kwh, 4);
    }

    [Fact]
    public void Een_eenheid_in_de_cel_hoort_er_ook_af()
    {
        var csv = """
            Datum,Productie
            2026-05-01,13.5 kWh
            """;

        var uit = HistorieImport.Lees(csv, Vandaag);

        Assert.Equal(13.5, Assert.Single(uit.Dagen).Kwh, 3);
    }

    [Fact]
    public void Een_dag_in_de_toekomst_telt_niet_mee()
    {
        // Een verkeerd gelezen datum (maand en dag omgewisseld) kan in de toekomst vallen. Zo'n
        // regel weggooien is beter dan een jaaroverzicht met een piek in 2027.
        var csv = """
            Date,Energy Produced (Wh)
            2026-05-01,13500
            2027-05-01,9000
            """;

        var uit = HistorieImport.Lees(csv, Vandaag);

        Assert.Equal(new DateOnly(2026, 5, 1), Assert.Single(uit.Dagen).Datum);
    }

    [Fact]
    public void Negatieve_opwek_bestaat_niet()
    {
        var csv = """
            Date,Energy Produced (Wh)
            2026-05-01,-500
            2026-05-02,1000
            """;

        var uit = HistorieImport.Lees(csv, Vandaag);

        Assert.Equal(new DateOnly(2026, 5, 2), Assert.Single(uit.Dagen).Datum);
    }

    [Fact]
    public void Een_nederlandse_datum_is_dag_eerst()
    {
        // 03/04/2026 is hier 3 april. In een Amerikaanse export zou het 4 maart zijn; onze klanten
        // downloaden Nederlandse bestanden, dus die lezing gaat voor.
        var csv = """
            Datum,Opwek (kWh)
            03/04/2026,10
            """;

        var uit = HistorieImport.Lees(csv, Vandaag);

        Assert.Equal(new DateOnly(2026, 4, 3), Assert.Single(uit.Dagen).Datum);
    }

    [Fact]
    public void Een_bestand_zonder_de_juiste_kolommen_zegt_wat_er_wel_in_staat()
    {
        var csv = """
            Serienummer,Vermogen (W),Status
            123456,350,Normaal
            """;

        var uit = HistorieImport.Lees(csv, Vandaag);

        Assert.False(uit.Gelukt);
        Assert.Contains("geen kolom", uit.Fout);
        Assert.Contains("Serienummer", uit.GezieneKoppen);
        Assert.Contains("Status", uit.GezieneKoppen);
    }

    [Fact]
    public void Een_leeg_bestand_is_geen_crash()
    {
        Assert.False(HistorieImport.Lees("", Vandaag).Gelukt);
        Assert.False(HistorieImport.Lees("   \n\n", Vandaag).Gelukt);
    }

    [Fact]
    public void Kolommen_zonder_bruikbare_regels_melden_dat_apart()
    {
        var csv = """
            Date,Energy Produced (Wh)
            onleesbaar,rommel
            """;

        var uit = HistorieImport.Lees(csv, Vandaag);

        Assert.False(uit.Gelukt);
        Assert.Contains("geen enkele regel", uit.Fout);
    }

    [Fact]
    public void Een_tab_gescheiden_bestand_kan_ook()
    {
        var csv = "Date\tEnergy Produced (Wh)\n2026-05-01\t13500";

        var uit = HistorieImport.Lees(csv, Vandaag);

        Assert.Equal(13.5, Assert.Single(uit.Dagen).Kwh, 3);
    }
}
