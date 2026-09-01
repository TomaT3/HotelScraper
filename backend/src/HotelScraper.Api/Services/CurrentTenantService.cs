using System.Security.Claims;

namespace HotelScraper.Api.Services;

public record TenantContext(int? TenantId, string? City, bool IsAdmin);

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
                return new TenantContext(null, null, false);

            var isAdmin = user.IsInRole("admin");
            var tenantId = int.TryParse(user.FindFirstValue("tenant_id"), out var t) ? t : (int?)null;
            var city = user.FindFirstValue("city");
            return new TenantContext(tenantId, city, isAdmin);
        }
    }
}
