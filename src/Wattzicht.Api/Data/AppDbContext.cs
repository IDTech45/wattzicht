using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Wattzicht.Api.Services;

namespace Wattzicht.Api.Data;

/// <summary>
/// EF Core-context voor Postgres. Timeseries staan in <see cref="Readings"/>.
/// <para>
/// Scheiding tussen klanten: elke tabel met huishouddata heeft een globaal queryfilter op basis van
/// <see cref="TenantContext"/>. Een bewoner ziet alleen het eigen huishouden; een installateur alleen
/// de eigen klanten die toestemming gaven. Zo lekt er niets, ook niet als een endpoint een Where vergeet.
/// </para>
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options, TenantContext? tenant = null)
    : DbContext(options), IDataProtectionKeyContext
{
    private readonly TenantContext _tenant = tenant ?? TenantContext.System;

    // Door EF per query opnieuw gelezen (eigenschappen van de context worden parameters in het filter).
    private bool TenantBypass => _tenant.IsSystem;
    private Guid? TenantHouseholdId => _tenant.HouseholdId;
    private Guid? TenantInstallerId => _tenant.InstallerId;

    public DbSet<Installer> Installers => Set<Installer>();
    public DbSet<Household> Households => Set<Household>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Reading> Readings => Set<Reading>();
    public DbSet<Tariff> Tariffs => Set<Tariff>();
    public DbSet<DailySummary> DailySummaries => Set<DailySummary>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<InstallerAccessLog> InstallerAccessLogs => Set<InstallerAccessLog>();
    public DbSet<PushDevice> PushDevices => Set<PushDevice>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<PromoEvent> PromoEvents => Set<PromoEvent>();

    /// <summary>Sleutelring van de Data Protection API (versleuteld met WZ_PROTECTION_KEY).</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PromoEvent>(e =>
        {
            e.ToTable("promo_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Campaign).HasMaxLength(40).IsRequired();
            e.Property(x => x.Channel).HasMaxLength(20).IsRequired();
            e.Property(x => x.Action).HasMaxLength(20).IsRequired();
            e.HasIndex(x => new { x.HouseholdId, x.Campaign, x.Channel, x.SentAt });
            e.HasOne(x => x.Household).WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Subscription>(e =>
        {
            e.ToTable("subscriptions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Source).HasMaxLength(20).IsRequired();
            e.Property(x => x.Status).HasMaxLength(20).IsRequired();
            e.Property(x => x.ExternalId).HasMaxLength(255);
            e.HasIndex(x => new { x.HouseholdId, x.Status });
            e.HasIndex(x => x.ExternalId);
            e.HasOne(x => x.Household).WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Installer>(e =>
        {
            e.ToTable("installers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(60).IsRequired();
            e.HasIndex(x => x.Slug).IsUnique();
            // Geen maximale lengte: het logo mag ook als data:-URL (base64) opgeslagen worden.
            e.Property(x => x.LogoUrl);
            e.Property(x => x.PrimaryColor).HasMaxLength(9).IsRequired();
            e.Property(x => x.AccentColor).HasMaxLength(9).IsRequired();
            e.Property(x => x.QuoteUrl).HasMaxLength(500);
            e.Property(x => x.Phone).HasMaxLength(40);
            e.Property(x => x.ContactEmail).HasMaxLength(320);
            e.Property(x => x.ContactPerson).HasMaxLength(200);
            e.Property(x => x.City).HasMaxLength(120);
            e.Property(x => x.BillingMode).HasMaxLength(20).IsRequired();
            e.Property(x => x.PricePerHousehold).HasPrecision(10, 2);
        });

        modelBuilder.Entity<Household>(e =>
        {
            e.ToTable("households");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.PostalCode).HasMaxLength(10);
            e.Property(x => x.City).HasMaxLength(100);
            e.Property(x => x.Plan).HasMaxLength(20).IsRequired();
            e.HasOne(x => x.Installer)
                .WithMany(i => i.Households)
                .HasForeignKey(x => x.InstallerId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.InstallerId);
            e.HasQueryFilter(h => TenantBypass
                || h.Id == TenantHouseholdId
                || (h.InstallerId == TenantInstallerId && h.InstallerAccessGranted));
        });

        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(320).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.PasswordHash).IsRequired();
            e.Property(x => x.Role).HasConversion<int>();
            e.HasOne(x => x.Household)
                .WithMany(h => h.Users)
                .HasForeignKey(x => x.HouseholdId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Installer)
                .WithMany(i => i.Users)
                .HasForeignKey(x => x.InstallerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Device>(e =>
        {
            e.ToTable("devices");
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasConversion<int>();
            e.Property(x => x.Brand).HasConversion<int>();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.ExternalId).HasMaxLength(200).IsRequired();
            e.Property(x => x.EncryptedCredentials).IsRequired();
            e.Property(x => x.LastError).HasMaxLength(1000);
            e.HasOne(x => x.Household)
                .WithMany(h => h.Devices)
                .HasForeignKey(x => x.HouseholdId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.HouseholdId, x.Brand, x.ExternalId }).IsUnique();
            e.HasQueryFilter(d => TenantBypass
                || d.HouseholdId == TenantHouseholdId
                || (d.Household.InstallerId == TenantInstallerId && d.Household.InstallerAccessGranted));
        });

        modelBuilder.Entity<Reading>(e =>
        {
            e.ToTable("readings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.HasOne(x => x.Device)
                .WithMany(d => d.Readings)
                .HasForeignKey(x => x.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
            // Belangrijkste index: laatste meting per apparaat en bereikquery's per dag.
            e.HasIndex(x => new { x.DeviceId, x.Timestamp }).IsDescending(false, true);
            e.HasQueryFilter(r => TenantBypass
                || r.Device.HouseholdId == TenantHouseholdId
                || (r.Device.Household.InstallerId == TenantInstallerId && r.Device.Household.InstallerAccessGranted));
        });

        modelBuilder.Entity<Tariff>(e =>
        {
            e.ToTable("tariffs");
            e.HasKey(x => x.Id);
            e.Property(x => x.ImportPricePerKwh).HasPrecision(10, 4);
            e.Property(x => x.ExportPricePerKwh).HasPrecision(10, 4);
            e.Property(x => x.PublicChargingPricePerKwh).HasPrecision(10, 4);
            e.Property(x => x.FeedInCostPerKwh).HasPrecision(10, 4);
            e.HasOne(x => x.Household)
                .WithMany(h => h.Tariffs)
                .HasForeignKey(x => x.HouseholdId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.HouseholdId, x.ValidFrom }).IsUnique();
            e.HasQueryFilter(t => TenantBypass
                || t.HouseholdId == TenantHouseholdId
                || (t.Household.InstallerId == TenantInstallerId && t.Household.InstallerAccessGranted));
        });

        modelBuilder.Entity<DailySummary>(e =>
        {
            e.ToTable("daily_summaries");
            e.HasKey(x => x.Id);
            e.Property(x => x.SavedEur).HasPrecision(12, 4);
            e.HasOne(x => x.Household)
                .WithMany(h => h.DailySummaries)
                .HasForeignKey(x => x.HouseholdId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.HouseholdId, x.Date }).IsUnique();
            e.HasQueryFilter(s => TenantBypass
                || s.HouseholdId == TenantHouseholdId
                || (s.Household.InstallerId == TenantInstallerId && s.Household.InstallerAccessGranted));
        });

        modelBuilder.Entity<Alert>(e =>
        {
            e.ToTable("alerts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasConversion<int>();
            e.Property(x => x.Message).HasMaxLength(500).IsRequired();
            e.Ignore(x => x.IsOpen);
            e.HasOne(x => x.Device)
                .WithMany(d => d.Alerts)
                .HasForeignKey(x => x.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.DeviceId, x.Type, x.ResolvedAt });
            e.HasQueryFilter(a => TenantBypass
                || a.Device.HouseholdId == TenantHouseholdId
                || (a.Device.Household.InstallerId == TenantInstallerId && a.Device.Household.InstallerAccessGranted));
        });

        modelBuilder.Entity<PushDevice>(e =>
        {
            e.ToTable("push_devices");
            e.HasKey(x => x.Id);
            e.Property(x => x.Token).HasMaxLength(4096).IsRequired();
            e.Property(x => x.Platform).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.Token).IsUnique().HasDatabaseName("ix_push_devices_token");
            e.HasIndex(x => x.HouseholdId).HasDatabaseName("ix_push_devices_household");
            e.HasOne(x => x.Household)
                .WithMany()
                .HasForeignKey(x => x.HouseholdId)
                .OnDelete(DeleteBehavior.Cascade);
            // Alleen de bewoner zelf; installateurs zien geen toestellen.
            e.HasQueryFilter(d => TenantBypass || d.HouseholdId == TenantHouseholdId);
        });

        modelBuilder.Entity<InstallerAccessLog>(e =>
        {
            e.ToTable("installer_access_log");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Action).HasMaxLength(40).IsRequired();
            e.HasOne(x => x.Household)
                .WithMany()
                .HasForeignKey(x => x.HouseholdId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.HouseholdId, x.At }).HasDatabaseName("ix_installer_access_log_household_at");
            // Bewoner ziet wie er bij hem keek; installateur ziet alleen eigen logregels.
            e.HasQueryFilter(l => TenantBypass
                || l.HouseholdId == TenantHouseholdId
                || (l.InstallerId == TenantInstallerId && l.Household.InstallerAccessGranted));
        });

        modelBuilder.Entity<Lead>(e =>
        {
            e.ToTable("leads");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Company).HasMaxLength(200).IsRequired();
            e.Property(x => x.Email).HasMaxLength(320).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(40);
            e.Property(x => x.Source).HasMaxLength(100);
            e.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<DataProtectionKey>(e => e.ToTable("data_protection_keys"));
    }
}
