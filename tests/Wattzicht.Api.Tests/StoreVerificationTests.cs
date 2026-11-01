using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Wattzicht.Api.Services;
using Wattzicht.Api.Tests.Connectors;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Het lezen van wat Apple en Google terugsturen.
///
/// Waarom dit los getest wordt: dit is de enige plek waar bepaald wordt of iemand echt betaald
/// heeft. Alles wat hier "ja" zegt, levert een abonnement op. Dus moet het ook "nee" zeggen bij een
/// product dat niet van ons is, bij een periode die al voorbij is, en bij een abonnement dat de
/// store zelf niet meer actief noemt.
///
/// De echte aanroep naar Apple of Google staat hier niet in; die vraagt sleutels die alleen op de
/// server horen. Wat hier getest wordt is het antwoord uitlezen, en dat is precies het stuk waar een
/// verkeerd gelezen veld stilletjes iemand gratis Plus geeft.
/// </summary>
public sealed class StoreVerificationTests
{
    private static readonly DateTimeOffset Nu = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _klok = new(Nu);

    // ---- Apple -------------------------------------------------------------

    [Fact]
    public void Apple_een_lopend_jaarabonnement_wordt_herkend()
    {
        var bon = Apple().LeesAntwoord(AppleAntwoord(
            productId: StoreProducts.Yearly,
            origineel: "2000000123456789",
            verlooptOp: Nu.AddDays(300)));

        Assert.True(bon.Valid);
        Assert.Equal(StoreProducts.Yearly, bon.ProductId);
        Assert.Equal("2000000123456789", bon.OriginalTransactionId);
        Assert.Equal(Nu.AddDays(300), bon.ExpiresAt);
    }

    [Fact]
    public void Apple_een_verlopen_abonnement_geeft_geen_Plus()
    {
        var bon = Apple().LeesAntwoord(AppleAntwoord(
            productId: StoreProducts.Monthly,
            origineel: "2000000123456789",
            verlooptOp: Nu.AddDays(-1)));

        Assert.False(bon.Valid);
        Assert.Contains("niet meer geldig", bon.Error);
    }

    [Fact]
    public void Apple_een_abonnement_op_een_ander_product_telt_niet()
    {
        // Een aankoop uit een andere app, of een product dat wij ooit verwijderd hebben.
        var bon = Apple().LeesAntwoord(AppleAntwoord(
            productId: "nl.ietsanders.pro",
            origineel: "2000000123456789",
            verlooptOp: Nu.AddDays(300)));

        Assert.False(bon.Valid);
    }

    [Fact]
    public void Apple_een_antwoord_zonder_abonnementen_geeft_geen_Plus()
    {
        Assert.False(Apple().LeesAntwoord("""{"data":[]}""").Valid);
    }

    [Fact]
    public async Task Apple_zonder_sleutels_kent_niets_toe()
    {
        var verifier = new AppleStoreVerifier(
            new FakeVendor().Factory(AppleStoreVerifier.ProductionBaseUrl),
            new ConfigurationBuilder().Build(),
            _klok,
            NullLogger<AppleStoreVerifier>.Instance);

        Assert.False(verifier.Configured);
        var bon = await verifier.VerifyAsync("2000000123456789");

        Assert.False(bon.Valid);
        Assert.Contains("nog niet ingesteld", bon.Error);
    }

    // ---- Google ------------------------------------------------------------

    [Fact]
    public void Google_een_actief_abonnement_wordt_herkend()
    {
        var bon = Google().LeesAntwoord(
            GoogleAntwoord("SUBSCRIPTION_STATE_ACTIVE", StoreProducts.Monthly, Nu.AddDays(20)),
            "purchase-token-abc");

        Assert.True(bon.Valid);
        Assert.Equal(StoreProducts.Monthly, bon.ProductId);
        // Google kent geen origineel transactienummer; de purchase token blijft bij verlengingen
        // gelijk en is dus waar wij dezelfde klant aan herkennen.
        Assert.Equal("purchase-token-abc", bon.OriginalTransactionId);
    }

