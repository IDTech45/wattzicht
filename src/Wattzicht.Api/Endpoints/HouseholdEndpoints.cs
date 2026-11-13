using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Connectors;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Endpoints;

/// <summary>Endpoints voor de bewoner: profiel, dashboard, apparaten, samenvattingen, meldingen en tarief.</summary>
public static class HouseholdEndpoints
{
    public static IEndpointRouteBuilder MapHouseholdEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/me", MeAsync)
            .RequireAuthorization()
            .WithTags("Me")
            .WithSummary("Ingelogde gebruiker met huishouden en white-label instellingen.")
            .Produces<MeDto>();

        var household = app.MapGroup(string.Empty)
            .RequireAuthorization(AuthPolicies.Resident)
            .WithTags("Huishouden");

        household.MapGet("/household", HouseholdAsync)
            .WithSummary("Het eigen huishouden.")
            .Produces<HouseholdDto>();

        household.MapPut("/household", WoningBijwerkenAsync)
            .WithSummary("Werkt naam, plaats en piekvermogen bij; zoekt bij een nieuwe plaats de coordinaten op.")
            .Produces<HouseholdDto>()
            .ProducesValidationProblem();

        household.MapGet("/household/installer-access", GetInstallerAccessAsync)
            .WithSummary("Mag de installateur meekijken? (toestemming van de bewoner)")
            .Produces<InstallerAccessDto>();

        household.MapPut("/household/installer-access", SetInstallerAccessAsync)
            .WithSummary("Zet meekijken door de installateur aan of uit. Uit = direct onzichtbaar in het portal.")
            .Produces<InstallerAccessDto>();

        household.MapDelete("/household", VerwijderAsync)
            .WithSummary("Verwijdert dit huishouden en alles wat eraan hangt. Onomkeerbaar.")
            .Produces(StatusCodes.Status204NoContent);

        household.MapGet("/household/access-log", AccessLogAsync)
            .WithSummary("Wanneer de installateur je gegevens bekeek (laatste 50).")
            .Produces<List<AccessLogEntryDto>>();

        household.MapGet("/installer", InstallerAsync)
            .WithSummary("White-label gegevens van de eigen installateur (naam, logo, kleuren, offertelink, contact).")
            .Produces<InstallerBrandingDto>();

        household.MapGet("/dashboard", DashboardAsync)
            .WithSummary("Alles voor het startscherm: live, vandaag, maand, jaar en meldingen.")
            .Produces<DashboardDto>();

        household.MapGet("/dashboard/live", LiveAsync)
            .WithSummary("Alleen de actuele energiestromen (voor snelle verversing).")
            .Produces<LiveSnapshotDto>();

        household.MapGet("/devices", DevicesAsync)
            .WithSummary("Gekoppelde apparaten.")
            .Produces<List<DeviceDto>>();

        household.MapGet("/connectors/enphase/authorize-url", EnphaseAuthorizeUrlAsync)
            .WithSummary("De Enphase-toestemmingspagina waar de bewoner naartoe moet (client-id blijft op de server).")
            .Produces<EnphaseAuthorizeUrlDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        household.MapPost("/devices/connect", ConnectDeviceAsync)
            .WithSummary("Koppelt een apparaat: valideert de credentials bij het merk en slaat ze versleuteld op.")
            .Produces<List<DeviceDto>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        household.MapPost("/devices", ConnectDeviceAsync)
            .WithSummary("Alias van POST /devices/connect.")
            .Produces<List<DeviceDto>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        household.MapPost("/devices/{id:guid}/readings", LocalReadingAsync)
            .WithSummary("Een meting die de app zelf op het thuisnetwerk ophaalde (P1-meter).")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        household.MapPut("/devices/{id:guid}/naam", ApparaatHernoemenAsync)
            .WithSummary("Geeft een apparaat een eigen naam.")
            .Produces<DeviceDto>()
            .ProducesValidationProblem();

        household.MapPut("/devices/{id:guid}/sturen", SturenToestaanAsync)
            .WithSummary("Zet het starten en pauzeren van deze laadpaal aan of uit.")
            .Produces<DeviceDto>()
            .ProducesValidationProblem();

