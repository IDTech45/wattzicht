using System.Net;

namespace Wattzicht.App.Services;

/// <summary>
/// Fout vanuit de Wattzicht-API, met een nette melding voor de gebruiker.
/// </summary>
public sealed class ApiException : Exception
{
    public HttpStatusCode? StatusCode { get; }

    public bool IsUnauthorized => StatusCode == HttpStatusCode.Unauthorized;
    public bool IsNetworkError => StatusCode is null;

    public ApiException(string message, HttpStatusCode? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public static ApiException Network(Exception inner) =>
        new("Geen verbinding met Wattzicht. Controleer je internet en probeer het opnieuw.", null, inner);

    public static ApiException FromStatus(HttpStatusCode status, string? serverMessage = null)
    {
        var message = status switch
        {
            HttpStatusCode.Unauthorized => "Je sessie is verlopen. Log opnieuw in.",
            HttpStatusCode.Forbidden => "Je hebt hier geen toegang toe.",
            HttpStatusCode.NotFound => "Niet gevonden.",
            HttpStatusCode.BadRequest => string.IsNullOrWhiteSpace(serverMessage) ? "De ingevoerde gegevens kloppen niet." : serverMessage,
            HttpStatusCode.Conflict => string.IsNullOrWhiteSpace(serverMessage) ? "Dit bestaat al." : serverMessage,
            HttpStatusCode.TooManyRequests => "Te veel verzoeken. Wacht even en probeer het opnieuw.",
            _ when (int)status >= 500 => "Er ging iets mis bij Wattzicht. Probeer het later opnieuw.",
            _ => string.IsNullOrWhiteSpace(serverMessage) ? $"Onbekende fout ({(int)status})." : serverMessage
        };
        return new ApiException(message, status);
    }
}
