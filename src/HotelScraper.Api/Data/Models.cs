using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace HotelScraper.Api.Data;

[Index(nameof(BookingId))]
[Index(nameof(City))]
[Index(nameof(BookingId), nameof(City), IsUnique = true, Name = "uq_booking_city")]
public class Hotel
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string BookingId { get; set; } = "";

    [Required]
    [MaxLength(500)]
    public string Name { get; set; } = "";

    [MaxLength(1000)]
    public string? Address { get; set; }

    public int? Stars { get; set; }

    public double? ReviewScore { get; set; }

    [MaxLength(2000)]
    public string? ImageUrl { get; set; }

    public double? DistanceKm { get; set; }

    public bool Active { get; set; } = true;

    [Required]
    [MaxLength(100)]
    public string City { get; set; } = "";
}

[Index(nameof(HotelId))]
[Index(nameof(Date))]
[Index(nameof(HotelId), nameof(Date), IsUnique = true, Name = "uq_hotel_date")]
public class Price
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int HotelId { get; set; }

    public DateOnly Date { get; set; }

    public double PriceEur { get; set; }

    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}

public class Setting
{
    [Key]
    [MaxLength(200)]
    public string Key { get; set; } = "";

    [MaxLength(2000)]
    public string Value { get; set; } = "";
}
