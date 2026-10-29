using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Wattzicht.Api.Data;

/// <summary>EF Core-context voor Postgres. Timeseries staan in <see cref="Readings"/>.</summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<Installer> Installers => Set<Installer>();
    public DbSet<Household> Households => Set<Household>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Reading> Readings => Set<Reading>();
    public DbSet<Tariff> Tariffs => Set<Tariff>();
    public DbSet<DailySummary> DailySummaries => Set<DailySummary>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<Lead> Leads => Set<Lead>();

    /// <summary>Sleutelring van de Data Protection API (versleuteld met WZ_PROTECTION_KEY).</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
        });

        modelBuilder.Entity<Household>(e =>
        {
            e.ToTable("households");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.PostalCode).HasMaxLength(10);
            e.Property(x => x.City).HasMaxLength(100);
            e.HasOne(x => x.Installer)
                .WithMany(i => i.Households)
                .HasForeignKey(x => x.InstallerId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.InstallerId);
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
        });

        modelBuilder.Entity<Tariff>(e =>
        {
            e.ToTable("tariffs");
            e.HasKey(x => x.Id);
            e.Property(x => x.ImportPricePerKwh).HasPrecision(10, 4);
            e.Property(x => x.ExportPricePerKwh).HasPrecision(10, 4);
            e.Property(x => x.PublicChargingPricePerKwh).HasPrecision(10, 4);
            e.HasOne(x => x.Household)
                .WithMany(h => h.Tariffs)
                .HasForeignKey(x => x.HouseholdId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.HouseholdId, x.ValidFrom }).IsUnique();
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