        household.MapPost("/devices/{id:guid}/charge", LaadopdrachtAsync)
            .WithSummary("Start of pauzeert het laden van deze paal.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        household.MapPost("/devices/refresh", NuUitlezenAsync)
            .WithSummary("Leest de apparaten van deze woning nu uit (alleen de merken die de server kan bereiken).")
            .Produces<UitleesResultaatDto>();

        household.MapDelete("/devices/{id:guid}", DeleteDeviceAsync)
            .WithSummary("Ontkoppelt een apparaat; metingen en meldingen gaan mee weg.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        household.MapGet("/summaries", SummariesAsync)
            .WithSummary("Dagsamenvattingen tussen twee datums (standaard: afgelopen 30 dagen).")
            .Produces<List<DailySummaryDto>>();

        household.MapGet("/dashboard/summaries", SummariesAsync)
            .WithSummary("Alias van GET /summaries.")
            .Produces<List<DailySummaryDto>>();

        household.MapGet("/alerts", AlertsAsync)
            .WithSummary("Meldingen; standaard alleen open meldingen, met ?all=true ook opgeloste.")
            .Produces<List<AlertDto>>();

        household.MapGet("/dashboard/alerts", AlertsAsync)
            .WithSummary("Alias van GET /alerts.")
            .Produces<List<AlertDto>>();

        household.MapGet("/tariff", GetTariffAsync)
            .WithSummary("Huidig tarief.")
            .Produces<TariffDto>();

        household.MapPost("/tariff", SetTariffAsync)
            .WithSummary("Zet een (nieuw) tarief vanaf een datum (standaard vandaag) en herberekent de besparing vanaf die dag.")
            .Produces<TariffDto>()
            .ProducesValidationProblem();

        household.MapPut("/tariff", SetTariffAsync)
            .WithSummary("Alias van POST /tariff.")
            .Produces<TariffDto>()
            .ProducesValidationProblem();

        household.MapPost("/tariff/import", TariefLezenAsync)
            .WithSummary("Leest de tarieven uit een energiecontract of jaarafrekening; slaat niets op.")
            .Produces<TariefVoorstelDto>()
            .ProducesValidationProblem()
            .DisableAntiforgery();

        household.MapGet("/export", ExportAsync)
            .WithSummary("Alle dagcijfers van deze woning als CSV.")
            .Produces<string>(StatusCodes.Status200OK, "text/csv");

        household.MapGet("/battery-advice", BatterijAdviesAsync)
            .WithSummary("Wat een thuisbatterij bij deze woning zou opleveren, gerekend met de eigen gemeten dagen.")
            .Produces<BatterijAdviesDto>();

        household.MapGet("/usage", VerbruikVanLeverancierAsync)
            .WithSummary("De maandcijfers die uit overzichten van de leverancier zijn ingelezen.")
            .Produces<List<LeverancierMaandDto>>();

        household.MapPost("/usage/import", VerbruikLezenAsync)
            .WithSummary("Leest de kilowatturen per maand uit een overzicht van de leverancier.")
            .Produces<VerbruikImportDto>()
            .ProducesValidationProblem()
            .DisableAntiforgery();

        household.MapPost("/history/import", HistorieImporterenAsync)
            .WithSummary("Leest een CSV-export uit het portaal van de fabrikant in als dagtotalen opwek.")
            .Produces<HistorieImportDto>()
            .ProducesValidationProblem()
            .DisableAntiforgery();

        return app;
    }

    /// <summary>
    /// De tarieven uit een energiecontract of jaarafrekening halen.
    ///
    /// Om te laten zien wat iemand bespaart moeten we weten wat hij per kilowattuur betaalt en
    /// krijgt. Dat stond in vier invulvelden, over te tikken uit een pdf van zijn leverancier, en
    /// precies daar haakt iemand af. Zijn contract heeft die getallen al.
    ///
    /// Dit slaat niets op. Het antwoord is een voorstel met per bedrag de regel waar het vandaan
    /// komt; de bewoner ziet wat wij gelezen hebben en neemt het zelf over. Elke leverancier
    /// schrijft zijn afrekening anders, en een bedrag dat er ongemerkt naast zit maakt elke
    /// besparing op elk scherm verkeerd.
    /// </summary>
    private static async Task<IResult> TariefLezenAsync(HttpContext http, CancellationToken ct)
    {
        if (!http.Request.HasFormContentType)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["bestand"] = ["Stuur het bestand mee als formulierveld 'bestand'."],
            });
        }

        var form = await http.Request.ReadFormAsync(ct);
        var bestand = form.Files["bestand"] ?? form.Files.FirstOrDefault();

        if (bestand is null || bestand.Length == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["bestand"] = ["Er zat geen bestand bij."] });
        }

        if (bestand.Length > MaximumBestand)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["bestand"] = [$"Dit bestand is groter dan {MaximumBestand / (1024 * 1024)} MB."],
            });
        }

        await using var stroom = bestand.OpenReadStream();
        var gelezen = DocumentTekst.Lees(stroom, bestand.FileName, bestand.ContentType);

        if (gelezen.Tekst is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["bestand"] = [gelezen.Fout ?? "Dit bestand konden we niet lezen."],
            });
        }

        var voorstel = TariefLezer.Lees(gelezen.Tekst);
        if (!voorstel.HeeftIets)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["bestand"] =
                [
                    "In dit document staan geen tarieven per kilowattuur die wij herkennen. "
                    + "Een contract of jaarafrekening van je leverancier werkt meestal wel.",
                ],
            });
        }

        static TariefVondstDto? Naar(TariefLezer.Vondst? v) =>
            v is null ? null : new TariefVondstDto(v.EuroPerKwh, v.Regel);

        return Results.Ok(new TariefVoorstelDto(
            Naar(voorstel.Levering),
            Naar(voorstel.Teruglevering),
            Naar(voorstel.Terugleverkosten),
            Naar(voorstel.Energiebelasting),
            voorstel.PrijsIsAllIn));
    }

    /// <summary>
    /// Alle dagcijfers als CSV.
    ///
    /// Twee redenen. Het is jouw meting, dus je hoort hem mee te kunnen nemen; dat staat ook zo in
    /// de privacywet. En wie in een spreadsheet wil rekenen hoeft dan niet te vragen of wij iets
    /// kunnen bouwen voor die ene som.
    ///
    /// Puntkomma als scheidingsteken en een komma als decimaalteken: zo opent het in een Nederlandse
    /// Excel zonder dat alles in één kolom staat.
    /// </summary>
    private static async Task<IResult> ExportAsync(
        HttpContext http, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var nl = System.Globalization.CultureInfo.GetCultureInfo("nl-NL");

        var dagen = await db.DailySummaries
            .Where(s => s.HouseholdId == householdId)
            .OrderBy(s => s.Date)
            .Select(s => new
            {
                s.Date, s.PvKwh, s.SelfUseKwh, s.ExportKwh, s.ImportKwh, s.ChargedKwh,
                s.BatteryChargedKwh, s.BatteryDischargedKwh, s.SavedEur, s.IrradianceKwhM2, s.ImportedAt,
            })
            .ToListAsync(ct);

        var bouwer = new System.Text.StringBuilder();
        bouwer.AppendLine(
            "datum;opwek kWh;zelf gebruikt kWh;teruggeleverd kWh;van het net kWh;geladen kWh;"
            + "batterij in kWh;batterij uit kWh;bespaard EUR;instraling kWh/m2;bron");

        foreach (var d in dagen)
        {
            bouwer.AppendLine(string.Join(';',
                d.Date.ToString("yyyy-MM-dd"),
                d.PvKwh.ToString("0.###", nl),
                d.SelfUseKwh.ToString("0.###", nl),
                d.ExportKwh.ToString("0.###", nl),
                d.ImportKwh.ToString("0.###", nl),
                d.ChargedKwh.ToString("0.###", nl),
                d.BatteryChargedKwh.ToString("0.###", nl),
                d.BatteryDischargedKwh.ToString("0.###", nl),
                d.SavedEur.ToString("0.00", nl),
                d.IrradianceKwhM2?.ToString("0.##", nl) ?? string.Empty,
                // Zichtbaar houden welke dagen uit een export van de fabrikant komen en welke wij
                // zelf gemeten hebben; dat verschil mag in een spreadsheet niet verdwijnen.
                d.ImportedAt is null ? "gemeten" : "ingelezen"));
        }

        var naam = $"wattzicht-{DutchTime.LocalDate(clock.GetUtcNow()):yyyy-MM-dd}.csv";
        return Results.File(System.Text.Encoding.UTF8.GetBytes(bouwer.ToString()), "text/csv", naam);
    }

    /// <summary>
    /// Wat een thuisbatterij bij deze woning zou opleveren.
    ///
    /// Met de eigen gemeten dagen en niet met een rekenvoorbeeld. Elke verkoper rekent met een
    /// gemiddeld huishouden en dat bestaat niet; deze woning heeft zelf al laten zien hoeveel er
    /// overdag teruggaat en hoeveel er 's avonds weer bij moet.
    /// </summary>
    private static async Task<IResult> BatterijAdviesAsync(
        HttpContext http,
        AppDbContext db,
        SummaryService summaries,
        TimeProvider clock,
        CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var vandaag = DutchTime.LocalDate(clock.GetUtcNow());

        var heeftBatterij = await db.Devices
            .AnyAsync(d => d.HouseholdId == householdId && d.Type == DeviceType.Battery, ct);

        // Negentig dagen: lang genoeg om een seizoen te pakken, kort genoeg om over deze woning te
        // gaan zoals hij nu is.
        var vanaf = vandaag.AddDays(-90);
        var dagen = await db.DailySummaries
            .Where(s => s.HouseholdId == householdId && s.Date >= vanaf && s.Date < vandaag)
            .Select(s => new { s.ExportKwh, s.ImportKwh })
            .ToListAsync(ct);

        var invoer = dagen.Select(d => new BatterijAdvies.Dag(d.ExportKwh, d.ImportKwh)).ToList();
        var rates = await summaries.GetRatesAsync(householdId, vandaag, ct);
        var maten = BatterijAdvies.Bereken(invoer, rates, vandaag);

        return Results.Ok(new BatterijAdviesDto(
            maten.Select(m => new BatterijMaatDto(m.Kwh, m.PerJaarEur, m.PerJaarNaSalderingEur, m.VerplaatstPerDagKwh)).ToList(),
            invoer.Count(d => d.ExportKwh > 0 || d.ImportKwh > 0),
            heeftBatterij));
    }

    /// <summary>
    /// De ingelezen maandcijfers, met wat wij die maanden zelf maten ernaast.
    ///
    /// Wat je uploadt moet je daarna ook terug kunnen zien; anders is het een melding die
    /// wegscrollt en weet niemand meer of het er nog in staat.
    /// </summary>
    private static async Task<IResult> VerbruikVanLeverancierAsync(
        HttpContext http, AppDbContext db, CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;

        var rijen = await db.SupplierUsages
            .Where(x => x.HouseholdId == householdId)
            .OrderBy(x => x.Month)
            .Select(x => new { x.Month, x.ImportKwh, x.ExportKwh })
            .ToListAsync(ct);

        if (rijen.Count == 0)
        {
            return Results.Ok(new List<LeverancierMaandDto>());
        }

        var vanaf = rijen[0].Month;
        var tot = rijen[^1].Month.AddMonths(1);

        var eigen = await db.DailySummaries
            .Where(x => x.HouseholdId == householdId && x.Date >= vanaf && x.Date < tot)
            .Select(x => new { x.Date, x.ImportKwh })
            .ToListAsync(ct);

        var eigenPerMaand = eigen
            .GroupBy(x => new DateOnly(x.Date.Year, x.Date.Month, 1))
            .ToDictionary(g => g.Key, g => Math.Round(g.Sum(x => x.ImportKwh), 1));

        return Results.Ok(rijen
            .Select(r => new LeverancierMaandDto(
                r.Month,
                Math.Round(r.ImportKwh, 1),
                r.ExportKwh is { } e ? Math.Round(e, 1) : null,
                eigenPerMaand.TryGetValue(r.Month, out var onze) && onze > 0 ? onze : null))
            .ToList());
    }

    /// <summary>
    /// Het maandoverzicht van de leverancier inlezen.
    ///
    /// Bijna iedereen krijgt die mail, en voor wie geen P1-meter heeft is dat het enige wat we over
    /// de netkant kunnen weten. Het komt naast onze eigen metingen te staan en vervangt ze niet: een
    /// maandtotaal uitsmeren over de dagen zou een precisie tonen die er niet is.
    ///
    /// Het antwoord zet er meteen bij wat wij die maand zelf maten. Lopen die twee uit elkaar, dan
    /// is dat precies het moment waarop iemand wil weten waarom.
    /// </summary>
    private static async Task<IResult> VerbruikLezenAsync(
        HttpContext http,
        AppDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;

        if (!http.Request.HasFormContentType)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["bestand"] = ["Stuur het bestand mee als formulierveld 'bestand'."],
            });
        }

        var form = await http.Request.ReadFormAsync(ct);
        var bestand = form.Files["bestand"] ?? form.Files.FirstOrDefault();

        if (bestand is null || bestand.Length == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["bestand"] = ["Er zat geen bestand bij."] });
        }

        if (bestand.Length > MaximumBestand)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["bestand"] = [$"Dit bestand is groter dan {MaximumBestand / (1024 * 1024)} MB."],
            });
        }

        string? tekst;
        await using (var stroom = bestand.OpenReadStream())
        {
            var gelezen = DocumentTekst.Lees(stroom, bestand.FileName, bestand.ContentType);
            if (gelezen.Tekst is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["bestand"] = [gelezen.Fout ?? "Dit bestand konden we niet lezen."],
                });
            }

            tekst = gelezen.Tekst;
        }

        var vandaag = DutchTime.LocalDate(clock.GetUtcNow());
        var uitkomst = VerbruikLezer.Lees(tekst, vandaag);

        if (!uitkomst.Gelukt)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["bestand"] = [uitkomst.Fout ?? "Hier konden we geen maandcijfers in vinden."],
            });
        }

        var nu = clock.GetUtcNow();
        var maanden = uitkomst.Maanden.Select(m => m.Maand_).ToList();

        var bestaand = await db.SupplierUsages
            .Where(x => x.HouseholdId == householdId && maanden.Contains(x.Month))
            .ToDictionaryAsync(x => x.Month, ct);

        foreach (var maand in uitkomst.Maanden)
        {
            if (bestaand.TryGetValue(maand.Maand_, out var rij))
            {
                rij.ImportKwh = maand.AfnameKwh;
                rij.ExportKwh = maand.TerugleveringKwh ?? rij.ExportKwh;
                rij.UpdatedAt = nu;
                continue;
            }

            db.SupplierUsages.Add(new SupplierUsage
            {
                Id = Guid.NewGuid(),
                HouseholdId = householdId,
                Month = maand.Maand_,
                ImportKwh = maand.AfnameKwh,
                ExportKwh = maand.TerugleveringKwh,
                CreatedAt = nu,
                UpdatedAt = nu,
            });
        }

        await db.SaveChangesAsync(ct);

        // Wat wij die maanden zelf maten, zodat het verschil meteen zichtbaar is.
        var vanaf = maanden.Min();
        var tot = maanden.Max().AddMonths(1);
        var eigen = await db.DailySummaries
            .Where(x => x.HouseholdId == householdId && x.Date >= vanaf && x.Date < tot)
            .Select(x => new { x.Date, x.ImportKwh })
            .ToListAsync(ct);

        var eigenPerMaand = eigen
            .GroupBy(x => new DateOnly(x.Date.Year, x.Date.Month, 1))
            .ToDictionary(g => g.Key, g => Math.Round(g.Sum(x => x.ImportKwh), 1));

        var rijen = uitkomst.Maanden
            .Select(m => new LeverancierMaandDto(
                m.Maand_,
                m.AfnameKwh,
                m.TerugleveringKwh,
                eigenPerMaand.TryGetValue(m.Maand_, out var onze) && onze > 0 ? onze : null))
            .ToList();

        return Results.Ok(new VerbruikImportDto(rijen));
    }

    /// <summary>
    /// Een export uit het portaal van de fabrikant inlezen.
    ///
    /// Wij beginnen te meten op de dag dat iemand koppelt; wie zijn panelen jaren eerder liet leggen
    /// ziet dus een leeg jaaroverzicht. Die jaren staan wel in het portaal van zijn fabrikant, en
    /// bij Enphase kun je ze als CSV downloaden.
    ///
    /// Alleen dagen die wij nog niet zelf gemeten hebben worden gevuld. Een eigen meting is
    /// nauwkeuriger dan een dagtotaal uit een ander systeem, en hem overschrijven zou een gat in de
    /// besparing maken die we al hadden uitgerekend. Vandaag slaan we altijd over: die dag is nog
    /// niet klaar.
    /// </summary>
    private static async Task<IResult> HistorieImporterenAsync(
        HttpContext http,
        AppDbContext db,
        SummaryService summaries,
        TimeProvider clock,
        CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;

        if (!http.Request.HasFormContentType)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["bestand"] = ["Stuur het CSV-bestand mee als formulierveld 'bestand'."],
            });
        }

        var form = await http.Request.ReadFormAsync(ct);
        var bestand = form.Files["bestand"] ?? form.Files.FirstOrDefault();

        if (bestand is null || bestand.Length == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["bestand"] = ["Er zat geen bestand bij."],
            });
        }

        if (bestand.Length > MaximumBestand)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["bestand"] = [$"Dit bestand is groter dan {MaximumBestand / (1024 * 1024)} MB. Exporteer per jaar."],
            });
        }

        string inhoud;
        await using (var stroom = bestand.OpenReadStream())
        {
            using var lezer = new StreamReader(stroom, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            inhoud = await lezer.ReadToEndAsync(ct);
        }

        var vandaag = DutchTime.LocalDate(clock.GetUtcNow());
        var uitkomst = HistorieImport.Lees(inhoud, vandaag);

        if (!uitkomst.Gelukt)
        {
            var uitleg = uitkomst.Fout ?? "Dit bestand konden we niet lezen.";
            if (uitkomst.GezieneKoppen.Count > 0)
            {
                uitleg += " Gevonden kolommen: " + string.Join(", ", uitkomst.GezieneKoppen.Take(12)) + ".";
            }

            return Results.ValidationProblem(new Dictionary<string, string[]> { ["bestand"] = [uitleg] });
        }

        var van = uitkomst.Dagen[0].Datum;
        var tot = uitkomst.Dagen[^1].Datum;

        // Rijen die er al staan. Een rij met een eigen meting erin laten we met rust; een lege rij
        // (aangemaakt maar nooit gevuld) mag wel gevuld worden.
        var bestaand = await db.DailySummaries
            .Where(x => x.HouseholdId == householdId && x.Date >= van && x.Date <= tot)
            .ToDictionaryAsync(x => x.Date, ct);

        var eigenMetingen = bestaand
            .Where(p => p.Value.PvKwh > 0 || p.Value.LastSampleAt is not null)
            .Select(p => p.Key)
            .ToHashSet();

        var (overnemen, overgeslagen) = HistorieImport.Kiezen(uitkomst.Dagen, eigenMetingen, vandaag);

        if (overnemen.Count == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["bestand"] = [overgeslagen > 0
                    ? "Deze dagen staan er al in; er is niets bijgekomen."
                    : "In dit bestand staat alleen vandaag, en die dag is nog niet klaar."],
            });
        }

        var nu = clock.GetUtcNow();

        foreach (var dag in overnemen)
        {
            if (bestaand.TryGetValue(dag.Datum, out var rij))
            {
                rij.PvKwh = dag.Kwh;
                rij.ImportedAt = nu;
                rij.UpdatedAt = nu;
                continue;
            }

            db.DailySummaries.Add(new DailySummary
            {
                Id = Guid.NewGuid(),
                HouseholdId = householdId,
                Date = dag.Datum,
                PvKwh = dag.Kwh,
                ImportedAt = nu,
                UpdatedAt = nu,
            });
        }

        await db.SaveChangesAsync(ct);

        // De besparing van die dagen uitrekenen met het tarief dat toen gold.
        await summaries.RecalculateSavingsAsync(householdId, van, ct);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new HistorieImportDto(overnemen.Count, overgeslagen, van, tot,
            Math.Round(overnemen.Sum(d => d.Kwh), 1)));
    }

    /// <summary>Een export van meer dan dit is bijna altijd het verkeerde bestand.</summary>
    private const long MaximumBestand = 8 * 1024 * 1024;

    private static async Task<IResult> HouseholdAsync(
        HttpContext http, AppDbContext db, InsightsService insights, TimeProvider clock, CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var household = await db.Households
            .Include(h => h.Installer)
            .FirstOrDefaultAsync(h => h.Id == householdId, ct);

        if (household is null) return Results.NotFound();

        // De schatting alleen uitrekenen als de bewoner zelf niets heeft ingevuld; anders is het
        // werk voor een getal dat toch niet gebruikt wordt.
        var geschat = household.PvPeakKw is null
            ? await insights.GeschatPiekvermogenAsync(householdId, DutchTime.LocalDate(clock.GetUtcNow()), ct)
            : null;

        return Results.Ok(HouseholdQueries.ToDto(household) with { PvPeakKwGeschat = geschat });
    }

    /// <summary>
    /// Wat de bewoner zelf over zijn woning kan instellen.
    ///
    /// De plaats is het belangrijkste veld en het stond nergens: zonder plaats hebben we geen
    /// coordinaten, zonder coordinaten geen zonverwachting, en dan zegt het adviesscherm "koppel je
    /// zonnepanelen en vul je postcode in" aan iemand die zijn panelen allang gekoppeld heeft en
    /// nergens een postcodeveld kan vinden.
    ///
    /// We zoeken de coordinaten meteen op en bewaren ze. Lukt dat niet (plaats onbekend, weerdienst
    /// plat), dan slaan we de plaats wel op en probeert de nachtelijke weertaak het opnieuw; de
    /// bewoner krijgt dan te zien dat we de plaats nog niet konden vinden in plaats van een stille
    /// mislukking.
    /// </summary>
    private static async Task<IResult> WoningBijwerkenAsync(
        UpdateHouseholdRequest request,
        HttpContext http,
        AppDbContext db,
        IWeatherProvider weather,
        CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var woning = await db.Households
            .Include(h => h.Installer)
            .FirstOrDefaultAsync(h => h.Id == householdId, ct);

        if (woning is null)
        {
            return Results.NotFound();
        }

        var fouten = new Dictionary<string, string[]>();

        if (request.Name is { } naam)
        {
            var schoon = naam.Trim();
            if (schoon.Length is < 1 or > 60)
            {
                fouten["name"] = ["Geef je huishouden een naam van 1 tot 60 tekens."];
            }
            else
            {
                woning.Name = schoon;
            }
        }

        if (request.PvPeakKw is { } kwp && (kwp <= 0 || kwp > 100))
        {
            fouten["pvPeakKw"] = ["Een piekvermogen ligt tussen 0,1 en 100 kWp."];
        }

        if (fouten.Count > 0)
        {
            return Results.ValidationProblem(fouten);
        }

        if (request.PvPeakKw is { } peak)
        {
            woning.PvPeakKw = Math.Round(peak, 2);
        }

        var nieuwePlaats = Schoon(request.PostalCode);
        var nieuweStad = Schoon(request.City);
        var plaatsVeranderd = (nieuwePlaats is not null && nieuwePlaats != woning.PostalCode)
                              || (nieuweStad is not null && nieuweStad != woning.City);

        if (nieuwePlaats is not null) woning.PostalCode = nieuwePlaats;
        if (nieuweStad is not null) woning.City = nieuweStad;

        if (plaatsVeranderd)
        {
            // Coordinaten horen bij de plaats; bij een nieuwe plaats zijn de oude niet meer waar.
            woning.Latitude = null;
            woning.Longitude = null;

            var zoekterm = !string.IsNullOrWhiteSpace(woning.City) ? woning.City : woning.PostalCode;
            if (!string.IsNullOrWhiteSpace(zoekterm))
            {
                try
                {
                    if (await weather.GeocodeAsync(zoekterm!, ct) is { } gevonden)
                    {
                        woning.Latitude = gevonden.Lat;
                        woning.Longitude = gevonden.Lon;
                    }
                }
                catch (Exception)
                {
                    // De weerdienst is niet van ons. De plaats is opgeslagen; de nachtelijke taak
                    // probeert het opnieuw.
                }
            }
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(HouseholdQueries.ToDto(woning));
    }



    /// <summary>Lege invoer is "niet meegestuurd"; een veld leegmaken doe je niet per ongeluk.</summary>
    private static string? Schoon(string? waarde)
    {
        var schoon = waarde?.Trim();
        return string.IsNullOrWhiteSpace(schoon) ? null : schoon;
    }

    /// <summary>
    /// Een apparaat een eigen naam geven.
    ///
    /// De fabrikant levert namen als "Demo omvormer 4,2 kWp" of een serienummer. Wie twee strings op
    /// zijn dak heeft, wil er "voorkant" en "achterkant" van maken, en dat kon nergens.
    /// </summary>
    private static async Task<IResult> ApparaatHernoemenAsync(
        Guid id,
        ApparaatHernoemenRequest request,
        HttpContext http,
        AppDbContext db,
        ConnectorRegistry registry,
        TimeProvider clock,
        CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var apparaat = await db.Devices.FirstOrDefaultAsync(d => d.Id == id && d.HouseholdId == householdId, ct);
        if (apparaat is null) return Results.NotFound();

        var naam = (request.Naam ?? string.Empty).Trim();
        if (naam.Length is < 1 or > 60)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["naam"] = ["Geef een naam van 1 tot 60 tekens."],
            });
        }

        apparaat.Name = naam;
        await db.SaveChangesAsync(ct);

        return Results.Ok(HouseholdQueries.ToDto(
            apparaat, clock.GetUtcNow(), registry.Get(apparaat.Brand)?.KanSturen ?? false));
    }

    /// <summary>
    /// Het sturen van een laadpaal aan- of uitzetten.
    ///
    /// Apart van het koppelen, en standaard uit. Uitlezen is kijken; starten en pauzeren verandert
    /// iets in het huis van iemand anders. Die twee horen niet onder dezelfde toestemming te vallen,
    /// en wie het aanzet moet dat bewust doen.
    /// </summary>
    private static async Task<IResult> SturenToestaanAsync(
        Guid id,
        SturenRequest request,
        HttpContext http,
        AppDbContext db,
        ConnectorRegistry registry,
        TimeProvider clock,
        CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var apparaat = await db.Devices.FirstOrDefaultAsync(d => d.Id == id && d.HouseholdId == householdId, ct);
        if (apparaat is null) return Results.NotFound();

        var connector = registry.Get(apparaat.Brand);
        if (connector is null || !connector.KanSturen)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["sturen"] = [$"{apparaat.Brand} kan (nog) niet worden aangestuurd."],
            });
        }

        apparaat.SturenToegestaan = request.Toestaan;
        await db.SaveChangesAsync(ct);

        return Results.Ok(HouseholdQueries.ToDto(apparaat, clock.GetUtcNow(), kanSturen: true));
    }

    /// <summary>
    /// Starten of pauzeren.
    ///
    /// Alleen als de bewoner het sturen zelf heeft aangezet, en alleen voor merken waarvan we weten
    /// hoe het moet. Pauzeren en niet stoppen: stoppen beëindigt de sessie en dan moet er bij
    /// sommige instellingen opnieuw met een pas worden geautoriseerd.
    /// </summary>
    private static async Task<IResult> LaadopdrachtAsync(
        Guid id,
        LaadopdrachtRequest request,
        HttpContext http,
        AppDbContext db,
        ConnectorRegistry registry,
        CredentialProtector protector,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var apparaat = await db.Devices.FirstOrDefaultAsync(d => d.Id == id && d.HouseholdId == householdId, ct);
        if (apparaat is null) return Results.NotFound();

        if (!apparaat.SturenToegestaan)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["sturen"] = ["Zet eerst bij deze paal aan dat Wattzicht hem mag starten en pauzeren."],
            });
        }

        var connector = registry.Get(apparaat.Brand);
        if (connector is null || !connector.KanSturen)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["sturen"] = [$"{apparaat.Brand} kan (nog) niet worden aangestuurd."],
            });
        }

        var credentials = new DeviceCredentials(protector.Unprotect(apparaat.EncryptedCredentials));

        try
        {
            await connector.StuurAsync(
                apparaat,
                credentials,
                request.Start ? Laadopdracht.Start : Laadopdracht.Pauze,
                ct);
        }
        catch (ConnectorException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["sturen"] = [ex.Message] });
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("Devices").LogWarning(ex, "Laadopdracht mislukt voor {Apparaat}", apparaat.Id);
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["sturen"] = ["De laadpaal nam de opdracht niet aan. Probeer het zo nog eens."],
            });
        }

        // Een opdracht die de paal aannam verandert zijn toestand; die willen we meteen zien.
        if (credentials.Changed)
        {
            apparaat.EncryptedCredentials = protector.Protect(credentials.ToDictionary());
            await db.SaveChangesAsync(ct);
        }

        return Results.NoContent();
    }

    /// <summary>
    /// Nu uitlezen, op verzoek van de bewoner.
    ///
    /// Waarom dit er moest komen: de poller draait op zijn eigen ritme, en wie net een token heeft
    /// rechtgezet wil niet een kwartier naar hetzelfde foutbericht kijken om te weten of het nu
    /// goed is. Dit doet precies dezelfde ronde als de poller, alleen nu meteen.
    ///
    /// Kastjes die alleen op het thuisnetwerk praten zitten hier niet bij; die leest de app zelf en
    /// die staat op de telefoon van de bewoner.
    /// </summary>
    private static async Task<IResult> NuUitlezenAsync(
        HttpContext http,
        PollingWorker poller,
        AppDbContext db,
        CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var (ok, mis) = await poller.PollHouseholdAsync(householdId, ct);

        var laatsteFout = await db.Devices
            .Where(d => d.HouseholdId == householdId && d.LastError != null)
            .OrderByDescending(d => d.LastSeenAt)
            .Select(d => d.LastError)
            .FirstOrDefaultAsync(ct);

        return Results.Ok(new UitleesResultaatDto(ok, mis, laatsteFout));
    }

    private static async Task<IResult> GetInstallerAccessAsync(HttpContext http, AppDbContext db, CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var household = await db.Households
            .Include(h => h.Installer)
            .FirstOrDefaultAsync(h => h.Id == householdId, ct);

        return household is null
            ? Results.NotFound()
            : Results.Ok(new InstallerAccessDto(household.InstallerAccessGranted, household.InstallerAccessChangedAt, household.Installer.Name));
    }

    private static async Task<IResult> SetInstallerAccessAsync(
        UpdateInstallerAccessRequest request,
        HttpContext http,
        AppDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var household = await db.Households
            .Include(h => h.Installer)
            .FirstOrDefaultAsync(h => h.Id == householdId, ct);

        if (household is null)
        {
            return Results.NotFound();
        }

        if (household.InstallerAccessGranted != request.Granted)
        {
            household.InstallerAccessGranted = request.Granted;
            household.InstallerAccessChangedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }

        return Results.Ok(new InstallerAccessDto(household.InstallerAccessGranted, household.InstallerAccessChangedAt, household.Installer.Name));
    }

    private static async Task<IResult> AccessLogAsync(HttpContext http, AppDbContext db, CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var rows = await db.InstallerAccessLogs
            .Where(l => l.HouseholdId == householdId)
            .OrderByDescending(l => l.At)
            .Take(50)
            .Select(l => new
            {
                l.At,
                l.Action,
                Who = db.Users.Where(u => u.Id == l.UserId).Select(u => u.Email).FirstOrDefault()
            })
            .ToListAsync(ct);

        return Results.Ok(rows.Select(r => new AccessLogEntryDto(r.At, r.Action, r.Who)).ToList());
    }

    private static async Task<IResult> InstallerAsync(HttpContext http, AppDbContext db, CancellationToken ct)
    {
        var installerId = http.User.GetInstallerId();
        var installer = await db.Installers.FirstOrDefaultAsync(i => i.Id == installerId, ct);
        return installer is null ? Results.NotFound() : Results.Ok(HouseholdQueries.ToDto(installer));
    }

    private static async Task<IResult> LiveAsync(HttpContext http, HouseholdQueries queries, CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        return Results.Ok(await queries.LiveAsync(householdId, ct));
    }

    private static async Task<IResult> MeAsync(HttpContext http, AppDbContext db, CancellationToken ct)
    {
        var userId = http.User.GetUserId();
        var user = await db.Users
            .Include(u => u.Installer)
            .Include(u => u.Household!)
                .ThenInclude(h => h.Installer)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(new MeDto(
            user.Id,
            user.Email,
            user.Role,
            user.Household is null ? null : HouseholdQueries.ToDto(user.Household),
            HouseholdQueries.ToDto(user.Installer)));
    }

    private static async Task<IResult> DashboardAsync(HttpContext http, HouseholdQueries queries, CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        return Results.Ok(await queries.DashboardAsync(householdId, ct));
    }

    private static async Task<IResult> DevicesAsync(HttpContext http, HouseholdQueries queries, CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        return Results.Ok(await queries.DevicesAsync(householdId, ct));
    }

    /// <summary>
    /// Terugkeeradressen die wij voor Enphase accepteren. Een open lijst zou een omleidingsgat zijn: wie
    /// zijn eigen adres mee mag geven, kan de toestemmingscode van een klant naar zichzelf laten sturen.
    /// Deze adressen moeten ook in de Enphase developer-app staan, anders weigert Enphase ze zelf al.
    /// </summary>
    private static readonly string[] EnphaseRedirects =
    [
        "https://wattzicht.nl/oauth/enphase",
        "https://www.wattzicht.nl/oauth/enphase",
        "https://wattzicht.com/oauth/enphase",
        "https://www.wattzicht.com/oauth/enphase",
        // Blijft erin zolang het oude adres nog bestaat; een app of bladwijzer van voor de verhuizing
        // komt daar anders klem te zitten halverwege het toestemmingsscherm van Enphase.
        "https://wattzicht.laadcash.nl/oauth/enphase",
        "http://localhost:5173/oauth/enphase",
    ];

    private static Task<IResult> EnphaseAuthorizeUrlAsync(
        [AsParameters] EnphaseAuthorizeUrlQuery query,
        EnphaseOptions enphase)
    {
        if (string.IsNullOrWhiteSpace(enphase.ClientId))
        {
            return Task.FromResult(Results.Problem(
                title: "Enphase is op de server nog niet ingesteld.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        var redirectUri = string.IsNullOrWhiteSpace(query.RedirectUri) ? EnphaseRedirects[0] : query.RedirectUri.Trim();
        if (!EnphaseRedirects.Contains(redirectUri))
        {
            return Task.FromResult(Results.Problem(
                title: "Dit terugkeeradres staat niet op onze lijst.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        var url = "https://api.enphaseenergy.com/oauth/authorize?response_type=code"
                  + "&client_id=" + Uri.EscapeDataString(enphase.ClientId)
                  + "&redirect_uri=" + Uri.EscapeDataString(redirectUri);

        return Task.FromResult(Results.Ok(new EnphaseAuthorizeUrlDto(url, redirectUri)));
    }

    private static async Task<IResult> ConnectDeviceAsync(
        ConnectDeviceRequest request,
        HttpContext http,
        AppDbContext db,
        ConnectorRegistry registry,
        CredentialProtector protector,
        SubscriptionWriter subscriptions,
        TimeProvider clock,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var logger = loggerFactory.CreateLogger("Devices");

        var connector = registry.Get(request.Brand);
        if (connector is null)
        {
            return Results.Problem(
                title: $"{request.Brand} kun je nog niet koppelen.",
                detail: $"Ondersteund: {string.Join(", ", registry.SupportedBrands)}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var credentials = new Dictionary<string, string>(
            request.Credentials ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);

        // De app stuurt "ip" voor lokale apparaten; de connectors lezen "host".
        if (!credentials.ContainsKey("host") && credentials.TryGetValue("ip", out var ip))
        {
            credentials["host"] = ip;
        }

        ConnectResult result;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            result = await connector.ConnectAsync(new ConnectRequest(request.Brand, request.Type, credentials), timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Results.Problem(title: "Het koppelen duurde te lang. Probeer het opnieuw.", statusCode: StatusCodes.Status400BadRequest);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Koppelen van {Brand} mislukt", request.Brand);
            return Results.Problem(title: "Koppelen mislukt door een onverwachte fout.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!result.Success)
        {
            return Results.Problem(title: result.Error ?? "Koppelen mislukt.", statusCode: StatusCodes.Status400BadRequest);
        }

        var now = clock.GetUtcNow();
        var existing = await db.Devices
            .Where(d => d.HouseholdId == householdId && d.Brand == request.Brand)
            .ToListAsync(ct);

        var created = new List<Device>();
        var hersteld = new List<Device>();
        var single = result.Devices.Count == 1;

        foreach (var found in result.Devices)
        {
            var merged = new Dictionary<string, string>(credentials, StringComparer.OrdinalIgnoreCase);
            if (found.ExtraCredentials is not null)
            {
                foreach (var pair in found.ExtraCredentials)
                {
                    merged[pair.Key] = pair.Value;
                }
            }

            // Staat dit apparaat er al, dan is dit geen tweede koppeling maar een reparatie: een
            // token dat verlopen was, een wachtwoord dat veranderde, een kastje op een nieuw adres.
            // Tot 13-11-2026 sloegen we zo'n poging over met "al gekoppeld", en dan was de enige
            // uitweg het apparaat verwijderen — inclusief alle metingen die eraan hingen.
            if (existing.FirstOrDefault(e => e.ExternalId == found.ExternalId && e.Type == found.Type) is { } bestaand)
            {
                bestaand.EncryptedCredentials = protector.Protect(merged);
                bestaand.LastError = null;
                if (single && !string.IsNullOrWhiteSpace(request.Name))
                {
                    bestaand.Name = request.Name.Trim();
                }

                hersteld.Add(bestaand);
                continue;
            }

            var device = new Device
            {
                Id = Guid.NewGuid(),
                HouseholdId = householdId,
                Type = found.Type,
                Brand = request.Brand,
                Name = single && !string.IsNullOrWhiteSpace(request.Name) ? request.Name.Trim() : found.Name,
                ExternalId = found.ExternalId,
                EncryptedCredentials = protector.Protect(merged),
                CreatedAt = now
            };

            db.Devices.Add(device);
            created.Add(device);
        }

        if (created.Count == 0 && hersteld.Count == 0)
        {
            return Results.Problem(title: "Er kwam geen apparaat uit deze koppeling.", statusCode: StatusCodes.Status400BadRequest);
        }

        await db.SaveChangesAsync(ct);

        // Eerste koppeling van deze woning: zeven dagen Plus, zonder betaalgegevens. Dit is het
        // moment waarop iemand de app voor het eerst echt gebruikt, en pas dan is Plus iets waard.
        // Eén keer per woning; opnieuw koppelen geeft geen tweede proefperiode.
        await subscriptions.StartTrialIfFirstAsync(householdId, ct);

        // Ook de herstelde koppelingen teruggeven: voor de app is "deze paal hangt er weer in"
        // hetzelfde bericht als "deze paal is gekoppeld", en een lege lijst zou lezen als niets.
        var dtos = created.Concat(hersteld).Select(d => HouseholdQueries.ToDto(d, now)).ToList();
        return Results.Created("/devices", dtos);
    }

    /// <summary>
    /// Het eigen huishouden verwijderen, met alles wat eraan hangt.
    ///
    /// Google eist dit van elke app waarin je een account kunt maken: weghalen moet kunnen vanuit de
    /// app zelf, en het moet echt weg zijn. Dat laatste is hier letterlijk genomen. Geen vlaggetje
    /// "verwijderd" waarmee de rijen blijven staan, maar de rijen zelf eruit.
    ///
    /// Alles staat expliciet opgesomd en niet overgelaten aan cascade-regels in de database. Twee
    /// redenen: je kunt het hier lezen en nazoeken, en het werkt bij elke database. Komt er later een
    /// tabel bij die aan een woning hangt, dan hoort die hier ook bij, en de test hieronder valt om
    /// als dat vergeten wordt.
    ///
    /// Wat niet meegaat: de installateur, die is van iemand anders en heeft andere klanten. En een
    /// lead van de website hangt niet aan een woning maar aan een installateur die zich aanmeldde;
    /// die staat hier dus bewust niet tussen.
    /// </summary>
    private static async Task<IResult> VerwijderAsync(
        HttpContext http,
        AppDbContext db,
        ILoggerFactory logs,
        CancellationToken ct)
    {
        var homeId = http.User.GetHouseholdId();
        if (homeId is not { } id)
        {
            return Results.Forbid();
        }

        var woning = await db.Households.IgnoreQueryFilters().FirstOrDefaultAsync(h => h.Id == id, ct);
        if (woning is null)
        {
            // Al weg. Dat is precies wat de aanvrager wilde, dus geen fout.
            return Results.NoContent();
        }

        await HuishoudenVerwijderen.UitvoerenAsync(db, id, ct);

        // Geen mailadres en geen naam in het logboek: dat zou het verwijderen zinloos maken.
        logs.CreateLogger("Verwijderen").LogInformation("Huishouden {Home} is op eigen verzoek verwijderd.", id);
        return Results.NoContent();
    }

    /// <summary>
    /// Een meting van een apparaat dat alleen op het thuisnetwerk praat.
    ///
    /// Onze server komt niet op iemands wifi, dus de app leest het kastje uit en stuurt het antwoord
    /// hierheen zoals het was. De omzetting gebeurt hier en niet in de app: dan kan een oude versie
    /// van de app geen eigen uitleg van de velden meesturen, en hoeft niemand te updaten als er een
    /// veldnaam bij komt.
    ///
    /// Alleen voor merken die als lokaal bekendstaan. Voor een Growatt of Zaptec zou dit een gaatje
    /// zijn om zelf cijfers te verzinnen die wij ook gewoon zelf kunnen ophalen.
    /// </summary>
    private static async Task<IResult> LocalReadingAsync(
        Guid id,
        LocalReadingRequest request,
        HttpContext http,
        AppDbContext db,
        ConnectorRegistry registry,
        AlertService alerts,
        TimeProvider clock,
        CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == id && d.HouseholdId == householdId, ct);
        if (device is null) return Results.NotFound();

        var connector = registry.Get(device.Brand);
        if (connector is null || !connector.KanDoorDeAppGelezenWorden)
        {
            return Results.Problem(
                title: "Dit apparaat lezen wij zelf uit; metingen opsturen kan hier niet.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var nu = clock.GetUtcNow();
        var gemeten = request.MeasuredAt ?? nu;

        // Een tijdstip uit de toekomst zou de dagtotalen en de "laatst gezien" van slag brengen, en
        // iets van gisteren hoort niet meer als nieuwste meting te gelden. De klok van een telefoon
        // loopt zo vaak een paar seconden voor, dus een kleine marge mag.
        if (gemeten > nu.AddMinutes(2) || gemeten < nu.AddHours(-1))
        {
            gemeten = nu;
        }

        DeviceReading meting;
        try
        {
            meting = connector.LeesLokaal(request.Raw, gemeten);
        }
        catch (Exception)
        {
            return Results.Problem(
                title: "Dit antwoord van de meter begrijpen we niet.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var entity = meting.ToEntity(device.Id);
        db.Readings.Add(entity);

        device.LastSeenAt = entity.Timestamp;
        device.LastError = null;
        await alerts.SetOfflineAsync(device, null, ct);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteDeviceAsync(Guid id, HttpContext http, AppDbContext db, CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == id && d.HouseholdId == householdId, ct);
        if (device is null)
        {
            return Results.NotFound();
        }

        db.Devices.Remove(device);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SummariesAsync(
        HttpContext http,
        HouseholdQueries queries,
        TimeProvider clock,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var today = DutchTime.LocalDate(clock.GetUtcNow());
        var end = to ?? today;
        var start = from ?? end.AddDays(-29);

        if (start > end)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["from"] = ["'from' moet voor 'to' liggen."] });
        }

        if (end.DayNumber - start.DayNumber > 366)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["to"] = ["Maximaal 1 jaar per verzoek."] });
        }

        return Results.Ok(await queries.SummariesAsync(householdId, start, end, ct));
    }

    private static async Task<IResult> AlertsAsync(HttpContext http, AppDbContext db, CancellationToken ct, bool all = false)
    {
        var householdId = http.User.GetHouseholdId()!.Value;

        var query = db.Alerts.Where(a => a.Device.HouseholdId == householdId);
        if (!all)
        {
            query = query.Where(a => a.ResolvedAt == null);
        }

        var alerts = await query
            .OrderByDescending(a => a.Since)
            .Take(200)
            .Select(a => new AlertDto(a.Id, a.DeviceId, a.Device.Name, a.Type, a.Message, a.Since, a.ResolvedAt))
            .ToListAsync(ct);

        return Results.Ok(alerts);
    }

    private static async Task<IResult> GetTariffAsync(HttpContext http, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var today = DutchTime.LocalDate(clock.GetUtcNow());

        var tariff = await db.Tariffs
            .Where(t => t.HouseholdId == householdId && t.ValidFrom <= today)
            .OrderByDescending(t => t.ValidFrom)
            .FirstOrDefaultAsync(ct);

        if (tariff is null)
        {
            var d = TariffRates.Default;
            return Results.Ok(new TariffDto(d.ImportPricePerKwh, d.ExportPricePerKwh, false, null, d.PublicChargingPricePerKwh, d.FeedInCostPerKwh));
        }

        return Results.Ok(ToDto(tariff));
    }

    private static async Task<IResult> SetTariffAsync(
        TariffDto request,
        HttpContext http,
        AppDbContext db,
        SummaryService summaries,
        TimeProvider clock,
        CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var validFrom = request.ValidFrom ?? DutchTime.LocalDate(clock.GetUtcNow());

        var errors = new Dictionary<string, string[]>();
        if (request.ImportTariffEur is < 0 or > 2)
        {
            errors["importTariffEur"] = ["Leveringstarief moet tussen 0 en 2 euro per kWh liggen."];
        }

        if (request.ExportTariffEur is < 0 or > 2)
        {
            errors["exportTariffEur"] = ["Terugleververgoeding moet tussen 0 en 2 euro per kWh liggen."];
        }

        if (request.FeedInCostEur is < 0 or > 1)
        {
            errors["feedInCostEur"] = ["Terugleverkosten moeten tussen 0 en 1 euro per kWh liggen."];
        }

        if (request.PublicChargingTariffEur is < 0 or > 3)
        {
            errors["publicChargingTariffEur"] = ["Publiek laadtarief moet tussen 0 en 3 euro per kWh liggen."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var tariff = await db.Tariffs
            .FirstOrDefaultAsync(t => t.HouseholdId == householdId && t.ValidFrom == validFrom, ct);

        if (tariff is null)
        {
            // Terugleverkosten niet meegestuurd? Neem ze over van het vorige tarief (oudere apps kennen het veld niet).
            var previousFeedIn = await db.Tariffs
                .Where(t => t.HouseholdId == householdId && t.ValidFrom < validFrom)
                .OrderByDescending(t => t.ValidFrom)
                .Select(t => (decimal?)t.FeedInCostPerKwh)
                .FirstOrDefaultAsync(ct);

            tariff = new Tariff
            {
                Id = Guid.NewGuid(),
                HouseholdId = householdId,
                ValidFrom = validFrom,
                FeedInCostPerKwh = previousFeedIn ?? 0m
            };
            db.Tariffs.Add(tariff);
        }

        if (request.FeedInCostEur is { } feedIn)
        {
            tariff.FeedInCostPerKwh = feedIn;
        }

        tariff.ImportPricePerKwh = request.ImportTariffEur;
        tariff.ExportPricePerKwh = request.ExportTariffEur;
        tariff.IsDynamic = request.IsDynamic;
        tariff.PublicChargingPricePerKwh = request.PublicChargingTariffEur is { } pub && pub > 0
            ? pub
            : SavingsCalculator.DefaultPublicChargingPrice;

        // Tarief en herberekende besparing samen opslaan, of allebei niet.
        // Eerst opslaan binnen de transactie: een nieuw tarief komt anders niet terug uit de query in RecalculateSavingsAsync.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.SaveChangesAsync(ct);
            await summaries.RecalculateSavingsAsync(householdId, validFrom, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });

        return Results.Ok(ToDto(tariff));
    }

    private static TariffDto ToDto(Tariff t) => new(
        t.ImportPricePerKwh,
        t.ExportPricePerKwh,
        t.IsDynamic,
        t.ValidFrom,
        t.PublicChargingPricePerKwh,
        t.FeedInCostPerKwh);
}
