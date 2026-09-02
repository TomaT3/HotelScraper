namespace HotelScraper.Api.Services;

/// <summary>
/// Well-known application claim types stored in the auth cookie.
/// The string VALUES are persisted in cookies — do not rename them,
/// existing sessions would silently break tenant scoping.
/// </summary>
public static class AuthClaimTypes
{
    public const string TenantId = "tenant_id";
    public const string City = "city";
}
