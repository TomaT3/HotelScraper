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
