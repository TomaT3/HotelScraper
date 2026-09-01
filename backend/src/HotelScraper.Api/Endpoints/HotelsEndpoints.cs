using HotelScraper.Api.Configuration;
using HotelScraper.Api.Data;
using HotelScraper.Api.Dtos;
using HotelScraper.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HotelScraper.Api.Endpoints;

public static class HotelsEndpoints
{
    public static RouteGroupBuilder MapHotelEndpoints(this IEndpointRouteBuilder app)
    {
        // Public meta endpoints (no auth required — used by the frontend pre-login)
        var publicGroup = app.MapGroup("/api");
        publicGroup.MapGet("/version", () =>
        {
            var version = Environment.GetEnvironmentVariable("APP_VERSION");
            if (string.IsNullOrWhiteSpace(version) || version == "unknown")
                version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "unknown";
            return Results.Ok(new VersionInfo(version));
        });

        publicGroup.MapGet("/config", ([FromServices] ScraperOptions options) =>
            Results.Ok(new ConfigResponse(options.DatesPerRun))
        );

        var group = app.MapGroup("/api");
        group.RequireAuthorization();

        group.MapGet("/cities", async (AppDbContext db, [FromServices] ScraperOptions options, [FromServices] CurrentTenantService currentTenant) =>
        {
            var ctx = currentTenant.Current;

            IEnumerable<string> cities = options.CityList;
            if (!ctx.IsAdmin)
                cities = ctx.City is not null ? new[] { ctx.City } : [];

            var result = new List<CityOut>();
            foreach (var city in cities)
            {
                var label = await db.Settings
                    .Where(s => s.Key == $"dest_label:{city}")
                    .Select(s => s.Value)
                    .FirstOrDefaultAsync();
                result.Add(new CityOut(city, label));
            }
            return Results.Ok(result);
        });

        group.MapGet("/hotels", async (AppDbContext db, string? city, [FromServices] CurrentTenantService currentTenant) =>
        {
            var ctx = currentTenant.Current;

            // Users always see only their own city; admins may choose via ?city= (all if omitted)
            var effectiveCity = ctx.IsAdmin ? city : ctx.City;

            var query = db.Hotels.AsQueryable();
            if (!string.IsNullOrWhiteSpace(effectiveCity))
                query = query.Where(h => h.City == effectiveCity);

            var hotels = await query
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
        }).RequireAuthorization("admin");

        return group;
    }
}
