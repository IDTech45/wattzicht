using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Endpoints;

/// <summary>Slim sturen: advies wanneer je het beste laadt en verbruikt.</summary>
public static class AdviceEndpoints
{
    public static IEndpointRouteBuilder MapAdviceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/advice", AdviceAsync)
            .RequireAuthorization(AuthPolicies.Resident)
            .WithTags("Slim sturen")
            .WithSummary("Tips voor vandaag op basis van uurprijzen, zon en de eigen apparaten, met de besparing in euro.")
            .Produces<AdviceDto>();

        app.MapGet("/today", DagTipAsync)
            .RequireAuthorization(AuthPolicies.Resident)
            .WithTags("Slim sturen")
            .WithSummary("De tip van vandaag in één regel, voor de kaart op het startscherm.")
            .Produces<DayTipDto>()
            .Produces(StatusCodes.Status204NoContent);

        app.MapGet("/weather", WeerAsync)
            .RequireAuthorization(AuthPolicies.Resident)
            .WithTags("Slim sturen")
            .WithSummary("Het weer van vandaag en morgen, en het zonnige blok van morgen.")
            .Produces<WeerDto>()
            .Produces(StatusCodes.Status204NoContent);

        app.MapGet("/feed-in", FeedInAsync)
            .RequireAuthorization(AuthPolicies.Resident)
            .WithTags("Slim sturen")
            .WithSummary("Wat terugleveren kost (terugleverkosten) en wat zelf gebruiken scheelt, nu en vanaf 2027.")
            .Produces<FeedInDto>();

        app.MapGet("/charge-plan", ChargePlanAsync)
            .RequireAuthorization(AuthPolicies.Resident)
            .WithTags("Slim sturen")
            .WithSummary("Wanneer je auto laden het voordeligst is: uurprijzen afgewogen tegen de zon op je eigen dak.")
            .Produces<ChargePlanDto>()
            .ProducesValidationProblem();

        app.MapGet("/sustainability", async (HttpContext http, InsightsService insights, TimeProvider clock, CancellationToken ct) =>
                Results.Ok(await insights.SustainabilityAsync(http.User.GetHouseholdId()!.Value, DutchTime.LocalDate(clock.GetUtcNow()), ct)))
            .RequireAuthorization(AuthPolicies.Resident)
            .WithTags("Slim sturen")
            .WithSummary("Dit jaar: opwek, vermeden CO₂, zelfvoorziening en de prestatiecheck van de panelen.")
            .Produces<SustainabilityDto>();

