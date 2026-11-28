using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Wattzicht.Api.Services;

/// <summary>De SMTP-gegevens waarmee Wattzicht post verstuurt.</summary>
/// <param name="Host">De mailserver. Leeg betekent: niet versturen, alleen loggen.</param>
/// <param name="Port">587 voor STARTTLS, 465 voor een verbinding die meteen versleuteld is.</param>
/// <param name="Gebruiker">Het postvak waarmee we inloggen.</param>
/// <param name="Wachtwoord">Het wachtwoord van dat postvak.</param>
/// <param name="Afzender">Het adres dat de ontvanger ziet.</param>
/// <param name="AfzenderNaam">De naam voor dat adres.</param>
/// <param name="StartTls">Of de verbinding met STARTTLS versleuteld wordt.</param>
public sealed record MailInstellingen(
    string? Host,
    int Port,
    string? Gebruiker,
    string? Wachtwoord,
    string Afzender,
    string AfzenderNaam,
    bool StartTls)
{
    /// <summary>
    /// De instellingen uit de omgeving. Beide schrijfwijzen werken: <c>Mail:Host</c> in een
    /// appsettings-bestand en <c>MAIL_HOST</c> als losse omgevingsvariabele, want dat laatste is
    /// wat je in Dokploy invult.
    /// </summary>
    public static MailInstellingen Lees(IConfiguration config)
    {
        string? Waarde(string sleutel, string omgeving)
        {
            var v = config[sleutel];
            return string.IsNullOrWhiteSpace(v) ? config[omgeving] : v;
        }

        var poort = int.TryParse(Waarde("Mail:Port", "MAIL_PORT"), out var p) ? p : 587;
        var startTls = !bool.TryParse(Waarde("Mail:StartTls", "MAIL_STARTTLS"), out var s) || s;

        return new MailInstellingen(
            Waarde("Mail:Host", "MAIL_HOST"),
            poort,
            Waarde("Mail:User", "MAIL_USER"),
            Waarde("Mail:Password", "MAIL_PASSWORD"),
            Waarde("Mail:From", "MAIL_FROM") ?? "noreply@wattzicht.nl",
            Waarde("Mail:FromName", "MAIL_FROM_NAME") ?? "Wattzicht",
            startTls);
    }
}

/// <summary>Post versturen.</summary>
public interface IMailVerzender
{
    /// <summary>
    /// Verstuurt één bericht. Geeft null bij succes en anders de fout, zodat de aanroeper zelf kan
    /// bepalen of die fout de gebruiker moet bereiken.
    /// </summary>
    Task<string?> VerstuurAsync(string naar, string onderwerp, string tekst, CancellationToken ct = default);

    /// <summary>Of er een mailserver is ingesteld. Zo niet, dan wordt er alleen gelogd.</summary>
    bool Ingesteld { get; }
}

/// <summary>
/// Post via SMTP.
///
/// Platte tekst, geen opmaak. Een bevestigingsmail bestaat uit twee zinnen en een link; daar is
/// niets aan te vormgeven, en een kale tekstmail komt bij meer mensen aan dan een mail met een
/// plaatje en drie stijlbladen erin.
///
/// Staat er geen mailserver ingesteld, dan wordt de mail alleen gelogd. Dat is de stand op een
/// ontwikkelmachine en in de tests: aanmelden mag daar niet stuklopen op een ontbrekend postvak.
/// </summary>
public sealed class SmtpMailVerzender(IConfiguration config, ILogger<SmtpMailVerzender> logger) : IMailVerzender
{
    private readonly MailInstellingen _instellingen = MailInstellingen.Lees(config);

    public bool Ingesteld => !string.IsNullOrWhiteSpace(_instellingen.Host);

    public async Task<string?> VerstuurAsync(string naar, string onderwerp, string tekst, CancellationToken ct = default)
    {
        if (!Ingesteld)
        {
            logger.LogInformation("[MAIL] Aan: {Naar} | Onderwerp: {Onderwerp}\n{Tekst}", naar, onderwerp, tekst);
            return "Er is geen mailserver ingesteld (MAIL_HOST); de mail is alleen gelogd.";
        }

        MimeMessage bericht;
        try
        {
            bericht = new MimeMessage();
            bericht.From.Add(new MailboxAddress(_instellingen.AfzenderNaam, _instellingen.Afzender));
            bericht.To.Add(MailboxAddress.Parse(naar));
            bericht.Subject = onderwerp;
            bericht.Body = new TextPart("plain") { Text = tekst };
        }
        catch (ParseException)
        {
            // Een adres dat MailKit niet kan lezen is geen storing maar een verkeerd ingevuld veld.
            logger.LogWarning("Geen geldig e-mailadres: {Naar}", naar);
            return $"'{naar}' is geen geldig e-mailadres.";
        }

        try
        {
            using var client = new SmtpClient { Timeout = 20_000 };
            var beveiliging = _instellingen.Port == 465
                ? SecureSocketOptions.SslOnConnect
                : _instellingen.StartTls
                    ? SecureSocketOptions.StartTls
                    : SecureSocketOptions.Auto;

            await client.ConnectAsync(_instellingen.Host!, _instellingen.Port, beveiliging, ct);
            if (!string.IsNullOrEmpty(_instellingen.Gebruiker))
            {
                await client.AuthenticateAsync(_instellingen.Gebruiker, _instellingen.Wachtwoord ?? string.Empty, ct);
            }

            await client.SendAsync(bericht, ct);
            await client.DisconnectAsync(true, ct);
            logger.LogInformation("Mail verstuurd aan {Naar} ({Onderwerp})", naar, onderwerp);
            return null;
        }
        catch (Exception ex)
        {
            // Nooit de aanroeper laten omvallen: wie zich aanmeldt heeft een account, ook als de
            // bevestigingsmail niet weg kan. Hij kan hem opnieuw laten sturen.
            logger.LogError(ex, "Mail aan {Naar} mislukt ({Onderwerp})", naar, onderwerp);
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }
}
