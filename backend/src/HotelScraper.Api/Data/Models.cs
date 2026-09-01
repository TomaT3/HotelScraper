using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace HotelScraper.Api.Data;

[Index(nameof(BookingId))]
[Index(nameof(City))]
[Index(nameof(BookingId), nameof(City), IsUnique = true, Name = "uq_booking_city")]
[Table("hotels")]
public class Hotel
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    [Column("booking_id")]
    public string BookingId { get; set; } = "";

    [Required]
    [MaxLength(500)]
    [Column("name")]
    public string Name { get; set; } = "";

    [MaxLength(1000)]
    [Column("address")]
    public string? Address { get; set; }

    [Column("stars")]
    public int? Stars { get; set; }

    [Column("review_score")]
    public double? ReviewScore { get; set; }

    [MaxLength(2000)]
    [Column("image_url")]
    public string? ImageUrl { get; set; }

    [Column("distance_km")]
    public double? DistanceKm { get; set; }

    [Column("active")]
    public bool Active { get; set; } = true;

    [Required]
    [MaxLength(100)]
    [Column("city")]
    public string City { get; set; } = "";
}

[Index(nameof(HotelId))]
[Index(nameof(Date))]
[Index(nameof(HotelId), nameof(Date), nameof(RoomType), IsUnique = true, Name = "uq_hotel_date_room")]
[Table("prices")]
public class Price
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    [Column("hotel_id")]
    public int HotelId { get; set; }

    [Column("date")]
    public DateOnly Date { get; set; }

    [Column("price_eur")]
    public double PriceEur { get; set; }

    [MaxLength(20)]
    [Column("room_type")]
    public string RoomType { get; set; } = "double";

    [Column("fetched_at")]
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}

[Table("settings")]
public class Setting
{
    [Key]
    [MaxLength(200)]
    [Column("key")]
    public string Key { get; set; } = "";

    [MaxLength(2000)]
    [Column("value")]
    public string Value { get; set; } = "";
}

[Table("tenants")]
public class Tenant
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    [Column("name")]
    public string Name { get; set; } = "";

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// One tenant (hotel group) may see 1..N cities — each row is one city assignment.
[Table("tenant_cities")]
public class TenantCity
{
    [Column("tenant_id")]
    public int TenantId { get; set; }

    [Required]
    [MaxLength(100)]
    [Column("city")]
    public string City { get; set; } = "";
}

[Index(nameof(Email), IsUnique = true)]
[Table("users")]
public class AppUser
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [MaxLength(256)]
    [Column("email")]
    public string Email { get; set; } = "";

    [Required]
    [Column("password_hash")]
    public string PasswordHash { get; set; } = "";

    [Column("tenant_id")]
    public int? TenantId { get; set; }

    [Required]
    [MaxLength(20)]
    [Column("role")]
    public string Role { get; set; } = "user";

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[Index(nameof(TenantId), nameof(HotelId), IsUnique = true)]
[Table("watchlist_items")]
public class WatchlistItem
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    [Column("tenant_id")]
    public int TenantId { get; set; }

    [Column("hotel_id")]
    public int HotelId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
