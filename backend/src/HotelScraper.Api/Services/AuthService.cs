using System.Security.Claims;
using HotelScraper.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HotelScraper.Api.Services;

public class AuthService
{
    private readonly AppDbContext _db;

    public AuthService(AppDbContext db) => _db = db;

    public string HashPassword(string password) => BCrypt.Net.BCrypt.HashPassword(password);

    public bool VerifyPassword(string password, string passwordHash)
        => BCrypt.Net.BCrypt.Verify(password, passwordHash);

    public async Task<AppUser?> ValidateAsync(string email, string password, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null || !user.IsActive)
            return null;

        if (!VerifyPassword(password, user.PasswordHash))
            return null;

        return user;
    }

    public async Task<List<Claim>> BuildClaimsAsync(AppUser user, CancellationToken ct = default)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Email),
            new(ClaimTypes.Role, user.Role),
        };

        if (user.TenantId.HasValue)
        {
            claims.Add(new Claim("tenant_id", user.TenantId.Value.ToString()));

            // Multi-city: one "city" claim per city the tenant is assigned to.
            var cities = await _db.TenantCities
                .Where(tc => tc.TenantId == user.TenantId.Value)
                .Select(tc => tc.City)
                .ToListAsync(ct);

            foreach (var city in cities)
                claims.Add(new Claim("city", city));
        }

        return claims;
    }
}
