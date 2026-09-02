using System.Security.Claims;

namespace HotelScraper.Api.Services;

public record TenantContext(int? TenantId, IReadOnlyList<string> Cities, bool IsAdmin);

public class CurrentTenantService
{
    private readonly IHttpContextAccessor _http;

    public CurrentTenantService(IHttpContextAccessor http) => _http = http;

    public TenantContext Current
    {
        get
        {
            var user = _http.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
                return new TenantContext(null, Array.Empty<string>(), false);

            var isAdmin = user.IsInRole("admin");
            var tenantId = int.TryParse(user.FindFirstValue(AuthClaimTypes.TenantId), out var t) ? t : (int?)null;
            var cities = user.FindAll(AuthClaimTypes.City).Select(c => c.Value).ToList();
            return new TenantContext(tenantId, cities, isAdmin);
        }
    }
}
