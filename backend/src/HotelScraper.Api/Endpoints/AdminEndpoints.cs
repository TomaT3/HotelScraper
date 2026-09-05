using HotelScraper.Api.Configuration;
using HotelScraper.Api.Data;
using HotelScraper.Api.Dtos;
using HotelScraper.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HotelScraper.Api.Endpoints;

public static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin");
        group.RequireAuthorization("admin");

        // ── Tenants ──────────────────────────────────────────────────────

        group.MapGet("/tenants", async (AppDbContext db) =>
        {
            var tenants = await db.Tenants
                .OrderBy(t => t.Name)
                .ToListAsync();

            var cityGroups = await db.TenantCities
                .GroupBy(tc => tc.TenantId)
                .ToDictionaryAsync(g => g.Key, g => g.Select(tc => tc.City).OrderBy(c => c).ToList());

            var result = tenants.Select(t => new TenantOut(
                t.Id, t.Name,
                cityGroups.TryGetValue(t.Id, out var cities) ? cities : new List<string>(),
                t.IsActive, t.CreatedAt)).ToList();

            return Results.Ok(result);
        });

        group.MapPost("/tenants", async (TenantIn body, AppDbContext db, [FromServices] ScraperOptions options) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name) || body.Cities is null || body.Cities.Count == 0)
                return Results.BadRequest(new { detail = "name and at least one city are required" });

            var cities = body.Cities
                .Select(c => c.Trim())
                .Where(c => c.Length > 0)
                .Distinct()
                .ToList();

            if (cities.Count == 0)
                return Results.BadRequest(new { detail = "name and at least one city are required" });

            var unknown = cities.Where(c => !options.CityList.Contains(c)).ToList();
            if (unknown.Count > 0)
                return Results.BadRequest(new { detail = $"unknown city: {string.Join(", ", unknown)}" });

            var tenant = new Tenant
            {
                Name = body.Name.Trim(),
                IsActive = body.IsActive ?? true,
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();

            db.TenantCities.AddRange(cities.Select(c => new TenantCity { TenantId = tenant.Id, City = c }));
            await db.SaveChangesAsync();

            return Results.Ok(new TenantOut(tenant.Id, tenant.Name, cities, tenant.IsActive, tenant.CreatedAt));
        });

        group.MapPatch("/tenants/{id:int}", async (int id, TenantIn body, AppDbContext db, [FromServices] ScraperOptions options) =>
        {
            var tenant = await db.Tenants.FindAsync(id);
            if (tenant is null)
                return Results.NotFound(new { detail = "Tenant not found" });

            if (!string.IsNullOrWhiteSpace(body.Name))
                tenant.Name = body.Name.Trim();
            if (body.IsActive.HasValue)
                tenant.IsActive = body.IsActive.Value;

            // Cities are replaced as a whole when provided
            if (body.Cities is not null)
            {
                var cities = body.Cities
                    .Select(c => c.Trim())
                    .Where(c => c.Length > 0)
                    .Distinct()
                    .ToList();

                if (cities.Count == 0)
                    return Results.BadRequest(new { detail = "at least one city is required" });

                var unknown = cities.Where(c => !options.CityList.Contains(c)).ToList();
                if (unknown.Count > 0)
                    return Results.BadRequest(new { detail = $"unknown city: {string.Join(", ", unknown)}" });

                var existing = await db.TenantCities.Where(tc => tc.TenantId == tenant.Id).ToListAsync();
                db.TenantCities.RemoveRange(existing);
                db.TenantCities.AddRange(cities.Select(c => new TenantCity { TenantId = tenant.Id, City = c }));
            }

            await db.SaveChangesAsync();

            var tenantCities = await db.TenantCities
                .Where(tc => tc.TenantId == tenant.Id)
                .Select(tc => tc.City)
                .OrderBy(c => c)
                .ToListAsync();

            return Results.Ok(new TenantOut(tenant.Id, tenant.Name, tenantCities, tenant.IsActive, tenant.CreatedAt));
        });

        // ── Users ────────────────────────────────────────────────────────

        group.MapGet("/users", async (AppDbContext db) =>
        {
            var users = await db.Users
                .OrderBy(u => u.Email)
                .Select(u => new UserOut(
                    u.Id, u.Email, u.TenantId,
                    u.TenantId.HasValue ? db.Tenants.Where(t => t.Id == u.TenantId.Value).Select(t => t.Name).FirstOrDefault() : null,
                    u.Role, u.IsActive, u.CreatedAt))
                .ToListAsync();
            return Results.Ok(users);
        });

        group.MapPost("/users", async (UserIn body, AppDbContext db, AuthService auth) =>
        {
            if (string.IsNullOrWhiteSpace(body.Email) || string.IsNullOrWhiteSpace(body.Password))
                return Results.BadRequest(new { detail = "email and password are required" });

            var role = string.IsNullOrWhiteSpace(body.Role) ? "user" : body.Role.Trim().ToLowerInvariant();
            if (role is not ("admin" or "user"))
                return Results.BadRequest(new { detail = "role must be 'admin' or 'user'" });

            if (role == "user" && !body.TenantId.HasValue)
                return Results.BadRequest(new { detail = "tenant_id is required for role 'user'" });

            if (body.TenantId.HasValue)
            {
                var tenantExists = await db.Tenants.AnyAsync(t => t.Id == body.TenantId.Value && t.IsActive);
                if (!tenantExists)
                    return Results.BadRequest(new { detail = "tenant not found or inactive" });
            }

            var email = body.Email.Trim().ToLowerInvariant();
            var existing = await db.Users.AnyAsync(u => u.Email == email);
            if (existing)
                return Results.Conflict(new { detail = "email already exists" });

            var user = new AppUser
            {
                Email = email,
                PasswordHash = auth.HashPassword(body.Password),
                TenantId = body.TenantId,
                Role = role,
                IsActive = body.IsActive ?? true,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();

            return Results.Ok(new UserOut(
                user.Id, user.Email, user.TenantId,
                user.TenantId.HasValue ? await db.Tenants.Where(t => t.Id == user.TenantId.Value).Select(t => t.Name).FirstOrDefaultAsync() : null,
                user.Role, user.IsActive, user.CreatedAt));
        });

        group.MapPost("/users/{id:int}/reset-password", async (int id, ResetPasswordIn body, AppDbContext db, AuthService auth) =>
        {
            if (string.IsNullOrWhiteSpace(body.Password))
                return Results.BadRequest(new { detail = "password is required" });

            var user = await db.Users.FindAsync(id);
            if (user is null)
                return Results.NotFound(new { detail = "User not found" });

            user.PasswordHash = auth.HashPassword(body.Password);
            user.PasswordChangedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true, id = user.Id });
        });

        group.MapPatch("/users/{id:int}", async (int id, UserPatchIn body, AppDbContext db, ClaimsPrincipal principal) =>
        {
            var user = await db.Users.FindAsync(id);
            if (user is null)
                return Results.NotFound(new { detail = "User not found" });

            // Self-protection: an admin must never lock themselves out.
            var selfId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            var isSelf = int.TryParse(selfId, out var selfIdValue) && selfIdValue == user.Id;

            if (!string.IsNullOrWhiteSpace(body.Role))
            {
                var role = body.Role.Trim().ToLowerInvariant();
                if (role is not ("admin" or "user"))
                    return Results.BadRequest(new { detail = "role must be 'admin' or 'user'" });

                if (role == "user" && !body.TenantId.HasValue)
                    return Results.BadRequest(new { detail = "tenant_id is required for role 'user'" });

                // Self-protection: an admin must never demote themselves — if they
                // are the last admin, nobody could restore the role (same lockout
                // as self-deactivation).
                if (isSelf && user.Role == "admin" && role == "user")
                    return Results.BadRequest(new { detail = "you cannot demote your own account" });

                user.Role = role;
            }

            if (body.TenantId.HasValue)
            {
                var tenantExists = await db.Tenants.AnyAsync(t => t.Id == body.TenantId.Value && t.IsActive);
                if (!tenantExists)
                    return Results.BadRequest(new { detail = "tenant not found or inactive" });
                user.TenantId = body.TenantId;
            }

            if (body.IsActive.HasValue)
            {
                if (!body.IsActive.Value && isSelf)
                    return Results.BadRequest(new { detail = "you cannot deactivate your own account" });
                user.IsActive = body.IsActive.Value;
            }

            await db.SaveChangesAsync();

            return Results.Ok(new UserOut(
                user.Id, user.Email, user.TenantId,
                user.TenantId.HasValue ? await db.Tenants.Where(t => t.Id == user.TenantId.Value).Select(t => t.Name).FirstOrDefaultAsync() : null,
                user.Role, user.IsActive, user.CreatedAt));
        });

        return group;
    }
}
