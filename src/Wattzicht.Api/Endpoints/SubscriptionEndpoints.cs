using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Wattzicht.Shared;

namespace Wattzicht.Api.Endpoints;

/// <summary>
/// Aankopen uit de App Store en Google Play.
///
/// De app stuurt hier wat de store hem gaf; wij vragen het de store zelf na en schrijven pas daarna
/// een abonnement weg. Nooit andersom: wat de app zegt staat op het toestel van de klant.
///
/// De meldingen van Apple en Google komen op een eigen adres binnen, zonder inlog maar wel
/// gecontroleerd, en zorgen dat een verlenging, opzegging of terugbetaling vanzelf doorkomt. Zonder
/// die meldingen zou een abonnement dat vandaag verlengt bij ons morgen op verlopen staan.
/// </summary>
public static class SubscriptionEndpoints
{
    public static IEndpointRouteBuilder MapSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/subscriptions").WithTags("Plus");

        group.MapPost("/verify", VerifyAsync)
            .RequireAuthorization(AuthPolicies.Resident)
            .WithSummary("Controleert een aankoop bij de store en zet het abonnement klaar.")
            .Produces<VerifyPurchaseResultDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/restore", RestoreAsync)
            .RequireAuthorization(AuthPolicies.Resident)
            .WithSummary("Herstelt een eerdere aankoop; zelfde controle als bij kopen.")
            .Produces<VerifyPurchaseResultDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/notifications/apple", AppleNotificationAsync)
            .AllowAnonymous()
            .WithSummary("App Store Server Notifications v2.")
            .Produces(StatusCodes.Status200OK);

        group.MapPost("/notifications/google", GoogleNotificationAsync)
            .AllowAnonymous()
            .WithSummary("Google Play Real-time Developer Notifications (Pub/Sub push).")
            .Produces(StatusCodes.Status200OK);

