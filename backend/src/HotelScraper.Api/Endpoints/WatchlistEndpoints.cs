using HotelScraper.Api.Data;
using HotelScraper.Api.Dtos;
using HotelScraper.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HotelScraper.Api.Endpoints;

public static class WatchlistEndpoints
{
    public static RouteGroupBuilder MapWatchlistEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api");
        group.RequireAuthorization();

        // GET /api/watchlist → hotel IDs of the current tenant
        group.MapGet("/watchlist", async (AppDbContext db, [FromServices] CurrentTenantService currentTenant) =>
        {
            var ctx = currentTenant.Current;
            if (ctx.TenantId is null)
                return Results.Ok(Array.Empty<int>());

            var ids = await db.WatchlistItems
                .Where(w => w.TenantId == ctx.TenantId.Value)
                .Select(w => w.HotelId)
                .OrderBy(id => id)
                .ToListAsync();

            return Results.Ok(ids);
        });

        // PUT /api/watchlist/{hotelId} → add hotel to the tenant's watchlist
        group.MapPut("/watchlist/{hotelId:int}", async (
            AppDbContext db,
            [FromServices] CurrentTenantService currentTenant,
            int hotelId) =>
        {
            var ctx = currentTenant.Current;
            if (ctx.TenantId is null)
                return Results.Forbid();

            var hotel = await db.Hotels.FindAsync(hotelId);
            if (hotel is null)
                return Results.NotFound(new { detail = "Hotel not found" });

            // Hotel must belong to one of the tenant's cities
            if (!ctx.IsAdmin && !ctx.Cities.Contains(hotel.City))
                return Results.BadRequest(new { detail = "Hotel does not belong to your cities" });

            var exists = await db.WatchlistItems.AnyAsync(w => w.TenantId == ctx.TenantId.Value && w.HotelId == hotelId);
            if (!exists)
            {
                db.WatchlistItems.Add(new WatchlistItem { TenantId = ctx.TenantId.Value, HotelId = hotelId });
                await db.SaveChangesAsync();
            }

            return Results.Ok(new WatchlistAddOut(hotelId, true));
        });

        // DELETE /api/watchlist/{hotelId} → remove hotel from the tenant's watchlist
        group.MapDelete("/watchlist/{hotelId:int}", async (
            AppDbContext db,
            [FromServices] CurrentTenantService currentTenant,
            int hotelId) =>
        {
            var ctx = currentTenant.Current;
            if (ctx.TenantId is null)
                return Results.Forbid();

            var item = await db.WatchlistItems
                .FirstOrDefaultAsync(w => w.TenantId == ctx.TenantId.Value && w.HotelId == hotelId);
            if (item is not null)
            {
                db.WatchlistItems.Remove(item);
                await db.SaveChangesAsync();
            }

            return Results.Ok(new WatchlistRemoveOut(hotelId, true));
        });

        return group;
    }
}
