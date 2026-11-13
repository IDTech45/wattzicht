using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;

namespace Wattzicht.Api.Services;

/// <summary>
/// Houdt bij in welke taal een huishouden de app gebruikt.
///
/// Waarom dit nodig is: een pushmelding over een storing of een laadmoment komt uit een
/// achtergrondtaak. Daar is geen verzoek van de bewoner, dus ook geen kop met zijn taal erin. De
/// taal moet dus bij de woning staan.
///
/// Waarom zo en niet met een knop die opslaat: de app stuurt zijn taal al bij elk verzoek mee in
/// <c>Accept-Language</c>. Die lezen we hier en we leggen alleen een wijziging vast. Dat scheelt een
/// endpoint, en belangrijker: het kan niet uit de pas lopen. Een aparte opslaan-knop die één keer
/// mislukt laat de woning voor altijd op de oude taal staan.
///
/// De laatst bekende waarde staat in het geheugen, zodat er niet bij elk verzoek een query loopt.
/// Die cache mag verdwijnen bij een herstart: dan volgt er één extra controle per woning.
/// </summary>
public static class TaalBijhouden
{
    /// <summary>Laatst bekende taal per woning. Alleen om te voorkomen dat we steeds dezelfde vraag stellen.</summary>
    private static readonly ConcurrentDictionary<Guid, string> Bekend = new();

    /// <summary>
    /// Ruim bovenop het aantal huishoudens dat we voorzien, en toch een bovengrens: een cache die
    /// alleen groeit is een geheugenlek dat je pas na maanden ziet.
    /// </summary>
    private const int MaximumOnthouden = 20_000;

    public static IApplicationBuilder UseTaalBijhouden(this IApplicationBuilder app) =>
        app.Use(async (http, next) =>
        {
            await next();

            // Na de rest van de pijplijn: dit mag een verzoek nooit ophouden of laten mislukken.
            try
            {
                if (http.User.GetHouseholdId() is not { } woning) return;

                var taal = http.Request.Taal();
                if (Bekend.TryGetValue(woning, out var laatst) && laatst == taal) return;

                var db = http.RequestServices.GetService<AppDbContext>();
                if (db is null) return;

                var opgeslagen = await db.Households.IgnoreQueryFilters()
                    .Where(h => h.Id == woning)
                    .Select(h => h.Taal)
                    .FirstOrDefaultAsync(http.RequestAborted);

                if (opgeslagen is null) return;

                if (opgeslagen != taal)
                {
                    await db.Households.IgnoreQueryFilters()
                        .Where(h => h.Id == woning)
                        .ExecuteUpdateAsync(s => s.SetProperty(h => h.Taal, taal), http.RequestAborted);
                }

                if (Bekend.Count < MaximumOnthouden)
                {
                    Bekend[woning] = taal;
                }
            }
            catch (Exception)
            {
                // De taal van een melding is nooit belangrijk genoeg om een verzoek op te laten
                // klappen. Mislukt het, dan staat er volgende keer weer een kans.
            }
        });

    /// <summary>Voor tests: begin met een leeg geheugen.</summary>
    internal static void Vergeet() => Bekend.Clear();
}
