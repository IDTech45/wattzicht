namespace Wattzicht.Api.Services;

/// <summary>Eén meting zoals de sessieherkenning hem nodig heeft. Losgekoppeld van de database om te kunnen testen.</summary>
/// <param name="Tijd">Moment van de meting.</param>
/// <param name="LaadpaalW">Wat de laadpaal op dat moment trok, in watt.</param>
/// <param name="NetW">Netkant: positief is afname, negatief is teruglevering.</param>
/// <param name="HeeftNet">Is er een netmeting? Zonder P1 of CT-klemmen weten we de herkomst niet.</param>
public readonly record struct Meetpunt(DateTimeOffset Tijd, double LaadpaalW, double NetW, bool HeeftNet);

/// <summary>Wat er van één laadbeurt overblijft als hij klaar is.</summary>
/// <param name="Start">Begin van de laadbeurt.</param>
/// <param name="Eind">Einde: het laatste moment waarop de paal nog vermogen trok.</param>
/// <param name="Kwh">Totaal geladen.</param>
/// <param name="ZonKwh">Deel daarvan dat niet van het net kwam.</param>
/// <param name="HerkomstBekend">
/// False als er geen netmeting was. Dan staat ZonKwh op nul en mogen we daar niets over zeggen,
/// want nul kan dan net zo goed alles betekenen.
/// </param>
public sealed record Laadsessie(DateTimeOffset Start, DateTimeOffset Eind, double Kwh, double ZonKwh, bool HerkomstBekend)
{
    /// <summary>Aandeel eigen opwek, 0 tot 1. Nul als we de herkomst niet weten.</summary>
    public double ZonAandeel => HerkomstBekend && Kwh > 0 ? Math.Clamp(ZonKwh / Kwh, 0, 1) : 0;
}

/// <summary>
/// Herkent laadbeurten in de metingen, en rekent uit hoeveel daarvan uit eigen opwek kwam.
///
/// Waarom dit bestaat: Wattzicht vertelt vooraf wanneer je het voordeligst laadt (zie ChargePlanner
/// en SmartMomentWorker), maar hoorde daarna nooit meer iets. Het laadvermogen verdween in een
/// grafiek en niemand kreeg te horen wat die beurt had gedaan. Dit is het stuk dat de cirkel sluit:
/// na afloop één bericht met wat erin ging en hoeveel daarvan van je eigen dak kwam.
///
/// De splitsing gaat bewust via de netkant en niet via het huisverbruik. Of de auto al in dat
/// huisverbruik zit hangt af van waar de meting vandaan komt: een P1-meter ziet alles achter de
/// meter, een omvormer niet. Met de netkant hoeven we dat niet te weten. Trekt de paal c watt en
/// staat het net op g watt afname, dan kwam hoogstens min(c, g) van het net; de rest kwam van je
/// eigen dak of uit je thuisbatterij. Dat laatste tellen we mee als eigen stroom, want dat is het ook:
/// het is je eigen zon van een paar uur eerder.
///
/// Dit is een benadering. Met een meting per vijf minuten mis je een wolk die er net tussendoor
/// trekt. Ruim nauwkeurig genoeg voor "hoeveel laadde ik op eigen zon", en nooit genoeg voor een
/// afrekening; zo moeten we het ook nooit noemen.
/// </summary>
public static class LaadsessieDetector
{
    /// <summary>Hieronder is het geen laden maar ruis: standby, een verlichtingsring, een meetfout.</summary>
    public const double DrempelW = 200;

    /// <summary>Zo lang mag de paal stilvallen zonder dat het een nieuwe beurt wordt (auto die even pauzeert).</summary>
    public static readonly TimeSpan MaxPauze = TimeSpan.FromMinutes(15);

    /// <summary>Onder deze hoeveelheid melden we niets; voor twee ons stroom hoef je niemand te storen.</summary>
    public const double MinimumKwh = 0.5;