        return app;
    }

    /// <summary>
    /// De tip van vandaag voor de kaart op het startscherm. Geeft 204 als er niets te melden valt:
    /// een dag zonder zon en zonder prijsverschil levert geen tip op, en dan blijft de kaart weg in
    /// plaats van iets algemeens te zeggen.
    /// </summary>
    private static async Task<IResult> DagTipAsync(
        HttpContext http,
        AppDbContext db,
        SummaryService summaries,
        IPriceProvider prices,
        IWeatherProvider weather,
        IConfiguration configuration,
        InsightsService insights,
        TimeProvider clock,
        CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var (input, today, _) = await BouwInvoerAsync(
            householdId, http.Request.Taal(), db, summaries, prices, weather, configuration, insights, clock, ct);

        var tip = DagTip.Bouw(input, today, AdviceEngine.Build(input));
        return tip is null ? Results.NoContent() : Results.Ok(tip);
    }

    /// <summary>
    /// Het weer van vandaag en morgen voor het beginscherm.
    ///
    /// Geeft 204 als we de plaats van deze woning niet kennen of de weerdienst niets geeft. Dat is
    /// met opzet geen leeg object met nullen erin: dan zou de app "0 graden en bewolkt" tonen, en dat
    /// is een bewering en geen gebrek aan gegevens.
    /// </summary>
    private static async Task<IResult> WeerAsync(
        HttpContext http,
        AppDbContext db,
        IWeatherProvider weather,
        InsightsService insights,
        IConfiguration configuration,
        TimeProvider clock,
        CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var woning = await db.Households.FirstOrDefaultAsync(h => h.Id == householdId, ct);
        if (woning is null)
        {
            return Results.NoContent();
        }

        await ZorgVoorLocatieAsync(woning, db, weather, ct);
        if (woning is not { Latitude: { } lat, Longitude: { } lon })
        {
            return Results.NoContent();
        }

        var dagen = await weather.DailyForecastAsync(lat, lon, ct);
        if (dagen.Count == 0)
        {
            return Results.NoContent();
        }

        var vandaag = DutchTime.LocalDate(clock.GetUtcNow());
        var dagVandaag = dagen.FirstOrDefault(d => d.Datum == vandaag);
        if (dagVandaag is null)
        {
            return Results.NoContent();
        }

        // Het piekvermogen van dit dak, langs dezelfde ladder als het advies: wat de bewoner opgaf,
        // anders onze schatting uit zijn eigen metingen, en als laatste een gemiddeld dak. Zonder
        // opgave en zonder schatting blijft het getal weg; dan is het niet van dit dak.
        var piek = woning.PvPeakKw ?? await insights.GeschatPiekvermogenAsync(householdId, vandaag, ct);
        var uren = await weather.HourlyForecastAsync(lat, lon, ct);

        var dagMorgen = dagen.FirstOrDefault(d => d.Datum == vandaag.AddDays(1));
        var blok = piek is { } kwp ? ZonnigBlok.VoorDag(uren, vandaag.AddDays(1), kwp) : null;

        return Results.Ok(new WeerDto(
            NaarDto(dagVandaag, uren, piek),
            dagMorgen is null ? null : NaarDto(dagMorgen, uren, piek),
            blok));
    }

    /// <summary>Eén dag naar de app, met de verwachte opbrengst van dit dak erbij als we die kennen.</summary>
    private static DagWeerDto NaarDto(DagWeer dag, IReadOnlyList<HourlyIrradiance> uren, double? pvPiekKw)
    {
        double? kwh = null;
        if (pvPiekKw is { } kwp)
        {
            var straling = uren
                .Where(u => DutchTime.LocalDate(u.StartUtc) == dag.Datum)
                .Sum(u => u.WattPerM2 / 1000.0 * kwp);
            kwh = Math.Round(straling, 1);
        }

        return new DagWeerDto(dag.Datum, dag.Soort, dag.TempMaxC, dag.TempMinC, dag.ZonUren, kwh);
    }

    /// <summary>
    /// De plaats opzoeken als we alleen een postcode of stadsnaam hebben.
    ///
    /// Gebeurt hier en niet alleen in de nachtelijke weertaak, want anders vult iemand zijn postcode
    /// in en staat er een dag lang dat er geen verwachting is.
    /// </summary>
    private static async Task ZorgVoorLocatieAsync(
        Household woning, AppDbContext db, IWeatherProvider weather, CancellationToken ct)
    {
        if (woning.Latitude is not null || (woning.City is null && woning.PostalCode is null))
        {
            return;
        }

        var zoekterm = !string.IsNullOrWhiteSpace(woning.City) ? woning.City : woning.PostalCode;
        try
        {
            if (await weather.GeocodeAsync(zoekterm!, ct) is { } gevonden)
            {
                woning.Latitude = gevonden.Lat;
                woning.Longitude = gevonden.Lon;
                await db.SaveChangesAsync(ct);
            }
        }
        catch (Exception)
        {
            // De weerdienst is niet van ons; dan deze ronde zonder verwachting.
        }
    }

    /// <summary>
    /// Alles wat de adviesmotor nodig heeft voor één woning. Gedeeld door /advice en /today, zodat de
    /// kaart op het startscherm niet met andere cijfers kan komen dan de tab erachter.
    /// </summary>
    private static async Task<(AdviceInput Input, DateOnly Today, IReadOnlyList<HourPrice> Prices)> BouwInvoerAsync(
        Guid householdId,
        string taal,
        AppDbContext db,
        SummaryService summaries,
        IPriceProvider prices,
        IWeatherProvider weather,
        IConfiguration configuration,
        InsightsService insights,
        TimeProvider clock,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = DutchTime.LocalDate(now);

        var types = await db.Devices
            .Where(d => d.HouseholdId == householdId)
            .Select(d => d.Type)
            .Distinct()
            .ToListAsync(ct);

        var tariff = await db.Tariffs
            .Where(t => t.HouseholdId == householdId && t.ValidFrom <= today)
            .OrderByDescending(t => t.ValidFrom)
            .FirstOrDefaultAsync(ct);
        var rates = await summaries.GetRatesAsync(householdId, today, ct);

        var since = today.AddDays(-14);
        var history = await db.DailySummaries
            .Where(s => s.HouseholdId == householdId && s.Date >= since && s.Date < today)
            .Select(s => new { s.ChargedKwh, s.ExportKwh, s.BatteryDischargedKwh })
            .ToListAsync(ct);

        var hourPrices = await prices.GetPricesAsync(today, ct);
        var surcharge = configuration.GetValue("Advice:SurchargePerKwh", AdviceEngine.DefaultSurchargePerKwh);

        // De zonverwachting van deze woning. Zonder plaats of zonder bereikbare weerdienst blijft hij
        // leeg; het advies valt dan terug op de middaguren.
        var woning = await db.Households.FirstOrDefaultAsync(h => h.Id == householdId, ct);

        // Staat de plaats er wel maar de coordinaten nog niet, dan zoeken we ze hier op in plaats van
        // te wachten op de nachtelijke weertaak. Anders vult iemand zijn postcode in en blijft er bij
        // "wanneer laad ik mijn auto" een dag lang staan dat er geen zonverwachting is.
        if (woning is not null)
        {
            await ZorgVoorLocatieAsync(woning, db, weather, ct);
        }

        var forecast = woning is { Latitude: { } lat, Longitude: { } lon }
            ? await weather.HourlyForecastAsync(lat, lon, ct)
            : [];

        var input = new AdviceInput(
            now,
            hourPrices,
            HasPv: types.Contains(DeviceType.Inverter),
            HasBattery: types.Contains(DeviceType.Battery),
            HasCharger: types.Contains(DeviceType.Charger),
            IsDynamic: tariff?.IsDynamic ?? false,
            rates,
            AvgDailyChargedKwh: history.Count > 0 ? history.Average(h => h.ChargedKwh) : 0,
            AvgDailyExportKwh: history.Count > 0 ? history.Average(h => h.ExportKwh) : 0,
            TypicalBatteryKwh: history.Count > 0 ? history.Max(h => h.BatteryDischargedKwh) : 0,
            SurchargePerKwh: surcharge,
            Forecast: forecast,
            // Het piekvermogen van dit dak: wat de bewoner invulde, anders wat wij uit zijn eigen
            // metingen schatten, en pas als laatste de instelling van de server (een gemiddeld dak).
            PvPeakKw: woning?.PvPeakKw
                      ?? await insights.GeschatPiekvermogenAsync(householdId, today, ct)
                      ?? configuration.GetValue("Advice:PvPeakKw", 4.0),
            Taal: taal);

        return (input, today, hourPrices);
    }

    private static async Task<IResult> AdviceAsync(
        HttpContext http,
        AppDbContext db,
        SummaryService summaries,
        HouseholdQueries queries,
        IPriceProvider prices,
        IWeatherProvider weather,
        IConfiguration configuration,
        InsightsService insights,
        TimeProvider clock,
        CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var (input, _, hourPrices) = await BouwInvoerAsync(
            householdId, http.Request.Taal(), db, summaries, prices, weather, configuration, insights, clock, ct);

        var advice = AdviceEngine.Build(input);

        // Levert het huis nu flink terug? Dan bovenaan een tip voor nu.
        if (input.HasPv)
        {
            var live = await queries.LiveAsync(householdId, ct);
            var liveTip = FeedInCalculator.LiveSurplusTip(live, input.NowUtc, input.HasCharger, input.Rates);
            if (liveTip is not null)
            {
                advice = advice with { Tips = [liveTip, .. advice.Tips] };
            }
        }

        return Results.Ok(advice with { PriceSource = hourPrices.Count > 0 ? prices.Source : null });
    }

    /// <summary>
    /// Een laadplan voor één auto: hoeveel er in moet en wanneer hij klaar moet zijn.
    ///
    /// Bewust parameters en geen opgeslagen instelling: de vraag verschilt per dag. Vanavond moet er
    /// 30 kWh in voor een rit naar Groningen, morgen is 10 kWh genoeg. Een vast schema zou daar altijd
    /// naast zitten.
    /// </summary>
    private static async Task<IResult> ChargePlanAsync(
        HttpContext http,
        AppDbContext db,
        SummaryService summaries,
        IPriceProvider prices,
        IWeatherProvider weather,
        IConfiguration configuration,
        TimeProvider clock,
        CancellationToken ct,
        double kwh = 20,
        DateTimeOffset? by = null,
        double? chargerKw = null)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var now = clock.GetUtcNow();
        var today = DutchTime.LocalDate(now);

        if (kwh is <= 0 or > 200)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["kwh"] = ["Vul in hoeveel kilowattuur erin moet, tussen 1 en 200."]
            });
        }

        // Standaard morgenochtend zeven uur: dat is waar de meeste mensen hun auto voor nodig hebben.
        var deadline = by ?? DutchTime.StartOfLocalDayUtc(today.AddDays(1)).AddHours(7);
        if (deadline <= now)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["by"] = ["Het tijdstip waarop hij vol moet zijn ligt in het verleden."]
            });
        }

        var household = await db.Households
            .Where(h => h.Id == householdId)
            .Select(h => new { h.Latitude, h.Longitude, h.City })
            .FirstOrDefaultAsync(ct);

        var tariff = await db.Tariffs
            .Where(t => t.HouseholdId == householdId && t.ValidFrom <= today)
            .OrderByDescending(t => t.ValidFrom)
            .FirstOrDefaultAsync(ct);
        var rates = await summaries.GetRatesAsync(householdId, today, ct);

        var pvPiek = configuration.GetValue("Advice:PvPeakKw", 4.0);
        var laadVermogen = chargerKw is > 0 and <= 50 ? chargerKw.Value : configuration.GetValue("Advice:ChargerKw", 11.0);

        var hourPrices = await prices.GetPricesAsync(today, ct);
        var forecast = household is { Latitude: { } lat, Longitude: { } lon }
            ? await weather.HourlyForecastAsync(lat, lon, ct)
            : [];

        var plan = ChargePlanner.Build(new ChargePlanInput(
            now, deadline, kwh, laadVermogen, hourPrices, forecast, pvPiek,
            IsDynamic: tariff?.IsDynamic ?? false,
            rates,
            configuration.GetValue("Advice:SurchargePerKwh", AdviceEngine.DefaultSurchargePerKwh),
            Taal: http.Request.Taal()));

        return Results.Ok(new ChargePlanDto(
            plan.Slots.Select(s => new ChargeSlotDto(s.StartUtc, s.Kwh, s.SolarKwh, s.CostEur)).ToList(),
            plan.TotalKwh, plan.CostEur, plan.CostIfStartNowEur, plan.SavingEur, plan.SolarKwh,
            plan.Feasible, plan.Explanation,
            hourPrices.Count > 0 ? prices.Source : null));
    }

    private static async Task<IResult> FeedInAsync(
        HttpContext http,
        AppDbContext db,
        SummaryService summaries,
        HouseholdQueries queries,
        TimeProvider clock,
        CancellationToken ct)
    {
        var householdId = http.User.GetHouseholdId()!.Value;
        var now = clock.GetUtcNow();
        var today = DutchTime.LocalDate(now);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var yearAgo = today.AddDays(-364);

        var rates = await summaries.GetRatesAsync(householdId, today, ct);

        var days = await db.DailySummaries
            .Where(s => s.HouseholdId == householdId && s.Date >= yearAgo && s.Date <= today)
            .Select(s => new { s.Date, s.ExportKwh })
            .ToListAsync(ct);

        var live = await queries.LiveAsync(householdId, ct);

        return Results.Ok(FeedInCalculator.Build(
            today,
            rates,
            monthExportKwh: days.Where(d => d.Date >= monthStart).Sum(d => d.ExportKwh),
            // Jaar: alleen volle dagen, vandaag is nog niet af.
            exportKwhLastYear: days.Where(d => d.Date < today).Sum(d => d.ExportKwh),
            daysWithData: days.Count(d => d.Date < today),
            liveExportW: FeedInCalculator.LiveExportW(live, now)));
    }
}
