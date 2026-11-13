using Wattzicht.Api.Connectors;
using Wattzicht.Api.Data;

namespace Wattzicht.Api.Services;

/// <summary>
/// Wat de omvormer over de installatie vertelt, overnemen in de woning.
///
/// Waarom dit bestaat: het piekvermogen stond als invulveld in de app. Dat is een vraag die bijna
/// niemand uit zijn hoofd kan beantwoorden, terwijl het apparaat dat wij net hebben uitgelezen het
/// gewoon meldt. Een vraag stellen waarvan je het antwoord al hebt, is een vraag te veel.
///
/// Eén regel is belangrijker dan de rest: een getal dat de bewoner zelf heeft ingevuld blijft staan.
/// Wie zijn offerte erbij pakt en 6,4 invult, hoort dat de volgende ochtend niet overschreven te
/// zien door 6,2 uit een systeem dat een paneel in de schaduw niet meetelt. Het aantal panelen is
/// een ander geval: dat kan de bewoner nergens invullen, dus dat volgt altijd de omvormer.
/// </summary>
public static class InstallatieFeiten
{
    /// <summary>Boven dit vermogen is het geen thuisinstallatie meer en geloven we de melding niet.</summary>
    public const double MaximumKwp = 100;

    /// <summary>Geeft terug of er iets veranderd is.</summary>
    public static bool Overnemen(Household woning, DeviceReading meting)
    {
        var veranderd = false;

        if (meting.PvPeakW is { } watt && watt > 0)
        {
            var kwp = Math.Round(watt / 1000.0, 2);
            if (woning.PvPeakKw is null && kwp is > 0 and <= MaximumKwp)
            {
                woning.PvPeakKw = kwp;
                veranderd = true;
            }
        }

        if (meting.PanelCount is { } aantal && aantal > 0 && woning.PvPanelCount != aantal)
        {
            woning.PvPanelCount = aantal;
            veranderd = true;
        }

        return veranderd;
    }
}
