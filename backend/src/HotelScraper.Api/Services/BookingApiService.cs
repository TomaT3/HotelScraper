using System.Text.Json;

namespace HotelScraper.Api.Services;

public class BookingApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BookingApiService> _logger;

    private const string BaseUrl = "https://booking-com15.p.rapidapi.com";

    public BookingApiService(HttpClient httpClient, ILogger<BookingApiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(BaseUrl);
        _httpClient.DefaultRequestHeaders.Add("X-RapidAPI-Host", "booking-com15.p.rapidapi.com");
    }

    /// <summary>
    /// Search for a city destination and return its dest_id, dest_type, and label.
    /// </summary>
    public async Task<LocationResult?> SearchLocationAsync(string city)
    {
        var response = await _httpClient.GetAsync(
            $"/api/v1/hotels/searchDestination?query={Uri.EscapeDataString(city)}"
        );
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<SearchDestinationResponse>();
        var destinations = data?.Data;

        if (destinations is null || destinations.Count == 0)
            return null;

        // Prefer city-type result
        foreach (var item in destinations)
        {
            if (string.Equals(item.DestType, "city", StringComparison.OrdinalIgnoreCase))
                return new LocationResult(
                    item.DestId.ToString(),
                    "city",
                    item.Label ?? item.Name ?? city
                );
        }

        // Fallback to first result
        var first = destinations[0];
        return new LocationResult(
            first.DestId.ToString(),
            first.DestType ?? "city",
            first.Label ?? first.Name ?? city
        );
    }

    /// <summary>
    /// Search for hotels with prices for a specific date range.
    /// Fetches page 1 and 2, deduplicates by booking_id.
    /// </summary>
    public async Task<List<HotelResult>> SearchHotelsAsync(string destId, DateOnly checkin, DateOnly checkout, int adults = 2)
    {
        var page1 = await FetchHotelPageAsync(destId, checkin, checkout, 1, adults);
        var page2 = await FetchHotelPageAsync(destId, checkin, checkout, 2, adults);

        // Deduplicate by booking_id — keep first occurrence
        var seen = new HashSet<string>();
        var combined = new List<HotelResult>();

        foreach (var hotel in page1.Concat(page2))
        {
            if (seen.Add(hotel.BookingId))
                combined.Add(hotel);
        }

        _logger.LogInformation(
            "Found {Unique} unique hotels (page1={P1}, page2={P2}) for {Date}",
            combined.Count, page1.Count, page2.Count, checkin
        );

        return combined;
    }

    private async Task<List<HotelResult>> FetchHotelPageAsync(
        string destId, DateOnly checkin, DateOnly checkout, int pageNumber, int adults = 2)
    {
        var queryParams = new Dictionary<string, string>
        {
            ["dest_id"] = destId,
            ["search_type"] = "CITY",
            ["arrival_date"] = checkin.ToString("yyyy-MM-dd"),
            ["departure_date"] = checkout.ToString("yyyy-MM-dd"),
            ["adults"] = adults.ToString(),
            ["room_qty"] = "1",
            ["units"] = "metric",
            ["temperature_unit"] = "c",
            ["languagecode"] = "en-us",
            ["currency_code"] = "EUR",
            ["page_number"] = pageNumber.ToString(),
            ["sort_by"] = "distance",
        };

        var url = $"/api/v1/hotels/searchHotels?{string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"))}";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<SearchHotelsResponse>();
        var hotels = data?.Data?.Hotels ?? [];

        var results = new List<HotelResult>();
        foreach (var hotel in hotels)
        {
            try
            {
                var prop = hotel.Property;
                if (prop is null) continue;

                var grossPrice = prop.PriceBreakdown?.GrossPrice;
                if (grossPrice?.Value is null) continue;

                if (!double.TryParse(grossPrice.Value.ToString(), out var priceValue) || priceValue <= 0)
                    continue;

                var distanceKm = DistanceParser.ParseDistanceFromLabel(hotel.AccessibilityLabel);
                var photoUrls = prop.PhotoUrls ?? [];
                var imageUrl = photoUrls.Count > 0 ? photoUrls[0] : "";

                results.Add(new HotelResult(
                    BookingId: prop.Id?.ToString() ?? "",
                    Name: prop.Name ?? "Unknown",
                    Stars: prop.AccuratePropertyClass is not null ? (int)prop.AccuratePropertyClass.Value : null,
                    ReviewScore: prop.ReviewScore,
                    ImageUrl: imageUrl,
                    PriceEur: Math.Round(priceValue, 2),
                    DistanceKm: distanceKm
                ));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse hotel data: {Name}", hotel.Property?.Name ?? "?");
            }
        }

        return results;
    }

    // ── JSON response types ─────────────────────────────────────────────

    private record SearchDestinationResponse(List<DestinationItem>? Data);
    private record DestinationItem(long DestId, string? DestType, string? Label, string? Name);
    private record SearchHotelsResponse(SearchHotelsData? Data);
    private record SearchHotelsData(List<HotelItem> Hotels);
    private record HotelItem(string? AccessibilityLabel, PropertyInfo? Property);
    private record PropertyInfo(
        long? Id,
        string? Name,
        long? AccuratePropertyClass,
        double? ReviewScore,
        List<string>? PhotoUrls,
        PriceBreakdownInfo? PriceBreakdown
    );
    private record PriceBreakdownInfo(GrossPriceInfo? GrossPrice);
    private record GrossPriceInfo(object? Value);
}

public record LocationResult(string DestId, string DestType, string Label);

public record HotelResult(
    string BookingId,
    string Name,
    int? Stars,
    double? ReviewScore,
    string ImageUrl,
    double PriceEur,
    double? DistanceKm
);
