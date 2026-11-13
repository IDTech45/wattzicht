using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;

namespace Wattzicht.Api.Services;

/// <summary>
/// Een huishouden en alles wat eraan hangt echt weghalen.
///
/// Staat hier en niet in het endpoint, zodat een test precies kan controleren wat er verdwijnt en
/// wat er blijft staan. Dat is bij verwijderen belangrijker dan bij wat ook: je kunt het achteraf
/// niet nakijken, want dan is het weg.
///
/// Alles staat expliciet opgesomd en wordt niet overgelaten aan cascade-regels in de database. Twee
/// redenen: je kunt hier lezen wat er gebeurt, en het werkt bij elke database. Komt er later een
/// tabel bij die aan een woning hangt, dan hoort die hier ook bij; de test valt om als dat vergeten
/// wordt.
/// </summary>
public static class HuishoudenVerwijderen
{
    public static async Task UitvoerenAsync(AppDbContext db, Guid homeId, CancellationToken ct = default)
    {
        // Metingen hangen aan apparaten en niet rechtstreeks aan de woning, dus die eerst, en via
        // het apparaat. Dezelfde reden voor meldingen.
        db.Readings.RemoveRange(db.Readings.IgnoreQueryFilters().Where(x => x.Device!.HouseholdId == homeId));
        db.Alerts.RemoveRange(db.Alerts.IgnoreQueryFilters().Where(x => x.Device!.HouseholdId == homeId));

        db.SupplierUsages.RemoveRange(
            db.SupplierUsages.IgnoreQueryFilters().Where(x => x.HouseholdId == homeId));
        db.DailySummaries.RemoveRange(db.DailySummaries.IgnoreQueryFilters().Where(x => x.HouseholdId == homeId));
        db.Devices.RemoveRange(db.Devices.IgnoreQueryFilters().Where(x => x.HouseholdId == homeId));
        db.Tariffs.RemoveRange(db.Tariffs.IgnoreQueryFilters().Where(x => x.HouseholdId == homeId));
        db.PushDevices.RemoveRange(db.PushDevices.IgnoreQueryFilters().Where(x => x.HouseholdId == homeId));
        db.Subscriptions.RemoveRange(db.Subscriptions.IgnoreQueryFilters().Where(x => x.HouseholdId == homeId));
        db.PromoEvents.RemoveRange(db.PromoEvents.IgnoreQueryFilters().Where(x => x.HouseholdId == homeId));
        db.InstallerAccessLogs.RemoveRange(db.InstallerAccessLogs.IgnoreQueryFilters().Where(x => x.HouseholdId == homeId));
        db.Users.RemoveRange(db.Users.IgnoreQueryFilters().Where(x => x.HouseholdId == homeId));

        var woning = await db.Households.IgnoreQueryFilters().FirstOrDefaultAsync(h => h.Id == homeId, ct);
        if (woning is not null)
        {
            db.Households.Remove(woning);
        }

        await db.SaveChangesAsync(ct);
    }
}
