using Microsoft.EntityFrameworkCore;

namespace HotelScraper.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Hotel> Hotels => Set<Hotel>();
    public DbSet<Price> Prices => Set<Price>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantCity> TenantCities => Set<TenantCity>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<WatchlistItem> WatchlistItems => Set<WatchlistItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Hotel>(entity =>
        {
            entity.HasIndex(e => new { e.BookingId, e.City })
                  .IsUnique()
                  .HasDatabaseName("uq_booking_city");
        });

        modelBuilder.Entity<Price>(entity =>
        {
            entity.HasIndex(e => new { e.HotelId, e.Date, e.RoomType })
                  .IsUnique()
                  .HasDatabaseName("uq_hotel_date_room");
        });

        modelBuilder.Entity<TenantCity>(entity =>
        {
            // Composite PK (TenantId, City) — a tenant can be assigned 1..N cities.
            entity.HasKey(e => new { e.TenantId, e.City });

            entity.HasOne<Tenant>()
                  .WithMany()
                  .HasForeignKey(e => e.TenantId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasIndex(e => e.Email).IsUnique();

            entity.HasOne<Tenant>()
                  .WithMany()
                  .HasForeignKey(e => e.TenantId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<WatchlistItem>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.HotelId }).IsUnique();

            entity.HasOne<Tenant>()
                  .WithMany()
                  .HasForeignKey(e => e.TenantId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Hotel>()
                  .WithMany()
                  .HasForeignKey(e => e.HotelId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
