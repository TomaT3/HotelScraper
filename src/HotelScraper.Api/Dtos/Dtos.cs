namespace HotelScraper.Api.Dtos;

public record HotelOut(
    int Id,
    string BookingId,
    string Name,
    string? Address,
    int? Stars,
    double? ReviewScore,
    string? ImageUrl,
    double? DistanceKm,
    bool Active,
    string City
);

public record CityOut(
    string Name,
    string? DestLabel
);

public record HotelUpdate(
    bool? Active
);

public record PricePoint(
    DateOnly Date,
    double PriceEur
);

public record HotelPrices(
    int HotelId,
    string HotelName,
    int? Stars,
    List<PricePoint> Prices
);

public record StatusOut(
    string? City,
    int TotalHotels,
    int ActiveHotels,
    int TotalPrices,
    int DatesCovered,
    int DatesTotal,
    double CoveragePct,
    DateTime? LastFetch,
    bool SchedulerRunning,
    DateTime? NextRun
);

public record FetchResult(
    int DatesFetched,
    int HotelsFound,
    int PricesSaved,
    List<string> Errors
);

public record VersionInfo(string Version);

public record ConfigResponse(int DatesPerRun);
