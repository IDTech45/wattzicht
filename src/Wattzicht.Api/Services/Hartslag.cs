namespace Wattzicht.Api.Services;

/// <summary>
/// Wanneer draaide de uitleesronde voor het laatst, en met welke uitkomst.
///
/// Waarom dit bestaat: een ronde die stopt meldt zichzelf nooit. In LaadCash draaide de sync
/// wekenlang helemaal niet omdat de planner de taak niet kon aanmaken; niemand zag het, tot er in
/// één keer 1.329 kWh binnenkwam. Wattzicht heeft dezelfde blinde vlek, en hier is het erger: een
/// gemiste ronde is een gat in iemands grafiek, en dat gat komt niet meer terug.
///
/// Bewust in het geheugen en niet in de database. Het gaat om de vraag "draait dit proces nog",
/// niet om geschiedenis; na een herstart hoort de teller ook echt opnieuw te beginnen, want dan is
/// er net een verse ronde geweest. Een tabel zou bovendien suggereren dat je er iets uit kunt
/// terugkijken, en dat is niet zo.
/// </summary>
public sealed class Hartslag
{
    private readonly TimeProvider _clock;
    private readonly object _slot = new();

    private DateTimeOffset? _laatsteRonde;
    private DateTimeOffset _gestartOp;
    private int _huishoudens;
    private int _apparatenOk;
    private int _apparatenMislukt;

    public Hartslag(TimeProvider clock)
    {
        _clock = clock;
        _gestartOp = clock.GetUtcNow();
    }

    /// <summary>De ronde is klaar; vanaf nu loopt de klok opnieuw.</summary>
    public void Rondeklaar(int huishoudens, int apparatenOk, int apparatenMislukt)
    {
        lock (_slot)
        {
            _laatsteRonde = _clock.GetUtcNow();
            _huishoudens = huishoudens;
            _apparatenOk = apparatenOk;
            _apparatenMislukt = apparatenMislukt;
        }
    }

    /// <summary>
    /// Hoe lang geleden draaide de laatste ronde? Is er nog nooit een ronde geweest, dan telt de
    /// tijd vanaf het starten van de server: anders meldt een proces dat meteen na de start kapot
    /// gaat zich nooit als stil.
    /// </summary>
    public TimeSpan Stilte()
    {
        lock (_slot)
        {
            return _clock.GetUtcNow() - (_laatsteRonde ?? _gestartOp);
        }
    }

    public HartslagStand Stand()
    {
        lock (_slot)
        {
            return new HartslagStand(_laatsteRonde, Stilte(), _huishoudens, _apparatenOk, _apparatenMislukt);
        }
    }
}

/// <param name="LaatsteRonde">Null als er sinds het starten nog geen ronde klaar is.</param>
public readonly record struct HartslagStand(
    DateTimeOffset? LaatsteRonde,
    TimeSpan Stilte,
    int Huishoudens,
    int ApparatenOk,
    int ApparatenMislukt);