    [Fact]
    public void Google_een_respijtperiode_telt_nog_mee()
    {
        // Betaling mislukt, Google probeert het opnieuw. In die dagen houdt de klant gewoon Plus;
        // hem meteen afsluiten om een geweigerde pas is niet uit te leggen.
        var bon = Google().LeesAntwoord(
            GoogleAntwoord("SUBSCRIPTION_STATE_IN_GRACE_PERIOD", StoreProducts.Yearly, Nu.AddDays(3)),
            "purchase-token-abc");

        Assert.True(bon.Valid);
    }

    [Fact]
    public void Google_een_opgezegd_abonnement_geeft_geen_Plus()
    {
        var bon = Google().LeesAntwoord(
            GoogleAntwoord("SUBSCRIPTION_STATE_EXPIRED", StoreProducts.Monthly, Nu.AddDays(20)),
            "purchase-token-abc");

        Assert.False(bon.Valid);
    }

    [Fact]
    public void Google_een_verlopen_einddatum_geeft_geen_Plus()
    {
        var bon = Google().LeesAntwoord(
            GoogleAntwoord("SUBSCRIPTION_STATE_ACTIVE", StoreProducts.Yearly, Nu.AddDays(-1)),
            "purchase-token-abc");

        Assert.False(bon.Valid);
    }

    [Fact]
    public async Task Google_zonder_sleutels_kent_niets_toe()
    {
        var verifier = new GoogleStoreVerifier(
            new FakeVendor().Factory(GoogleStoreVerifier.BaseUrl),
            new ConfigurationBuilder().Build(),
            _klok,
            NullLogger<GoogleStoreVerifier>.Instance);

        Assert.False(verifier.Configured);
        Assert.False((await verifier.VerifyAsync("purchase-token-abc")).Valid);
    }

    // ---- Producten ---------------------------------------------------------

    [Theory]
    [InlineData("nl.wattzicht.plus.monthly", true)]
    [InlineData("nl.wattzicht.plus.yearly", true)]
    [InlineData("nl.wattzicht.plus", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Alleen_onze_eigen_producten_tellen(string? productId, bool verwacht)
    {
        Assert.Equal(verwacht, StoreProducts.IsOnze(productId));
    }

    // ---- Hulpstukken -------------------------------------------------------

    private AppleStoreVerifier Apple() => new(
        new FakeVendor().Factory(AppleStoreVerifier.ProductionBaseUrl),
        new ConfigurationBuilder().Build(),
        _klok,
        NullLogger<AppleStoreVerifier>.Instance);

    private GoogleStoreVerifier Google() => new(
        new FakeVendor().Factory(GoogleStoreVerifier.BaseUrl),
        new ConfigurationBuilder().Build(),
        _klok,
        NullLogger<GoogleStoreVerifier>.Instance);

    /// <summary>Bootst het antwoord van Apple na: de velden zitten in een JWT-payload.</summary>
    private static string AppleAntwoord(string productId, string origineel, DateTimeOffset verlooptOp)
    {
        var payload = JsonSerializer.Serialize(new
        {
            productId,
            originalTransactionId = origineel,
            expiresDate = verlooptOp.ToUnixTimeMilliseconds(),
        });

        var jwt = "kop." + Base64Url(Encoding.UTF8.GetBytes(payload)) + ".handtekening";
        return JsonSerializer.Serialize(new
        {
            data = new[] { new { lastTransactions = new[] { new { signedTransactionInfo = jwt } } } },
        });
    }

    private static string GoogleAntwoord(string state, string productId, DateTimeOffset verlooptOp) =>
        JsonSerializer.Serialize(new
        {
            subscriptionState = state,
            lineItems = new[] { new { productId, expiryTime = verlooptOp.ToString("o") } },
        });

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
