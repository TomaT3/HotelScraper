using HotelScraper.Api.Configuration;
using HotelScraper.Api.Data;
using HotelScraper.Api.Dtos;
using HotelScraper.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace HotelScraper.Api.Endpoints;

public static class PricesEndpoints
{
    public static RouteGroupBuilder MapPriceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api");

        group.MapGet("/prices", async (
            AppDbContext db,
            string? hotel_ids,
            DateOnly? from,
            DateOnly? to,
            string? room_type) =>
        {
            var roomType = room_type ?? "double";

            var hotelQuery = db.Hotels.AsQueryable();
            if (!string.IsNullOrWhiteSpace(hotel_ids))
            {
                var ids = hotel_ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(s => int.TryParse(s, out _))
                    .Select(int.Parse)
                    .ToList();
                hotelQuery = hotelQuery.Where(h => ids.Contains(h.Id));
            }

            var hotels = await hotelQuery.OrderBy(h => h.Name).ToListAsync();
            var result = new List<HotelPrices>();

            foreach (var hotel in hotels)
            {
                var priceQuery = db.Prices.Where(p => p.HotelId == hotel.Id && p.RoomType == roomType);
                if (from.HasValue)
                    priceQuery = priceQuery.Where(p => p.Date >= from.Value);
                if (to.HasValue)
                    priceQuery = priceQuery.Where(p => p.Date <= to.Value);

                var prices = await priceQuery.OrderBy(p => p.Date).ToListAsync();
                if (prices.Count > 0)
                {
                    result.Add(new HotelPrices(
                        hotel.Id,
                        hotel.Name,
                        hotel.Stars,
                        prices.Select(p => new PricePoint(p.Date, p.PriceEur, p.RoomType)).ToList()
                    ));
                }
            }

            return Results.Ok(result);
        });

        group.MapGet("/status", async (
            AppDbContext db,
            string? city,
            [FromServices] ScraperOptions options,
            [FromServices] ISchedulerFactory? schedulerFactory) =>
        {
            var scheduler = schedulerFactory is not null ? await schedulerFactory.GetScheduler() : null;

            var hotelQuery = db.Hotels.AsQueryable();
            if (!string.IsNullOrWhiteSpace(city))
                hotelQuery = hotelQuery.Where(h => h.City == city);

            var totalHotels = await hotelQuery.CountAsync();
            var activeHotels = await hotelQuery.CountAsync(h => h.Active);

            int totalPrices;
            int datesCovered;
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            if (!string.IsNullOrWhiteSpace(city))
            {
                totalPrices = await db.Prices
                    .Where(p => db.Hotels.Where(h => h.City == city).Select(h => h.Id).Contains(p.HotelId))
                    .CountAsync();
                datesCovered = await db.Prices
                    .Where(p => p.Date >= today)
                    .Where(p => db.Hotels.Where(h => h.City == city).Select(h => h.Id).Contains(p.HotelId))
                    .Select(p => p.Date)
                    .Distinct()
                    .CountAsync();
            }
            else
            {
                totalPrices = await db.Prices.CountAsync();
                datesCovered = await db.Prices
                    .Where(p => p.Date >= today)
                    .Select(p => p.Date)
                    .Distinct()
                    .CountAsync();
            }

            var datesTotal = options.DatesPerRun;
            var coveragePct = datesTotal > 0
                ? Math.Round((double)datesCovered / datesTotal * 100.0, 1)
                : 0;

            // Last fetch time
            var lastFetchKey = !string.IsNullOrWhiteSpace(city) ? $"last_fetch:{city}" : "last_fetch";
            var lastFetchSetting = await db.Settings.FindAsync(lastFetchKey);
            DateTime? lastFetch = null;
            if (lastFetchSetting is not null && DateTime.TryParse(lastFetchSetting.Value, out var parsed))
                lastFetch = parsed;

            // Next scheduled run
            DateTime? nextRun = null;
            if (scheduler is not null && !scheduler.IsShutdown)
            {
                var jobKey = new JobKey("daily_fetch");
                var triggers = await scheduler.GetTriggersOfJob(jobKey);
                var nextFire = triggers.Select(t => t.GetNextFireTimeUtc())
                    .Where(dto => dto.HasValue)
                    .Select(dto => dto!.Value.UtcDateTime)
                    .FirstOrDefault();
                if (nextFire != default)
                    nextRun = nextFire;
            }

            return Results.Ok(new StatusOut(
                city,
                totalHotels,
                activeHotels,
                totalPrices,
                datesCovered,
                datesTotal,
                coveragePct,
                lastFetch,
                scheduler is not null && !scheduler.IsShutdown,
                nextRun
            ));
        });

        group.MapPost("/fetch", async (
            PriceFetcherService fetcher,
            string? city,
            int? max_dates) =>
        {
            var md = max_dates ?? 15; // Will be overridden by service default if null/not passed

            FetchResultDto result;
            if (!string.IsNullOrWhiteSpace(city))
                result = await fetcher.FetchPricesForDatesAsync(city, maxDates: max_dates);
            else
                result = await fetcher.FetchAllCitiesAsync(maxDates: max_dates);

            return Results.Ok(new FetchResult(
                result.DatesFetched,
                result.HotelsFound,
                result.PricesSaved,
                result.Errors
            ));
        });

        return group;
    }
}
