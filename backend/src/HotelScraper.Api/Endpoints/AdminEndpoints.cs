using HotelScraper.Api.Data;
using HotelScraper.Api.Dtos;
using HotelScraper.Api.Services;
using Microsoft.EntityFrameworkCore;

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
                .Select(t => new TenantOut(t.Id, t.Name, t.City, t.IsActive, t.CreatedAt))
                .ToListAsync();
            return Results.Ok(tenants);
        });

        group.MapPost("/tenants", async (TenantIn body, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name) || string.IsNullOrWhiteSpace(body.City))
                return Results.BadRequest(new { detail = "name and city are required" });

            var tenant = new Tenant
            {
                Name = body.Name.Trim(),
                City = body.City.Trim(),
                IsActive = body.IsActive ?? true,
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();

            return Results.Ok(new TenantOut(tenant.Id, tenant.Name, tenant.City, tenant.IsActive, tenant.CreatedAt));
        });

        group.MapPatch("/tenants/{id:int}", async (int id, TenantIn body, AppDbContext db) =>
        {
            var tenant = await db.Tenants.FindAsync(id);
            if (tenant is null)
                return Results.NotFound(new { detail = "Tenant not found" });

            if (!string.IsNullOrWhiteSpace(body.Name))
                tenant.Name = body.Name.Trim();
            if (!string.IsNullOrWhiteSpace(body.City))
                tenant.City = body.City.Trim();
            if (body.IsActive.HasValue)
                tenant.IsActive = body.IsActive.Value;

            await db.SaveChangesAsync();
            return Results.Ok(new TenantOut(tenant.Id, tenant.Name, tenant.City, tenant.IsActive, tenant.CreatedAt));
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
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true, id = user.Id });
        });

        return group;
    }
}
