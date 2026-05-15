using HotelScraper.Api.Configuration;
using HotelScraper.Api.Data;
using HotelScraper.Api.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HotelScraper.Api.Endpoints;

public static class HotelsEndpoints
{
    public static RouteGroupBuilder MapHotelEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api");

        group.MapGet("/cities", async (AppDbContext db, [FromServices] ScraperOptions options) =>
        {
            var result = new List<CityOut>();
            foreach (var city in options.CityList)
            {
                var label = await db.Settings
                    .Where(s => s.Key == $"dest_label:{city}")
                    .Select(s => s.Value)
                    .FirstOrDefaultAsync();
                result.Add(new CityOut(city, label));
            }
            return Results.Ok(result);
        });

        group.MapGet("/hotels", async (AppDbContext db, string city) =>
        {
            var hotels = await db.Hotels
                .Where(h => h.City == city)
                .OrderBy(h => h.Name)
                .Select(h => new HotelOut(
                    h.Id, h.BookingId, h.Name, h.Address, h.Stars,
                    h.ReviewScore, h.ImageUrl, h.DistanceKm, h.Active, h.City
                ))
                .ToListAsync();
            return Results.Ok(hotels);
        });

        group.MapPatch("/hotels/{id:int}", async (AppDbContext db, int id, HotelUpdate body) =>
        {
            var hotel = await db.Hotels.FindAsync(id);
            if (hotel is null)
                return Results.NotFound(new { detail = "Hotel not found" });

            if (body.Active.HasValue)
                hotel.Active = body.Active.Value;

            await db.SaveChangesAsync();
            return Results.Ok(new HotelOut(
                hotel.Id, hotel.BookingId, hotel.Name, hotel.Address, hotel.Stars,
                hotel.ReviewScore, hotel.ImageUrl, hotel.DistanceKm, hotel.Active, hotel.City
            ));
        });

        group.MapGet("/version", () =>
        {
            var version = Environment.GetEnvironmentVariable("APP_VERSION");
            if (string.IsNullOrWhiteSpace(version) || version == "unknown")
                version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "unknown";
            return Results.Ok(new VersionInfo(version));
        });

        group.MapGet("/config", ([FromServices] ScraperOptions options) =>
            Results.Ok(new ConfigResponse(options.DatesPerRun))
        );

        return group;
    }
}
