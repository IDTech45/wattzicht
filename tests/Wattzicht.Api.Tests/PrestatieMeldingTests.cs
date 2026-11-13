using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Tests;

/// <summary>
/// De melding dat je panelen minder leveren dan het weer toeliet.
///
/// Dit is de enige melding die over geld gaat dat je níét verdient, en die je dus nooit uit jezelf
/// gaat opzoeken. Daarom staat hier vast dat het bedrag en het percentage erin staan: zonder die
/// twee is het een weetje en geen reden om iemand te storen.
/// </summary>
public sealed class PrestatieMeldingTests
{
    private static PerformanceDto Laag(int percent = 62, double verloren = 23.4) =>
        new(PerformanceStatus.Low, percent, verloren, 7.1m, "Levert minder dan verwacht.");

    [Fact]
    public void De_melding_noemt_het_percentage_en_de_gemiste_kilowatturen()
    {
        var (titel, tekst) = PrestatieWorker.Bericht(Laag(), "nl");

        Assert.Contains("minder", titel);
        Assert.Contains("62%", tekst);
        Assert.Contains("23 kWh", tekst);
    }

    [Fact]
    public void En_hij_noemt_waar_het_meestal_aan_ligt()
    {
        // Zonder die zin is het een melding waar je niets mee kunt; met die zin pakt iemand een
        // emmer water of belt hij zijn installateur.
        var (_, tekst) = PrestatieWorker.Bericht(Laag(), "nl");

        Assert.Contains("vuil", tekst);
        Assert.Contains("omvormer", tekst);
    }

    [Theory]
    [InlineData("en", "less")]
    [InlineData("tr", "az")]
    public void Ook_in_de_taal_van_de_bewoner(string taal, string verwacht)
    {
        var (titel, tekst) = PrestatieWorker.Bericht(Laag(), taal);

        Assert.Contains(verwacht, titel);
        Assert.Contains("62", tekst);
    }

    [Fact]
    public void Twee_weken_tussen_twee_meldingen()
    {
        // De oorzaak los je niet op een middag op. Een app die elke dag hetzelfde zegt zet je uit.
        Assert.Equal(TimeSpan.FromDays(14), PrestatieWorker.Tussenpoos);
    }

    [Fact]
    public void Niet_s_nachts()
    {
        Assert.True(PrestatieWorker.VanafUur >= 9);
        Assert.True(PrestatieWorker.TotUur <= 21);
    }
}