        return app;
    }

    private static async Task<IResult> VerifyAsync(
        VerifyPurchaseRequest request,
        HttpContext http,
        IEnumerable<IStoreVerifier> verifiers,
        SubscriptionWriter subscriptions,
        EntitlementService entitlement,
        CancellationToken ct)
    {
        var homeId = http.User.GetHouseholdId()!.Value;
        return await VerifieerEnSchrijfAsync(homeId, request, verifiers, subscriptions, entitlement, ct);
    }

    private static async Task<IResult> RestoreAsync(
        VerifyPurchaseRequest request,
        HttpContext http,
        IEnumerable<IStoreVerifier> verifiers,
        SubscriptionWriter subscriptions,
        EntitlementService entitlement,
        CancellationToken ct)
    {
        // Herstellen is dezelfde controle als kopen: de app stuurt het bonnetje dat de store nog heeft,
        // wij vragen het na. Apple eist deze knop, en hij hoort ook gewoon te werken na een nieuwe
        // telefoon of een herinstallatie.
        var homeId = http.User.GetHouseholdId()!.Value;
        return await VerifieerEnSchrijfAsync(homeId, request, verifiers, subscriptions, entitlement, ct);
    }

    private static async Task<IResult> VerifieerEnSchrijfAsync(
        Guid homeId,
        VerifyPurchaseRequest request,
        IEnumerable<IStoreVerifier> verifiers,
        SubscriptionWriter subscriptions,
        EntitlementService entitlement,
        CancellationToken ct)
    {
        var platform = (request.Platform ?? "").Trim().ToLowerInvariant();
        var verifier = verifiers.FirstOrDefault(v => v.Platform == platform);

        if (verifier is null)
        {
            return Results.Problem(
                title: "Onbekend platform. Gebruik apple of google.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var bon = await verifier.VerifyAsync(request.Receipt ?? "", ct);
        if (!bon.Valid)
        {
            return Results.Problem(title: bon.Error ?? "Deze aankoop kon niet bevestigd worden.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var duur = bon.ExpiresAt is { } tot ? tot - DateTimeOffset.UtcNow : StoreProducts.Duur(bon.ProductId);
        var bron = platform == "apple" ? SubscriptionSource.Apple : SubscriptionSource.Google;

        var wissel = await subscriptions.StartAsync(
            homeId, bron, duur, bon.OriginalTransactionId, ct: ct);

        return Results.Ok(new VerifyPurchaseResultDto(
            Entitlement: await entitlement.GetForHomeAsync(homeId, ct),
            StartsLater: wissel.StartedLater,
            Message: wissel.StartedLater
                ? "Je hebt nog een lopende periode; je nieuwe abonnement begint daarna."
                : "Je hebt Plus. Bedankt."));
    }

    /// <summary>
    /// Een melding van Apple: verlenging, opzegging, terugbetaling of een afgelopen respijtperiode.
    ///
    /// Idempotent: dezelfde melding twee keer verwerken moet hetzelfde resultaat geven. Apple stuurt
    /// opnieuw als wij niet binnen een paar seconden 200 teruggeven, en dat gebeurt een keer.
    /// </summary>
    private static async Task<IResult> AppleNotificationAsync(
        AppleNotificationRequest request,
        AppDbContext db,
        SubscriptionWriter subscriptions,
        TimeProvider clock,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var log = loggerFactory.CreateLogger("StoreNotifications");
        var payload = AppleStoreVerifier.Payload(request.SignedPayload);
        if (payload is null)
        {
            log.LogWarning("Apple-melding zonder leesbare inhoud.");
            // Toch 200: anders blijft Apple het uren opnieuw proberen met dezelfde onleesbare melding.
            return Results.Ok();
        }

        var type = Tekst(payload.Value, "notificationType");
        var data = payload.Value.TryGetProperty("data", out var d) ? d : default;
        var transactie = AppleStoreVerifier.Payload(Tekst(data, "signedTransactionInfo"));
        var origineel = transactie is null ? null : Tekst(transactie.Value, "originalTransactionId");

        if (string.IsNullOrEmpty(origineel))
        {
            log.LogWarning("Apple-melding {Type} zonder transactienummer.", type);
            return Results.Ok();
        }

        await VerwerkMeldingAsync(db, subscriptions, clock, log, "apple", origineel, type, transactie, ct);
        return Results.Ok();
    }

    /// <summary>
    /// Een melding van Google, via Pub/Sub. De inhoud zit base64 in message.data.
    /// </summary>
    private static async Task<IResult> GoogleNotificationAsync(
        GoogleNotificationRequest request,
        AppDbContext db,
        SubscriptionWriter subscriptions,
        TimeProvider clock,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var log = loggerFactory.CreateLogger("StoreNotifications");

        if (string.IsNullOrWhiteSpace(request.Message?.Data))
        {
            return Results.Ok();
        }

        System.Text.Json.JsonElement inhoud;
        try
        {
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(request.Message.Data));
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            inhoud = doc.RootElement.Clone();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Google-melding niet te lezen.");
            return Results.Ok();
        }

        if (!inhoud.TryGetProperty("subscriptionNotification", out var melding))
        {
            // Test- en voidedPurchase-meldingen: niets te doen.
            return Results.Ok();
        }

        var token = Tekst(melding, "purchaseToken");
        var type = melding.TryGetProperty("notificationType", out var t) && t.TryGetInt32(out var n)
            ? n.ToString()
            : null;

        if (string.IsNullOrEmpty(token))
        {
            return Results.Ok();
        }

        await VerwerkMeldingAsync(db, subscriptions, clock, log, "google", token, type, null, ct);
        return Results.Ok();
    }

    /// <summary>
    /// Wat een melding met het abonnement doet. Bewust alleen de einddatum bijwerken of stoppen: een
    /// melding is geen bewijs van betaling, dus er komt hier nooit een nieuw abonnement uit. Een
    /// aankoop die wij niet kennen laten we met rust; die komt binnen via /verify zodra de app opent.
    /// </summary>
    private static async Task VerwerkMeldingAsync(
        AppDbContext db,
        SubscriptionWriter subscriptions,
        TimeProvider clock,
        ILogger log,
        string platform,
        string externalId,
        string? type,
        System.Text.Json.JsonElement? transactie,
        CancellationToken ct)
    {
        var rij = await db.Subscriptions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.ExternalId == externalId, ct);

        if (rij is null)
        {
            log.LogInformation("{Platform}-melding {Type} voor een onbekend abonnement; overgeslagen.", platform, type);
            return;
        }

        var now = clock.GetUtcNow();
        var gestopt = type is "EXPIRED" or "REFUND" or "REVOKE" or "13" or "12" or "3";
        var verlengd = type is "DID_RENEW" or "SUBSCRIBED" or "2" or "1" or "4" or "7";

        if (gestopt)
        {
            await subscriptions.StopAsync(rij.HouseholdId, $"{platform}: {type}", ct);
            log.LogInformation("Abonnement van woning {Home} gestopt door {Platform}-melding {Type}",
                rij.HouseholdId, platform, type);
            return;
        }

        if (verlengd)
        {
            var tot = transactie is null ? null : Millis(transactie.Value, "expiresDate");
            rij.Status = SubscriptionStatus.Active;
            rij.EndsAt = tot ?? (rij.EndsAt is { } huidig && huidig > now
                ? huidig + TimeSpan.FromDays(31)
                : now + TimeSpan.FromDays(31));
            await db.SaveChangesAsync(ct);
            log.LogInformation("Abonnement van woning {Home} verlengd tot {Tot:u} door {Platform}",
                rij.HouseholdId, rij.EndsAt, platform);
        }
    }

    private static string? Tekst(System.Text.Json.JsonElement element, string naam) =>
        element.ValueKind == System.Text.Json.JsonValueKind.Object
        && element.TryGetProperty(naam, out var v)
        && v.ValueKind == System.Text.Json.JsonValueKind.String
            ? v.GetString()
            : null;

    private static DateTimeOffset? Millis(System.Text.Json.JsonElement element, string naam) =>
        element.TryGetProperty(naam, out var v) && v.TryGetInt64(out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : null;
}
