namespace Wattzicht.App.Services;

/// <summary>
/// De namen van de vier apparaten uit een proefwoning, in de taal van de app.
///
/// Ze krijgen hun naam van de server en dus in één taal. Dat las raar in een app die Engels of Turks
/// staat: "Demo omvormer 4,2 kWp" onder het kopje Inverter. Voor deze vier vertalen we de naam alsnog;
/// ze zijn te herkennen aan hun vaste kenmerk. Een echt apparaat houdt de naam die de bewoner gaf.
/// </summary>
public static class DemoNamen
{
    public static string? Voor(string? kenmerk) => kenmerk switch
    {
        "DEMO-INV-001" => Taal.T("proef.omvormer"),
        "DEMO-BAT-001" => Taal.T("proef.batterij"),
        "DEMO-CHG-001" => Taal.T("proef.laadpaal"),
        "DEMO-P1-001" => Taal.T("proef.meter"),
        _ => null,
    };
}
