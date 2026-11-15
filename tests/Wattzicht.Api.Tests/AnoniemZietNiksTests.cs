using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Data;
using Wattzicht.Api.Services;
using Wattzicht.Shared;
using Xunit;

namespace Wattzicht.Api.Tests;

/// <summary>
/// Een anoniem verzoek ziet niets, en dat is een valkuil zodra je er iets moet opzoeken.
///
/// Het tenantfilter leunt op de ingelogde gebruiker. Dat is precies goed voor een endpoint dat per
/// ongeluk zonder inlog bereikbaar is, maar de terugkeerpagina van Enphase is bewust zonder inlog:
/// de woning komt daar uit het kaartje. Wie dan een gewone vraag stelt krijgt nul rijen terug, en
/// dat leest als "er staat nog niets".
///
/// Zo kwam mijn dak op 15-11-2026 een tweede keer in de lijst, als apparaat "Ibo" naast
/// "Zonnepanelen". De samenvoeging zocht naar een bestaand apparaat, kreeg een lege lijst, en
/// maakte er dus een nieuw aan. Twee omvormers op één dak, en de dagtotalen tellen alles op.
/// </summary>
public class AnoniemZietNiksTests
{
    private static readonly Guid Woning = Guid.NewGuid();

    /// <summary>Een tenantcontext zoals in een verzoek zonder inlog.</summary>
    private sealed class ZonderInlog : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = new DefaultHttpContext();
    }

    [Fact]
    public async Task Zonder_inlog_vindt_een_gewone_vraag_het_apparaat_niet()
    {
        var naam = Guid.NewGuid().ToString();
        var opties = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(naam).Options;

        await using (var systeem = new AppDbContext(opties, TenantContext.System))
        {
            systeem.Households.Add(new Household { Id = Woning, Name = "Thuis" });
            systeem.Devices.Add(new Device
            {
                Id = Guid.NewGuid(), HouseholdId = Woning, Type = DeviceType.Inverter,
                Brand = DeviceBrand.Enphase, Name = "Zonnepanelen",
                ExternalId = "enphase-lokaal-192.168.178.59",
            });
            await systeem.SaveChangesAsync();
        }

        await using var anoniem = new AppDbContext(opties, new TenantContext(new ZonderInlog()));

        // Dit is de valkuil: geen fout, geen melding, gewoon nul rijen.
        Assert.Empty(await anoniem.Devices.Where(d => d.HouseholdId == Woning).ToListAsync());

        // En dit is de uitweg voor code die de woning al weet uit iets anders dan een inlog.
        var gevonden = await anoniem.Devices.IgnoreQueryFilters()
            .Where(d => d.HouseholdId == Woning).ToListAsync();

        Assert.Single(gevonden);
        Assert.Equal("Zonnepanelen", gevonden[0].Name);

        // En dan doet de samenvoeging weer wat ze hoort te doen: de cloudkoppeling is hetzelfde dak.
        Assert.NotNull(ZelfdeInstallatie.Kies(gevonden, DeviceType.Inverter, "4678930"));
    }
}
