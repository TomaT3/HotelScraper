using System.Text.Json.Serialization;

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
        var url = $"/api/v1/hotels/searchDestination?query={Uri.EscapeDataString(city)}";
        _logger.LogInformation("Calling searchDestination API: {Url}", url);

        var response = await _httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            _logger.LogError("searchDestination failed with {StatusCode}: {ErrorBody}", (int)response.StatusCode, errorBody);
            response.EnsureSuccessStatusCode();
        }

        var rawJson = await response.Content.ReadAsStringAsync();
        _logger.LogInformation("searchDestination raw response (first 2000 chars): {RawJson}",
            rawJson.Length > 2000 ? rawJson[..2000] + "..." : rawJson);

        var data = System.Text.Json.JsonSerializer.Deserialize<SearchDestinationResponse>(rawJson,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var destinations = data?.Data;

        _logger.LogInformation("searchDestination returned {Count} destination(s)", destinations?.Count ?? 0);

        if (destinations is null || destinations.Count == 0)
        {
            _logger.LogWarning("No destinations found for city: {City}", city);
            return null;
        }

        // Prefer city-type result
        foreach (var item in destinations)
        {
            _logger.LogDebug("Destination candidate: dest_id={DestId}, type={DestType}, label={Label}",
                item.DestId, item.DestType, item.Label);
            if (string.Equals(item.DestType, "city", StringComparison.OrdinalIgnoreCase))
                return new LocationResult(
                    item.DestId,
                    "city",
                    item.Label ?? item.Name ?? city
                );
        }

        // Fallback to first result
        var first = destinations[0];
        if (string.IsNullOrWhiteSpace(first.DestId))
        {
            _logger.LogError("Fallback destination has null/empty dest_id. Label={Label}, Name={Name}, DestType={DestType}",
                first.Label, first.Name, first.DestType);
            return null;
        }
        _logger.LogInformation("No city-type result found, using fallback: dest_id={DestId}", first.DestId);
        return new LocationResult(
            first.DestId,
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
            ["languagecode"] = "de",
            ["currency_code"] = "EUR",
            ["page_number"] = pageNumber.ToString(),
            ["sort_by"] = "distance",
        };

        var url = $"/api/v1/hotels/searchHotels?{string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"))}";
        _logger.LogInformation("Calling searchHotels API (page {Page}): {Url}", pageNumber, url);

        var response = await _httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            _logger.LogError("searchHotels page {Page} failed with {StatusCode}: {ErrorBody}",
                pageNumber, (int)response.StatusCode, errorBody);
            response.EnsureSuccessStatusCode();
        }

        var rawJson = await response.Content.ReadAsStringAsync();
        _logger.LogInformation("searchHotels page {Page} raw response (first 2000 chars): {RawJson}",
            pageNumber, rawJson.Length > 2000 ? rawJson[..2000] + "..." : rawJson);

        var data = System.Text.Json.JsonSerializer.Deserialize<SearchHotelsResponse>(rawJson,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var hotels = data?.Data?.Hotels ?? [];

        _logger.LogInformation("searchHotels page {Page}: deserialized {Count} hotels from JSON",
            pageNumber, hotels.Count);

        // Diagnostic: log first 3 accessibilityLabels to understand format
        var sampleLabels = hotels.Take(3).Select(h => h.AccessibilityLabel).ToList();
        for (int i = 0; i < sampleLabels.Count; i++)
        {
            _logger.LogInformation("searchHotels page {Page} hotel[{I}] accessibilityLabel: {Label}",
                pageNumber, i, sampleLabels[i]);
        }

        if (hotels.Count == 0)
        {
            _logger.LogWarning("searchHotels page {Page}: ZERO hotels in response. data?.Data is null={DataNull}. Raw JSON (last 1000 chars): {RawTail}",
                pageNumber, data?.Data is null, rawJson.Length > 1000 ? rawJson[^1000..] : rawJson);
        }

        var results = new List<HotelResult>();
        int skippedNoProperty = 0;
        int skippedNoGrossPrice = 0;
        int skippedUnparseablePrice = 0;
        int skippedZeroPrice = 0;

        foreach (var hotel in hotels)
        {
            try
            {
                var prop = hotel.Property;
                if (prop is null)
                {
                    skippedNoProperty++;
                    _logger.LogDebug("Hotel skipped: property is null (accessibilityLabel={Label})",
                        hotel.AccessibilityLabel?[..Math.Min(hotel.AccessibilityLabel?.Length ?? 0, 80)]);
                    continue;
                }

                var grossPrice = prop.PriceBreakdown?.GrossPrice;
                if (grossPrice?.Value is null)
                {
                    skippedNoGrossPrice++;
                    _logger.LogDebug("Hotel skipped: no grossPrice value (name={Name}, id={Id})", prop.Name, prop.Id);
                    continue;
                }

                var priceStr = grossPrice.Value.ToString() ?? "";
                if (!double.TryParse(priceStr, out var priceValue) || priceValue <= 0)
                {
                    if (priceValue <= 0)
                        skippedZeroPrice++;
                    else
                        skippedUnparseablePrice++;
                    _logger.LogDebug("Hotel skipped: unparseable/zero price '{PriceStr}' (name={Name}, id={Id})",
                        priceStr, prop.Name, prop.Id);
                    continue;
                }

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

        _logger.LogInformation(
            "searchHotels page {Page}: parsed {Parsed}/{Total} hotels (skipped: noProperty={NoProp}, noGrossPrice={NoPrice}, unparseablePrice={BadPrice}, zeroPrice={ZeroPrice})",
            pageNumber, results.Count, hotels.Count, skippedNoProperty, skippedNoGrossPrice, skippedUnparseablePrice, skippedZeroPrice);

        return results;
    }

    // ── JSON response types ─────────────────────────────────────────────

    private record SearchDestinationResponse(List<DestinationItem>? Data);
    private record DestinationItem(
        [property: JsonPropertyName("dest_id")] string DestId,
        [property: JsonPropertyName("dest_type")] string? DestType,
        string? Label,
        string? Name
    );
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
