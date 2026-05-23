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

        // Check cached value
        var setting = await db.Settings.FindAsync(key);
        if (setting is not null && IsValidDestId(setting.Value))
        {
            _logger.LogDebug("Using cached dest_id={DestId} for {City}", setting.Value, city);
            return setting.Value;
        }

        if (setting is not null)
        {
            _logger.LogWarning("Cached dest_id for {City} is invalid ({Value}), re-fetching from API", city, setting.Value);
        }

        // Fallback: check old key format (migration from single-city setup)
        if (city == _options.CityList[0])
        {
            var oldSetting = await db.Settings.FindAsync("dest_id");
            if (oldSetting is not null && IsValidDestId(oldSetting.Value))
            {
                db.Settings.Add(new Setting { Key = key, Value = oldSetting.Value });
                var oldLabel = await db.Settings.FindAsync("dest_label");
                if (oldLabel is not null)
                    db.Settings.Add(new Setting { Key = $"dest_label:{city}", Value = oldLabel.Value });
                await db.SaveChangesAsync();
                _logger.LogInformation("Migrated old dest_id={DestId} to {City}", oldSetting.Value, city);
                return oldSetting.Value;
            }
        }

        // Fetch from API
        var location = await _bookingApi.SearchLocationAsync(city);
        if (location is null)
            throw new InvalidOperationException($"Could not find destination for city: {city}");

        // Update or insert the cached dest_id value
        if (setting is not null)
        {
            setting.Value = location.DestId;
        }
        else
        {
            db.Settings.Add(new Setting { Key = key, Value = location.DestId });
        }

        // Update or insert the cached label value
        var labelKey = $"dest_label:{city}";
        var labelSetting = await db.Settings.FindAsync(labelKey);
        if (labelSetting is not null)
        {
            labelSetting.Value = location.Label;
        }
        else
        {
            db.Settings.Add(new Setting { Key = labelKey, Value = location.Label });
        }

        await db.SaveChangesAsync();

        _logger.LogInformation("Stored dest_id={DestId} for {Label}", location.DestId, location.Label);
        return location.DestId;
    }

    /// <summary>
    /// Returns true if the dest_id looks valid (non-empty and not "0").
    /// </summary>
    private static bool IsValidDestId(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && value != "0";
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
    /// Insert or update a price for a hotel on a specific date and room type.
    /// Handles UNIQUE constraint violations gracefully (safety net for partially migrated databases).
    /// </summary>
    public async Task SavePriceAsync(AppDbContext db, int hotelId, DateOnly priceDate, double priceEur, string roomType)
    {
        var existing = await db.Prices
            .FirstOrDefaultAsync(p => p.HotelId == hotelId && p.Date == priceDate && p.RoomType == roomType);

        if (existing is not null)
        {
            existing.PriceEur = priceEur;
            existing.FetchedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return;
        }

        db.Prices.Add(new Price
        {
            HotelId = hotelId,
            Date = priceDate,
            PriceEur = priceEur,
            RoomType = roomType,
            FetchedAt = DateTime.UtcNow,
        });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
            when (ex.InnerException is Microsoft.Data.Sqlite.SqliteException sqlEx
                  && sqlEx.Message.Contains("UNIQUE constraint failed"))
        {
            // Constraint violation — the row already exists (likely a mismatched DB schema).
            // Detach the failed entity and update the existing row instead.
            var entry = db.ChangeTracker.Entries<Price>()
                .FirstOrDefault(e => e.Entity.HotelId == hotelId && e.Entity.Date == priceDate && e.Entity.RoomType == roomType);
            if (entry is not null)
                entry.State = Microsoft.EntityFrameworkCore.EntityState.Detached;

            var retry = await db.Prices
                .FirstOrDefaultAsync(p => p.HotelId == hotelId && p.Date == priceDate && p.RoomType == roomType);
            if (retry is not null)
            {
                retry.PriceEur = priceEur;
                retry.FetchedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }
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

            // Fetch double room prices (adults=2)
            try
            {
                var hotelsDouble = await _bookingApi.SearchHotelsAsync(destId, checkDate, checkout, adults: 2);
                totalHotels = Math.Max(totalHotels, hotelsDouble.Count);

                foreach (var hotelData in hotelsDouble)
                {
                    var hotelId = await UpsertHotelAsync(db, hotelData, city);
                    await SavePriceAsync(db, hotelId, checkDate, hotelData.PriceEur, "double");
                    totalPrices++;
                }

                _logger.LogInformation("[{City}] Saved {Count} double-room prices for {Date}", city, hotelsDouble.Count, checkDate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{City}] Error fetching double-room prices for {Date}", city, checkDate);
                errors.Add($"{checkDate:yyyy-MM-dd} (double): {ex.Message}");
            }

            // Fetch single room prices (adults=1)
            try
            {
                var hotelsSingle = await _bookingApi.SearchHotelsAsync(destId, checkDate, checkout, adults: 1);
                totalHotels = Math.Max(totalHotels, hotelsSingle.Count);

                foreach (var hotelData in hotelsSingle)
                {
                    var hotelId = await UpsertHotelAsync(db, hotelData, city);
                    await SavePriceAsync(db, hotelId, checkDate, hotelData.PriceEur, "single");
                    totalPrices++;
                }

                _logger.LogInformation("[{City}] Saved {Count} single-room prices for {Date}", city, hotelsSingle.Count, checkDate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{City}] Error fetching single-room prices for {Date}", city, checkDate);
                errors.Add($"{checkDate:yyyy-MM-dd} (single): {ex.Message}");
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
