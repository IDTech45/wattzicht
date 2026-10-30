using System.Net;
using System.Text;

namespace Wattzicht.Api.Tests.Connectors;

/// <summary>
/// Nep-fabrikant voor koppelingstests. Antwoorden per pad (deel van de URL), legt elk verzoek vast
/// zodat een test kan controleren welke URL, methode en headers de connector echt stuurt.
/// Er gaat nooit iets naar het internet.
/// </summary>
public sealed class FakeVendor : HttpMessageHandler
{
    private readonly List<(string PathContains, HttpMethod? Method, Func<HttpRequestMessage, Task<HttpResponseMessage>> Respond)> _routes = [];

    /// <summary>Alle verzoeken die de connector deed, in volgorde.</summary>
    public List<RecordedRequest> Requests { get; } = [];

    public FakeVendor Json(string pathContains, string json, HttpStatusCode status = HttpStatusCode.OK, HttpMethod? method = null)
    {
        return Route(pathContains, method, _ => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));
    }

    public FakeVendor Status(string pathContains, HttpStatusCode status, HttpMethod? method = null) =>
        Json(pathContains, "", status, method);

    /// <summary>Gedraagt zich als een time-out van HttpClient (TaskCanceledException zonder geannuleerd token).</summary>
    public FakeVendor Timeout(string pathContains)
    {
        return Route(pathContains, null, _ => Task.FromException<HttpResponseMessage>(new TaskCanceledException("time-out")));
    }

    /// <summary>Host onbereikbaar (DNS, verbinding geweigerd).</summary>
    public FakeVendor Unreachable(string pathContains)
    {
        return Route(pathContains, null, _ => Task.FromException<HttpResponseMessage>(new HttpRequestException("verbinding geweigerd")));
    }

    private FakeVendor Route(string pathContains, HttpMethod? method, Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
    {
        _routes.Add((pathContains, method, respond));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase),
            body));

        var url = request.RequestUri!.ToString();
        foreach (var route in _routes)
        {
            if (url.Contains(route.PathContains, StringComparison.OrdinalIgnoreCase)
                && (route.Method is null || route.Method == request.Method))
            {
                return await route.Respond(request);
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("geen route in test: " + url) };
    }

    /// <summary>Factory die steeds een nieuwe client op deze nep-fabrikant geeft, zoals de echte factory doet.</summary>
    public IHttpClientFactory Factory(string? baseUrl = null) => new Fac(this, baseUrl);

    private sealed class Fac(FakeVendor handler, string? baseUrl) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient(handler, disposeHandler: false);
            if (baseUrl is not null)
            {
                client.BaseAddress = new Uri(baseUrl);
            }

            return client;
        }
    }
}

public sealed record RecordedRequest(HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, string? Body);