    /// <summary>
    /// Alle afgeronde laadbeurten in deze reeks, op volgorde van tijd. Een beurt die aan het eind van
    /// de reeks nog loopt telt niet mee: die is nog bezig, en daar valt nog niets over te zeggen.
    /// </summary>
    public static IReadOnlyList<Laadsessie> Vind(IReadOnlyList<Meetpunt> metingen)
    {
        var gevonden = new List<Laadsessie>();
        if (metingen.Count < 2) return gevonden;

        var punten = metingen.OrderBy(m => m.Tijd).ToList();

        DateTimeOffset? start = null;
        DateTimeOffset laatstActief = default;
        double kwh = 0, zonKwh = 0;
        var netGezien = false;

        for (var i = 1; i < punten.Count; i++)
        {
            var vorige = punten[i - 1];
            var huidige = punten[i];
            var duur = huidige.Tijd - vorige.Tijd;

            // Een gat in de metingen is geen laadbeurt van een uur. De telefoon lag in een la, of de
            // server stond stil; wat daartussen gebeurde weten we niet en verzinnen we niet.
            if (duur <= TimeSpan.Zero || duur > MaxPauze)
            {
                if (start is { } s1) Sluit(gevonden, s1, laatstActief, kwh, zonKwh, netGezien);
                start = null; kwh = 0; zonKwh = 0; netGezien = false;
                continue;
            }

            // Allebei de uiteinden moeten laden, niet één. Met "één van beide" telde de stap van
            // 11 kW naar nul half mee als laadenergie, en werd een uur op elf kilowatt ineens
            // 11,46 kWh. Waar de paal precies binnen die vijf minuten stopte weten we niet, en dan
            // is te weinig tellen beter dan energie bijschrijven nadat de auto al klaar was.
            // Twee verschillende vragen, en ze hebben een ander antwoord nodig. Voor de teller: trok de
            // paal aan bééd uiteinden stroom? Alleen dan weten we wat er tussenin gebeurde. Voor de
            // vraag of de beurt nog loopt is één kant genoeg; anders eindigt een laadbeurt al bij het
            // eerste meetpunt waarop de auto even niets vroeg.
            var trektNu = huidige.LaadpaalW >= DrempelW;
            if (trektNu || vorige.LaadpaalW >= DrempelW) laatstActief = trektNu ? huidige.Tijd : vorige.Tijd;

            var actief = vorige.LaadpaalW >= DrempelW && trektNu;
            if (actief)
            {
                start ??= vorige.Tijd;

                // Trapezium: het gemiddelde van begin en eind van het interval. Met alleen de
                // beginwaarde loopt elke sessie structureel te hoog of te laag af.
                var uren = duur.TotalHours;
                var paalKw = (vorige.LaadpaalW + huidige.LaadpaalW) / 2000.0;
                kwh += paalKw * uren;

                if (vorige.HeeftNet && huidige.HeeftNet)
                {
                    netGezien = true;
                    var netKw = (vorige.NetW + huidige.NetW) / 2000.0;
                    // Van wat de auto trok kwam hoogstens de afname van het net; teruglevering (negatief)
                    // betekent dat er niets van het net kwam.
                    var vanNet = Math.Max(0, Math.Min(paalKw, netKw));
                    zonKwh += Math.Max(0, paalKw - vanNet) * uren;
                }
            }
            else if (start is { } s2 && !trektNu && huidige.Tijd - laatstActief >= MaxPauze)
            {
                Sluit(gevonden, s2, laatstActief, kwh, zonKwh, netGezien);
                start = null; kwh = 0; zonKwh = 0; netGezien = false;
            }
        }

        // Een beurt die aan het eind van de reeks nog loopt laten we open: pas als de stekker eruit
        // gaat valt er iets te melden, en anders zou dezelfde beurt elke ronde opnieuw voorbijkomen.
        return gevonden;
    }

    private static void Sluit(List<Laadsessie> gevonden, DateTimeOffset start, DateTimeOffset eind,
        double kwh, double zonKwh, bool netGezien)
    {
        if (kwh < MinimumKwh) return;
        gevonden.Add(new Laadsessie(start, eind, Math.Round(kwh, 2), Math.Round(netGezien ? zonKwh : 0, 2), netGezien));
    }
}
