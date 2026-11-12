using System.Text.Json;
using Wattzicht.Api.Data;
using Wattzicht.Shared;

namespace Wattzicht.Api.Connectors;

/// <summary>
/// Een Peblar-laadpaal uitlezen over het thuisnetwerk.
///
/// Peblar heeft wel een cloud, maar die is er alleen voor hun eigen app; buitenstaanders komen er
/// niet in. Wat ze wel geven is een gedocumenteerde lokale API op de paal zelf, die de eigenaar in
/// de webinterface aanzet en waar hij zelf een token voor aanmaakt. Dat is precies genoeg voor ons:
/// <c>http://{ip}/api/v1/system</c> voor serienummer en model, en <c>/api/v1/meter</c> voor de stand
/// van de ingebouwde meter en het vermogen van dit moment.
///
/// Drie dingen om te weten:
/// 1. De API staat standaard UIT. De eigenaar zet hem aan in de webinterface van de paal en krijgt
///    daar een token. Zonder die twee handelingen is er niets te halen, en dat is geen storing.
/// 2. Het adres is een privéadres in het huis. Onze server komt daar nooit, dus de app leest uit,
///    net als bij Sessy en Alfen.
/// 3. Peblar praat http op het thuisnetwerk, geen https. Op een iPhone mag dat alleen met
///    NSAllowsLocalNetworking, en dat staat in Info.plist.
///
/// LET OP: deze parser is defensief geschreven. De veldnamen komen uit Peblars ontwikkelaarspagina
/// en uit de Home Assistant-integratie, niet uit een meting aan een echt apparaat van een klant.
/// Daarom lezen we elk getal onder meerdere schrijfwijzen en hoofdlettervarianten. Zodra de eerste
/// echte Peblar gekoppeld is, hoort hier een opgenomen antwoord bij te komen en mag dit strakker.
/// </summary>
public sealed class PeblarConnector(IHttpClientFactory httpClientFactory, ILogger<PeblarConnector> logger) : IDeviceConnector
{
    public const string HttpClientName = "peblar-lokaal";

    public DeviceBrand Brand => DeviceBrand.Peblar;

    /// <summary>Een paal op 192.168.x.x is voor onze server onbereikbaar; de telefoon in huis niet.</summary>
    public bool IsLokaalVoor(IReadOnlyDictionary<string, string> credentials) =>
        ThuisAdres.IsPrive(credentials.Get("host"));

    /// <summary>De app mag een Peblar altijd aanleveren.</summary>
    public bool KanDoorDeAppGelezenWorden => true;

    public DeviceReading LeesLokaal(JsonElement rauw, DateTimeOffset gemeten) =>
        Parse(rauw) with { Timestamp = gemeten };

