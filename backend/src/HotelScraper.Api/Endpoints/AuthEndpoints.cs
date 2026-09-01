using System.Security.Claims;
using HotelScraper.Api.Data;
using HotelScraper.Api.Dtos;
using HotelScraper.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace HotelScraper.Api.Endpoints;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/login", async (
            LoginRequest body,
            AuthService auth,
            AppDbContext db,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Email) || string.IsNullOrWhiteSpace(body.Password))
                return Results.Unauthorized();

            var user = await auth.ValidateAsync(body.Email.Trim(), body.Password, ct);
            if (user is null)
                return Results.Unauthorized();

            var claims = await auth.BuildClaimsAsync(user, ct);
            var identity = new ClaimsIdentity(claims, "Cookie");
            var principal = new ClaimsPrincipal(identity);

            await ctx.SignInAsync(principal);

            var tenantName = user.TenantId.HasValue
                ? await db.Tenants.Where(t => t.Id == user.TenantId.Value).Select(t => t.Name).FirstOrDefaultAsync(ct)
                : null;

            return Results.Ok(new AuthUserOut(
                user.Email,
                user.Role,
                user.TenantId,
                tenantName,
                claims.FirstOrDefault(c => c.Type == "city")?.Value
            ));
        });

        group.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync();
            return Results.Ok(new { ok = true });
        });

        group.MapGet("/me", async (AppDbContext db, ClaimsPrincipal principal, CancellationToken ct) =>
        {
            var userIdClaim = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userIdClaim is null || !int.TryParse(userIdClaim, out var userId))
                return Results.Unauthorized();

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null || !user.IsActive)
                return Results.Unauthorized();

            string? tenantName = null;
            if (user.TenantId.HasValue)
                tenantName = await db.Tenants.Where(t => t.Id == user.TenantId.Value).Select(t => t.Name).FirstOrDefaultAsync(ct);

            return Results.Ok(new AuthUserOut(
                user.Email,
                user.Role,
                user.TenantId,
                tenantName,
                user.TenantId.HasValue
                    ? await db.Tenants.Where(t => t.Id == user.TenantId.Value).Select(t => t.City).FirstOrDefaultAsync(ct)
                    : null
            ));
        }).RequireAuthorization();

        return group;
    }
}
