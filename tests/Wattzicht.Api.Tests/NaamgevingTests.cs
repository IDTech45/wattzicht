using Wattzicht.Api.Services;
using Xunit;

namespace Wattzicht.Api.Tests;

/// <summary>
/// "Goedenavond, Thuis."
///
/// Zo begroette het beginscherm iedereen die met Google of Apple binnenkwam, want wij vulden de
/// woningnaam met onze eigen beginwaarde. De naam zat al die tijd in het token dat we toch al
/// controleerden; we deden er alleen niets mee.
/// </summary>
public class NaamgevingTests
{
    [Fact]
    public void Wat_iemand_zelf_invult_gaat_voor()
    {
        Assert.Equal("De Boerderij", Naamgeving.Kies("De Boerderij", "Ibrahim Dogan"));
    }

    [Fact]
    public void Anders_de_naam_van_google()
    {
        Assert.Equal("Ibrahim Dogan", Naamgeving.Kies(null, "Ibrahim Dogan"));
        Assert.Equal("Ibrahim Dogan", Naamgeving.Kies("   ", " Ibrahim Dogan "));
    }

    [Fact]
    public void En_anders_thuis()
    {
        Assert.Equal("Thuis", Naamgeving.Kies(null, null));
        Assert.Equal("Thuis", Naamgeving.Kies(null, ""));
    }

    [Fact]
    public void Een_mailadres_is_geen_naam()
    {
        // Apple geeft bij een verborgen adres soms het adres zelf terug. "Goedenavond,
        // km5tt5rms6@privaterelay.appleid.com" is erger dan Thuis.
        Assert.Equal("Thuis", Naamgeving.Kies(null, "km5tt5rms6@privaterelay.appleid.com"));
        Assert.False(Naamgeving.MagOvernemen("Thuis", "km5tt5rms6@privaterelay.appleid.com"));
    }

    [Fact]
    public void Een_woning_die_nog_thuis_heet_krijgt_de_naam_alsnog()
    {
        // Dit repareert de accounts die er al zijn, bij de eerstvolgende keer inloggen.
        Assert.True(Naamgeving.MagOvernemen("Thuis", "Ibrahim Dogan"));
        Assert.True(Naamgeving.MagOvernemen("thuis", "Ibrahim Dogan"));
        Assert.True(Naamgeving.MagOvernemen(null, "Ibrahim Dogan"));
    }

    [Fact]
    public void Een_zelfgekozen_naam_blijft_staan()
    {
        Assert.False(Naamgeving.MagOvernemen("De Boerderij", "Ibrahim Dogan"));
        Assert.False(Naamgeving.MagOvernemen("Thuis", null));
    }

    [Fact]
    public void Een_letter_is_geen_naam()
    {
        Assert.Equal("Thuis", Naamgeving.Kies(null, "I"));
        Assert.Equal("Thuis", Naamgeving.Kies(null, new string('x', 61)));
    }
}
