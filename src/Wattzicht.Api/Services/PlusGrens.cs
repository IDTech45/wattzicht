namespace Wattzicht.Api.Services;

/// <summary>
/// Wat een woning zonder Plus wel en niet krijgt.
///
/// Dit stond alleen in docs/VERDIENMODEL.md en nergens in de code. Van de hele lijst daar was in
/// werkelijkheid twee dingen afgedwongen: de aanbiedingen-tegel verdween bij Plus, en de besparing
/// rekende met het eigen tarief in plaats van het landelijk gemiddelde. Al het andere kreeg iedereen
/// gratis. Op de dag dat Plus opengaat zou er dus niets te verkopen zijn.
///
/// Eén plek, zodat de grens niet over tien endpoints verspreid raakt en je in één oogopslag kunt
/// zien wat je verkoopt. Staat hier iets, dan hoort het ook in VERDIENMODEL.md te staan en andersom.
///
/// De regel uit dat document: <b>zien is gratis, begrijpen en besparen is Plus.</b> Koppelen blijft
/// altijd gratis, want dat is de trechter; alles wat koppelen makkelijker maakt gaat vóór alles wat
/// geld oplevert.
/// </summary>
public static class PlusGrens
{
    /// <summary>
    /// Hoeveel dagen historie een woning zonder Plus ziet.
    ///
    /// Dertig dagen is genoeg om te zien dat de app klopt en te weinig om een seizoen te vergelijken.
    /// Precies dat laatste is waar iemand in januari voor betaalt: zien wat december deed tegenover
    /// vorig jaar.
    /// </summary>
    public const int GratisHistorieDagen = 30;

    /// <summary>Hoeveel woningen je zonder Plus mag hebben.</summary>
    public const int GratisWoningen = 1;

    /// <summary>
    /// De reden die de app toont als iets niet mag. Eén tekst, zodat er nergens een eigen variant
    /// ontstaat die net iets anders belooft.
    /// </summary>
    public static string Reden(string functie) =>
        $"{functie} hoort bij Wattzicht Plus.";

    /// <summary>
    /// Begrenst een gevraagde periode tot wat deze woning mag zien.
    ///
    /// Geeft de begindatum terug die werkelijk gebruikt wordt, plus of er is ingekort. Bewust geen
    /// foutmelding: een grafiek die leeg blijft met een melding erboven is onbruikbaar, terwijl
    /// dertig dagen tonen met "verder terugkijken hoort bij Plus" wél iets laat zien én uitlegt
    /// waarom het ophoudt.
    /// </summary>
    public static (DateOnly Vanaf, bool Ingekort) HistorieVanaf(
        DateOnly gevraagd, DateOnly vandaag, bool heeftPlus)
    {
        if (heeftPlus) return (gevraagd, false);

        var vroegste = vandaag.AddDays(-(GratisHistorieDagen - 1));
        return gevraagd < vroegste ? (vroegste, true) : (gevraagd, false);
    }
}
