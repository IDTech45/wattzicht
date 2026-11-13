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
        new(Taal.T("fout.geen-verbinding"), null, inner);

    public static ApiException FromStatus(HttpStatusCode status, string? serverMessage = null)
    {
        var message = status switch
        {
            HttpStatusCode.Unauthorized => Taal.T("fout.sessie-verlopen"),
            HttpStatusCode.Forbidden => Taal.T("fout.geen-toegang"),
            HttpStatusCode.NotFound => Taal.T("fout.niet-gevonden"),
            HttpStatusCode.BadRequest => string.IsNullOrWhiteSpace(serverMessage) ? Taal.T("fout.gegevens-kloppen-niet") : serverMessage,
            HttpStatusCode.Conflict => string.IsNullOrWhiteSpace(serverMessage) ? Taal.T("fout.bestaat-al") : serverMessage,
            HttpStatusCode.TooManyRequests => Taal.T("fout.te-veel-verzoeken"),
            _ when (int)status >= 500 => Taal.T("fout.serverfout"),
            _ => string.IsNullOrWhiteSpace(serverMessage)
                ? string.Format(Taal.T("fout.onbekend"), (int)status)
                : serverMessage
        };
        return new ApiException(message, status);
    }
}
