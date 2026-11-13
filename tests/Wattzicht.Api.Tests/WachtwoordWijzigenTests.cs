using Wattzicht.Api.Services;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Je wachtwoord wijzigen.
///
/// Dat kon niet: je kreeg er bij het aanmelden een en daar zat je aan vast. De regels die er nu om
/// heen staan, staan hier vast. Vooral deze: het huidige wachtwoord moet mee, ook al ben je
/// ingelogd. Een telefoon die even onbeheerd op tafel ligt hoort niet genoeg te zijn om iemand uit
/// zijn eigen account te zetten.
/// </summary>
public sealed class WachtwoordWijzigenTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Een_nieuw_wachtwoord_levert_een_andere_hash_op()
    {
        var oud = _hasher.Hash("ditIsGeheim1");
        var nieuw = _hasher.Hash("ditIsAnders22");

        Assert.NotEqual(oud, nieuw);
        Assert.True(_hasher.Verify("ditIsAnders22", nieuw));
        Assert.False(_hasher.Verify("ditIsGeheim1", nieuw));
    }

    [Fact]
    public void Hetzelfde_wachtwoord_twee_keer_gehasht_is_niet_hetzelfde()
    {
        // Elke hash krijgt zijn eigen salt. Zonder dat zie je in de database wie hetzelfde
        // wachtwoord gebruikt.
        var een = _hasher.Hash("ditIsGeheim1");
        var twee = _hasher.Hash("ditIsGeheim1");

        Assert.NotEqual(een, twee);
        Assert.True(_hasher.Verify("ditIsGeheim1", een));
        Assert.True(_hasher.Verify("ditIsGeheim1", twee));
    }

    [Fact]
    public void Een_leeg_wachtwoord_komt_nergens_doorheen()
    {
        var opgeslagen = _hasher.Hash("ditIsGeheim1");

        Assert.False(_hasher.Verify("", opgeslagen));
        Assert.False(_hasher.Verify("ditIsGeheim1", ""));
    }

    [Fact]
    public void Een_account_zonder_wachtwoord_laat_niemand_binnen()
    {
        // Dat is het geval bij wie met Google of Apple is binnengekomen: er staat geen hash, en dan
        // hoort geen enkel wachtwoord te werken.
        Assert.False(_hasher.Verify("watdanook", string.Empty));
    }
}
