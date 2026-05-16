using Microsoft.EntityFrameworkCore;

namespace HotelScraper.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Hotel> Hotels => Set<Hotel>();
    public DbSet<Price> Prices => Set<Price>();
    public DbSet<Setting> Settings => Set<Setting>();

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
    }
}