    public async Task<ConnectResult> ConnectAsync(ConnectRequest request, CancellationToken ct)
    {
        var host = request.Credentials.Get("host");
        var token = request.Credentials.Get("token");

        if (host is null || token is null)
        {
            return ConnectResult.Fail(
                "Vul het IP-adres van je Peblar en een API-token in. Dat token maak je zelf aan in de webinterface "
                + "van de paal, onder de ontwikkelaarsinstellingen; daar zet je de lokale API ook aan.");
        }

        try
        {
            using var systeem = await HaalAsync(host, token, "system", ct);
            var root = systeem.RootElement;

            var serie = Tekst(root, "ProductSerialNumber", "SerialNumber", "serial_number", "serial")
                        ?? host;
            var model = Tekst(root, "ProductModelName", "ProductPartNumber", "model", "product_part_number");

            // Een paal zonder ingebouwde meter levert geen stand. Dat hoort hier te stranden en niet
            // pas over een maand, als iemand zich afvraagt waarom zijn grafiek leeg blijft.
            using var meter = await HaalAsync(host, token, "meter", ct);
            if (Meterstand(meter.RootElement) is null)
            {
                return ConnectResult.Fail(
                    "De paal antwoordt wel, maar geeft geen meterstand terug. Dat gebeurt bij een uitvoering zonder "
                    + "ingebouwde meter. Laat ons weten welk type het is, dan kijken we mee.");
            }

            var naam = model is null ? "Peblar laadpaal" : $"Peblar {model}";
            return ConnectResult.Ok([new DiscoveredDevice(serie, naam, DeviceType.Charger)]);
        }
        catch (ConnectorException ex)
        {
            return ConnectResult.Fail(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Peblar op {Host} niet bereikbaar bij koppelen", host);
            return ConnectResult.Fail(
                $"Op {host} is geen Peblar te bereiken. Staat de lokale API aan in de webinterface van de paal, en "
                + "hangt hij aan hetzelfde netwerk als je telefoon?");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return ConnectResult.Fail($"De Peblar op {host} reageert niet (time-out).");
        }
        catch (JsonException)
        {
            return ConnectResult.Fail($"Op {host} antwoordt iets anders dan een Peblar. Controleer het adres.");
        }
    }

    public async Task<DeviceReading> ReadAsync(Device device, DeviceCredentials credentials, CancellationToken ct)
    {
        var host = credentials.Get("host") ?? throw new ConnectorException("Adres van de Peblar ontbreekt.");
        var token = credentials.Get("token") ?? throw new ConnectorException("API-token van de Peblar ontbreekt.");

        try
        {
            using var meter = await HaalAsync(host, token, "meter", ct);
            return Parse(meter.RootElement);
        }
        catch (HttpRequestException ex)
        {
            throw new ConnectorException($"Peblar op {host} niet bereikbaar.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ConnectorException($"Peblar op {host} reageert niet (time-out).", ex);
        }
        catch (JsonException ex)
        {
            throw new ConnectorException("Onverwacht antwoord van de Peblar.", ex);
        }
    }

    /// <summary>
    /// Zet het antwoord van <c>/api/v1/meter</c> om naar onze meting.
    ///
    /// Peblar geeft de totaalstand in Wh en het vermogen in watt. Wij bewaren tellers in Wh, dus de
    /// stand gaat er ongewijzigd in. Is de stand nul, dan laten we de teller leeg: nul is hier geen
    /// meting maar een paal die nog niets geleverd heeft, en een teller die van nul naar duizend
    /// springt leest later als duizend verbruikte Wh die er nooit waren.
    /// </summary>
    internal static DeviceReading Parse(JsonElement root)
    {
        var wh = Meterstand(root);
        var vermogen = Getal(root, "PowerTotal", "power_total", "powerTotal", "Power", "power") ?? 0;

        return new DeviceReading(
            DateTimeOffset.UtcNow,
            ChargerPowerW: vermogen,
            ChargerEnergyWh: wh > 0 ? wh : null);
    }

    /// <summary>
    /// De cumulatieve stand van de ingebouwde meter, in Wh.
    ///
    /// Peblar noemt dit EnergyTotal. Omdat de schrijfwijze per firmware kan verschillen en sommige
    /// uitvoeringen in kWh rapporteren, kijken we naar meerdere namen en rekenen we een kWh-veld om.
    /// </summary>
    internal static double? Meterstand(JsonElement root)
    {
        if (Getal(root, "EnergyTotal", "energy_total", "energyTotal", "TotalEnergy", "total_energy") is { } wh)
            return wh;

        if (Getal(root, "EnergyTotalKwh", "energy_total_kwh", "TotalEnergyKwh") is { } kwh)
            return kwh * 1000;

        return null;
    }

    /// <summary>
    /// Eén endpoint van de lokale API ophalen.
    ///
    /// Het token gaat mee als Authorization-header zonder schema, zo wil Peblar het. Een 401 is hier
    /// bijna altijd hetzelfde verhaal: het token is in de webinterface opnieuw aangemaakt en het oude
    /// staat nog bij ons. Dat zeggen we dus ook, in plaats van "niet geautoriseerd".
    /// </summary>
    private async Task<JsonDocument> HaalAsync(string host, string token, string pad, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var verzoek = new HttpRequestMessage(HttpMethod.Get, $"http://{host}/api/v1/{pad}");
        verzoek.Headers.TryAddWithoutValidation("Authorization", token);

        using var antwoord = await client.SendAsync(verzoek, ct);

        if (antwoord.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            throw new ConnectorException(
                "De Peblar weigert het token. Maak in de webinterface van de paal een nieuw API-token aan en vul dat "
                + "hier in; een token dat daar opnieuw is aangemaakt, maakt het oude ongeldig.");
        }

        if (antwoord.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new ConnectorException(
                "De paal kent dit adres niet. Dat wijst op een firmware waarin de lokale API nog niet zit; werk de "
                + "Peblar bij via zijn eigen webinterface en probeer het opnieuw.");
        }

        antwoord.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await antwoord.Content.ReadAsStringAsync(ct));
    }

    private static string? Tekst(JsonElement root, params string[] namen)
    {
        foreach (var naam in namen)
        {
            if (!root.TryGetProperty(naam, out var waarde)) continue;
            if (waarde.ValueKind == JsonValueKind.String && waarde.GetString() is { Length: > 0 } s) return s;
            if (waarde.ValueKind == JsonValueKind.Number) return waarde.GetRawText();
        }
        return null;
    }

    private static double? Getal(JsonElement root, params string[] namen)
    {
        foreach (var naam in namen)
        {
            if (!root.TryGetProperty(naam, out var waarde)) continue;
            switch (waarde.ValueKind)
            {
                case JsonValueKind.Number:
                    return waarde.GetDouble();
                case JsonValueKind.String when double.TryParse(waarde.GetString(),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var d):
                    return d;
            }
        }
        return null;
    }
}
