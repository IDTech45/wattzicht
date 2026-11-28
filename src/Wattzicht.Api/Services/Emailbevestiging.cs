using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Shared.Taal;

namespace Wattzicht.Api.Services;

/// <summary>
/// De bevestiging van een e-mailadres: sleutel maken, mail sturen, link nakijken.
///
/// Eén plek voor alle drie, want ze horen bij elkaar: de regel hoe lang een sleutel geldig is en de
/// regel hoe vaak je een mail opnieuw mag laten sturen staan naast elkaar en kunnen niet uit elkaar
/// lopen.
///
/// De sleutel staat alleen in de mail. In de database ligt zijn SHA-256, en de link wordt nagekeken
/// door de meegestuurde sleutel te hashen en te vergelijken. Daarmee is een gestolen database geen
/// stapel werkende bevestigingslinks.
/// </summary>
public sealed class Emailbevestiging(
    AppDbContext db,
    IMailVerzender mail,
    TimeProvider clock,
    ILogger<Emailbevestiging> logger)
{
    /// <summary>Hoe lang een sleutel uit de mail werkt.</summary>
    public static readonly TimeSpan Geldigheid = TimeSpan.FromDays(7);

    /// <summary>
    /// Hoe lang er tussen twee bevestigingsmails moet zitten.
    ///
    /// Zonder deze pauze is "stuur opnieuw" een knop waarmee iemand anders zijn postvak kan laten
    /// vollopen: het adres is van hem, de knop is van ons.
    /// </summary>
    public static readonly TimeSpan PauzeTussenMails = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Hoe lang een niet-bevestigd account blijft staan voordat het wordt opgeruimd.
    ///
    /// Gelijk aan de geldigheid van de sleutel: zolang de link uit de mail nog werkt hoort het
    /// account er te zijn. Wie zijn mail een week niet leest kan zich gewoon opnieuw aanmelden.
    /// </summary>
    public static readonly TimeSpan BewaartermijnOnbevestigd = Geldigheid;

    /// <summary>De SHA-256 van een sleutel, in kleine letters hex.</summary>
    public static string Hash(string sleutel) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sleutel))).ToLowerInvariant();

    /// <summary>
    /// Maakt een nieuwe sleutel voor deze gebruiker en stuurt de bevestigingsmail.
    ///
    /// Geeft terug of de mail de deur uit is. Een mislukte verzending is bewust geen fout voor de
    /// aanroeper: aanmelden mag niet stuklopen omdat een mailserver even niet bereikbaar is, en de
    /// gebruiker kan hem later opnieuw laten sturen.
    /// </summary>
    public async Task<bool> VerstuurAsync(User gebruiker, string basisUrl, string taal, CancellationToken ct = default)
    {
        var nu = clock.GetUtcNow();

        // 32 bytes willekeur, als url-veilige tekst. Geen Guid: die is korter en deels voorspelbaar,
        // en dit is de enige drempel tussen een vreemde en een bevestigd adres.
        var sleutel = Base64Url(RandomNumberGenerator.GetBytes(32));

        gebruiker.VerifyTokenHash = Hash(sleutel);
        gebruiker.VerifyTokenExpiresAt = nu + Geldigheid;
        gebruiker.VerifyMailSentAt = nu;
        await db.SaveChangesAsync(ct);

        var link = $"{basisUrl.TrimEnd('/')}/auth/verify?token={sleutel}";
        var fout = await mail.VerstuurAsync(
            gebruiker.Email,
            Teksten.T(taal, "mail.bevestig.onderwerp"),
            string.Format(Teksten.Cultuur(taal), Teksten.T(taal, "mail.bevestig.tekst"), link),
            ct);

        if (fout is not null)
        {
            logger.LogWarning("Bevestigingsmail aan {Gebruiker} niet verstuurd: {Fout}", gebruiker.Id, fout);
            return false;
        }

        return true;
    }

    /// <summary>Of er nu opnieuw een mail gestuurd mag worden, of dat de pauze nog loopt.</summary>
    public bool MagOpnieuw(User gebruiker) =>
        gebruiker.VerifyMailSentAt is null || clock.GetUtcNow() - gebruiker.VerifyMailSentAt.Value >= PauzeTussenMails;

    /// <summary>
    /// Keurt een sleutel uit een bevestigingslink.
    ///
    /// Een al bevestigd adres geeft ook "gelukt". Mensen klikken zo'n link twee keer, en een
    /// foutmelding op de tweede klik laat iemand denken dat het de eerste keer misging.
    /// </summary>
    public async Task<bool> BevestigAsync(string? sleutel, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sleutel)) return false;

        var hash = Hash(sleutel);
        var gebruiker = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.VerifyTokenHash == hash, ct);
        if (gebruiker is null) return false;

        if (gebruiker.EmailVerifiedAt is not null) return true;

        var nu = clock.GetUtcNow();
        if (gebruiker.VerifyTokenExpiresAt is null || gebruiker.VerifyTokenExpiresAt < nu) return false;

        gebruiker.EmailVerifiedAt = nu;

        // De sleutel is op. Blijft hij staan, dan werkt dezelfde link over een half jaar nog, en
        // dat is precies het soort link dat in een doorgestuurde mail blijft hangen.
        gebruiker.VerifyTokenHash = null;
        gebruiker.VerifyTokenExpiresAt = null;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Base64 zonder opvulling en zonder tekens die in een url iets anders betekenen.</summary>
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
