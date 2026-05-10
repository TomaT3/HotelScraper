using HotelScraper.Api.Configuration;
using HotelScraper.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HotelScraper.Api.Services;

public class PriceFetcherService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly BookingApiService _bookingApi;
    private readonly ScraperOptions _options;
    private readonly ILogger<PriceFetcherService> _logger;

    public PriceFetcherService(
        IDbContextFactory<AppDbContext> dbFactory,
        BookingApiService bookingApi,
        Microsoft.Extensions.Options.IOptions<ScraperOptions> options,
        ILogger<PriceFetcherService> logger)
    {
        _dbFactory = dbFactory;
        _bookingApi = bookingApi;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Get the destination ID for a city from DB or fetch it from the API.
    /// </summary>
    public async Task<string> GetDestIdAsync(AppDbContext db, string city)
    {
        var key = $"dest_id:{city}";
        var setting = await db.Settings.FindAsync(key);
        if (setting is not null)
            return setting.Value;

        // Fallback: check old key format (migration from single-city setup)
        if (city == _options.CityList[0])
        {
            var oldSetting = await db.Settings.FindAsync("dest_id");
            if (oldSetting is not null)
            {
                db.Settings.Add(new Setting { Key = key, Value = oldSetting.Value });
                var oldLabel = await db.Settings.FindAsync("dest_label");
                if (oldLabel is not null)
                    db.Settings.Add(new Setting { Key = $"dest_label:{city}", Value = oldLabel.Value });
                await db.SaveChangesAsync();
                return oldSetting.Value;
            }
        }

        var location = await _bookingApi.SearchLocationAsync(city);
        if (location is null)
            throw new InvalidOperationException($"Could not find destination for city: {city}");

        db.Settings.Add(new Setting { Key = key, Value = location.DestId });
        db.Settings.Add(new Setting { Key = $"dest_label:{city}", Value = location.Label });
        await db.SaveChangesAsync();

        _logger.LogInformation("Stored dest_id={DestId} for {Label}", location.DestId, location.Label);
        return location.DestId;
    }

    /// <summary>
    /// Generate the next N dates starting from tomorrow.
    /// </summary>
    public static List<DateOnly> GetNextDates(int numDates)
    {
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        return Enumerable.Range(0, numDates)
            .Select(i => tomorrow.AddDays(i))
            .ToList();
    }

    /// <summary>
    /// Insert or update a hotel for a specific city, returning its ID.
    /// </summary>
    public async Task<int> UpsertHotelAsync(AppDbContext db, HotelResult hotelData, string city)
    {
        var existing = await db.Hotels
            .FirstOrDefaultAsync(h => h.BookingId == hotelData.BookingId && h.City == city);

        if (existing is not null)
        {
            existing.Name = hotelData.Name;
            existing.Stars = hotelData.Stars;
            existing.ReviewScore = hotelData.ReviewScore;
            existing.ImageUrl = hotelData.ImageUrl;
            existing.DistanceKm = hotelData.DistanceKm;
            await db.SaveChangesAsync();
            return existing.Id;
        }

        var newHotel = new Hotel
        {
            BookingId = hotelData.BookingId,
            Name = hotelData.Name,
            Stars = hotelData.Stars,
            ReviewScore = hotelData.ReviewScore,
            ImageUrl = hotelData.ImageUrl,
            DistanceKm = hotelData.DistanceKm,
            Active = true,
            City = city,
        };
        db.Hotels.Add(newHotel);
        await db.SaveChangesAsync();
        return newHotel.Id;
    }

    /// <summary>
    /// Insert or update a price for a hotel on a specific date.
    /// </summary>
    public async Task SavePriceAsync(AppDbContext db, int hotelId, DateOnly priceDate, double priceEur)
    {
        var existing = await db.Prices
            .FirstOrDefaultAsync(p => p.HotelId == hotelId && p.Date == priceDate);

        if (existing is not null)
        {
            existing.PriceEur = priceEur;
            existing.FetchedAt = DateTime.UtcNow;
        }
        else
        {
            db.Prices.Add(new Price
            {
                HotelId = hotelId,
                Date = priceDate,
                PriceEur = priceEur,
                FetchedAt = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Fetch hotel prices for a specific city for the given dates.
    /// If no dates provided, generates next N dates from tomorrow.
    /// </summary>
    public async Task<FetchResultDto> FetchPricesForDatesAsync(
        string city,
        List<DateOnly>? dates = null,
        int? maxDates = null)
    {
        var errors = new List<string>();
        var totalHotels = 0;
        var totalPrices = 0;

        await using var db = await _dbFactory.CreateDbContextAsync();

        var destId = await GetDestIdAsync(db, city);

        dates ??= GetNextDates(maxDates ?? _options.DatesPerRun);

        if (dates.Count == 0)
        {
            _logger.LogInformation("[{City}] No dates to fetch.", city);
            return new FetchResultDto(0, 0, 0, []);
        }

        _logger.LogInformation(
            "[{City}] Fetching prices for {Count} dates: {First} ... {Last}",
            city, dates.Count, dates[0], dates[^1]
        );

        foreach (var checkDate in dates)
        {
            var checkout = checkDate.AddDays(1);
            try
            {
                var hotels = await _bookingApi.SearchHotelsAsync(destId, checkDate, checkout);
                totalHotels = Math.Max(totalHotels, hotels.Count);

                foreach (var hotelData in hotels)
                {
                    var hotelId = await UpsertHotelAsync(db, hotelData, city);
                    await SavePriceAsync(db, hotelId, checkDate, hotelData.PriceEur);
                    totalPrices++;
                }

                _logger.LogInformation("[{City}] Saved {Count} prices for {Date}", city, hotels.Count, checkDate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{City}] Error fetching {Date}", city, checkDate);
                errors.Add($"{checkDate:yyyy-MM-dd}: {ex.Message}");
            }
        }

        // Update last_fetch timestamp per city
        await using var db2 = await _dbFactory.CreateDbContextAsync();
        var nowStr = DateTime.UtcNow.ToString("o");
        foreach (var key in new[] { $"last_fetch:{city}", "last_fetch" })
        {
            var setting = await db2.Settings.FindAsync(key);
            if (setting is not null)
                setting.Value = nowStr;
            else
                db2.Settings.Add(new Setting { Key = key, Value = nowStr });
        }
        await db2.SaveChangesAsync();

        return new FetchResultDto(dates.Count, totalHotels, totalPrices, errors);
    }

    /// <summary>
    /// Fetch hotel prices for all configured cities sequentially.
    /// </summary>
    public async Task<FetchResultDto> FetchAllCitiesAsync(
        List<DateOnly>? dates = null,
        int? maxDates = null)
    {
        var total = new FetchResultDto(0, 0, 0, []);

        foreach (var city in _options.CityList)
        {
            _logger.LogInformation("Fetching prices for city: {City}", city);
            var result = await FetchPricesForDatesAsync(city, dates, maxDates);
            total = new FetchResultDto(
                total.DatesFetched + result.DatesFetched,
                total.HotelsFound + result.HotelsFound,
                total.PricesSaved + result.PricesSaved,
                [.. total.Errors, .. result.Errors.Select(e => $"[{city}] {e}")]
            );
        }

        return total;
    }
}

public record FetchResultDto(int DatesFetched, int HotelsFound, int PricesSaved, List<string> Errors);
