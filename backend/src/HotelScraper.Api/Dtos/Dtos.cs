using System.Text.Json.Serialization;

namespace HotelScraper.Api.Dtos;

public record HotelOut(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("booking_id")] string BookingId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("address")] string? Address,
    [property: JsonPropertyName("stars")] int? Stars,
    [property: JsonPropertyName("review_score")] double? ReviewScore,
    [property: JsonPropertyName("image_url")] string? ImageUrl,
    [property: JsonPropertyName("distance_km")] double? DistanceKm,
    [property: JsonPropertyName("active")] bool Active,
    [property: JsonPropertyName("city")] string City
);

public record CityOut(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("dest_label")] string? DestLabel
);

public record HotelUpdate(
    [property: JsonPropertyName("active")] bool? Active
);

public record PricePoint(
    [property: JsonPropertyName("date")] DateOnly Date,
    [property: JsonPropertyName("price_eur")] double PriceEur,
    [property: JsonPropertyName("room_type")] string RoomType
);

public record HotelPrices(
    [property: JsonPropertyName("hotel_id")] int HotelId,
    [property: JsonPropertyName("hotel_name")] string HotelName,
    [property: JsonPropertyName("stars")] int? Stars,
    [property: JsonPropertyName("prices")] List<PricePoint> Prices
);

public record StatusOut(
    [property: JsonPropertyName("city")] string? City,
    [property: JsonPropertyName("total_hotels")] int TotalHotels,
    [property: JsonPropertyName("active_hotels")] int ActiveHotels,
    [property: JsonPropertyName("total_prices")] int TotalPrices,
    [property: JsonPropertyName("dates_covered")] int DatesCovered,
    [property: JsonPropertyName("dates_total")] int DatesTotal,
    [property: JsonPropertyName("coverage_pct")] double CoveragePct,
    [property: JsonPropertyName("last_fetch")] DateTime? LastFetch,
    [property: JsonPropertyName("scheduler_running")] bool SchedulerRunning,
    [property: JsonPropertyName("next_run")] DateTime? NextRun
);

public record FetchResult(
    [property: JsonPropertyName("dates_fetched")] int DatesFetched,
    [property: JsonPropertyName("hotels_found")] int HotelsFound,
    [property: JsonPropertyName("prices_saved")] int PricesSaved,
    [property: JsonPropertyName("errors")] List<string> Errors
);

public record VersionInfo(
    [property: JsonPropertyName("version")] string Version
);

public record ConfigResponse(
    [property: JsonPropertyName("dates_per_run")] int DatesPerRun
);

public record LoginRequest(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("password")] string Password
);

public record AuthUserOut(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("tenant_id")] int? TenantId,
    [property: JsonPropertyName("tenant_name")] string? TenantName,
    [property: JsonPropertyName("city")] string? City
);

public record WatchlistAddOut(
    [property: JsonPropertyName("hotel_id")] int HotelId,
    [property: JsonPropertyName("added")] bool Added
);

public record WatchlistRemoveOut(
    [property: JsonPropertyName("hotel_id")] int HotelId,
    [property: JsonPropertyName("removed")] bool Removed
);

public record TenantIn(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("city")] string City,
    [property: JsonPropertyName("is_active")] bool? IsActive
);

public record TenantOut(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("city")] string City,
    [property: JsonPropertyName("is_active")] bool IsActive,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt
);

public record UserIn(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("tenant_id")] int? TenantId,
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("is_active")] bool? IsActive
);

public record UserOut(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("tenant_id")] int? TenantId,
    [property: JsonPropertyName("tenant_name")] string? TenantName,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("is_active")] bool IsActive,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt
);

public record ResetPasswordIn(
    [property: JsonPropertyName("password")] string Password
);
