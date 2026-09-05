using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using HotelScraper.Api;
using HotelScraper.Api.Configuration;
using HotelScraper.Api.Data;
using HotelScraper.Api.Dtos;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using Xunit;

namespace HotelScraper.Tests;

public class TestAuthHandlerOptions : AuthenticationSchemeOptions { }

/// <summary>
/// Authenticates requests based on headers set by <see cref="CustomWebApplicationFactory.CreateClientAs"/>.
/// Requests without the X-Test-Auth header fall back to the real cookie scheme,
/// so login round-trips (login → me → logout) work with real credentials.
/// </summary>
public class TestAuthHandler : AuthenticationHandler<TestAuthHandlerOptions>
{
    public const string SchemeName = "Test";
    public const string AuthHeader = "X-Test-Auth";
    public const string RoleHeader = "X-Test-Role";
    public const string TenantIdHeader = "X-Test-TenantId";
    public const string CityHeader = "X-Test-City";
    public const string UserIdHeader = "X-Test-UserId";

    public TestAuthHandler(
        IOptionsMonitor<TestAuthHandlerOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey(AuthHeader))
        {
            // No test identity requested → use the real cookie scheme (if a session exists)
            return await Context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Request.Headers[UserIdHeader].FirstOrDefault() ?? "1"),
            new(ClaimTypes.Name, "test@example.com"),
        };

        if (Request.Headers.TryGetValue(RoleHeader, out var role) && role.Count > 0)
            claims.Add(new Claim(ClaimTypes.Role, role[0]!));
        if (Request.Headers.TryGetValue(TenantIdHeader, out var tenantId) && tenantId.Count > 0)
            claims.Add(new Claim("tenant_id", tenantId[0]!));
        if (Request.Headers.TryGetValue(CityHeader, out var city) && city.Count > 0)
        {
            // Comma-separated header → one "city" claim per city (multi-city tenants)
            foreach (var c in city[0]!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                claims.Add(new Claim("city", c));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return AuthenticateResult.Success(ticket);
    }
}

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;

    public CustomWebApplicationFactory()
    {
        _connection = new SqliteConnection("Data Source=testdb;Mode=Memory;Cache=Shared");
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove ALL Quartz services to avoid scheduler initialization in tests
            var quartzDescriptors = services
                .Where(d => d.ServiceType.Namespace?.StartsWith("Quartz") == true
                         || d.ImplementationType?.Namespace?.StartsWith("Quartz") == true)
                .ToList();

            foreach (var d in quartzDescriptors)
                services.Remove(d);

            // Remove Quartz hosted service
            var hostedServiceDescriptors = services
                .Where(d => d.ServiceType == typeof(IHostedService))
                .ToList();

            foreach (var d in hostedServiceDescriptors)
                services.Remove(d);

            // Replace DbContext registration with shared in-memory SQLite
            services.RemoveAll<IDbContextFactory<AppDbContext>>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();

            services.AddDbContextFactory<AppDbContext>(options =>
                options.UseSqlite(_connection));

            // Override options for testing
            services.Configure<ScraperOptions>(opts =>
            {
                opts.RapidApiKey = "test-api-key-12345";
                opts.SearchCities = "Stuttgart,Tübingen";
                opts.DatesPerRun = 5;
                opts.FetchHour = 3;
            });

            // Test authentication: test handler as default, cookie scheme still available
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            }).AddScheme<TestAuthHandlerOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    /// <summary>
    /// Creates a client authenticated as the given role/tenant/cities via the test handler.
    /// Pass role "admin" without tenant/cities for a global admin.
    /// </summary>
    public HttpClient CreateClientAs(string role = "user", int? tenantId = null, IEnumerable<string>? cities = null, int? userId = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeader, "true");
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, role);
        if (tenantId.HasValue)
            client.DefaultRequestHeaders.Add(TestAuthHandler.TenantIdHeader, tenantId.Value.ToString());
        if (cities is not null && cities.Any())
            client.DefaultRequestHeaders.Add(TestAuthHandler.CityHeader, string.Join(",", cities));
        if (userId.HasValue)
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.Value.ToString());
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Close();
            _connection.Dispose();
        }
    }
}

public class ApiEndpointsTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private AppDbContext _db = null!;

    public ApiEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        var dbFactory = _factory.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        _db = await dbFactory.CreateDbContextAsync();
        await _db.Database.EnsureCreatedAsync();
        await SeedDataAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        _db.Dispose();
    }

    private async Task SeedDataAsync()
    {
        _db.Hotels.AddRange(
            new Hotel { BookingId = "1001", Name = "Stuttgart Grand Hotel", City = "Stuttgart", Stars = 5, Active = true },
            new Hotel { BookingId = "1002", Name = "Hotel am Schlossgarten", City = "Stuttgart", Stars = 4, Active = true },
            new Hotel { BookingId = "3001", Name = "Hotel am Neckar Tübingen", City = "Tübingen", Stars = 4, Active = true },
            new Hotel { BookingId = "2001", Name = "Berlin Central Hotel", City = "Berlin", Stars = 4, Active = false }
        );

        var tenantStuttgart = new Tenant { Name = "Stuttgart Hotels GmbH" };
        var tenantMulti = new Tenant { Name = "Stuttgart-Tübingen Gruppe" };
        _db.Tenants.AddRange(tenantStuttgart, tenantMulti);
        await _db.SaveChangesAsync();

        _db.TenantCities.AddRange(
            new TenantCity { TenantId = tenantStuttgart.Id, City = "Stuttgart" },
            new TenantCity { TenantId = tenantMulti.Id, City = "Stuttgart" },
            new TenantCity { TenantId = tenantMulti.Id, City = "Tübingen" }
        );

        _db.Users.Add(new AppUser
        {
            Email = "admin@test.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("AdminPass1!"),
            Role = "admin",
        });
        _db.Users.Add(new AppUser
        {
            Email = "user@stuttgart.test",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("UserPass1!"),
            Role = "user",
            TenantId = tenantStuttgart.Id,
        });

        _db.Settings.Add(new Setting { Key = "dest_label:Stuttgart", Value = "Stuttgart, Germany" });
        _db.Settings.Add(new Setting { Key = "dest_label:Tübingen", Value = "Tübingen, Germany" });

        await _db.SaveChangesAsync();

        // Seed prices for Stuttgart and Tübingen hotels
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var hotels = await _db.Hotels.Where(h => h.City == "Stuttgart" || h.City == "Tübingen").ToListAsync();
        foreach (var hotel in hotels)
        {
            for (int i = 0; i < 15; i++)
            {
                _db.Prices.Add(new Price
                {
                    HotelId = hotel.Id,
                    Date = today.AddDays(i + 1),
                    PriceEur = 100 + i * 5,
                    RoomType = "double",
                    FetchedAt = DateTime.UtcNow
                });
                _db.Prices.Add(new Price
                {
                    HotelId = hotel.Id,
                    Date = today.AddDays(i + 1),
                    PriceEur = 70 + i * 3,
                    RoomType = "single",
                    FetchedAt = DateTime.UtcNow
                });
            }
        }
        await _db.SaveChangesAsync();
    }

    private HttpClient UserClient() => _factory.CreateClientAs("user", tenantId: 1, cities: ["Stuttgart"]);
    private HttpClient MultiCityClient() => _factory.CreateClientAs("user", tenantId: 2, cities: ["Stuttgart", "Tübingen"]);
    private HttpClient AdminClient() => _factory.CreateClientAs("admin");

    // ── Version & Config (public) ───────────────────────────────────

    [Fact]
    public async Task GetVersion_ReturnsVersionString()
    {
        var response = await _client.GetAsync("/api/version");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<VersionResponse>();
        Assert.NotNull(data);
        Assert.NotEmpty(data!.Version);
    }

    [Fact]
    public async Task GetConfig_ReturnsDatesPerRun()
    {
        var response = await _client.GetAsync("/api/config");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<ConfigResponse>();
        Assert.NotNull(data);
        Assert.Equal(5, data!.DatesPerRun);
    }

    // ── Auth ────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_ValidCredentials_ReturnsUserAndSessionWorks()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "admin@test.local", password = "AdminPass1!" });
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(data);
        Assert.Equal("admin@test.local", data!.Email);
        Assert.Equal("admin", data.Role);

        // Cookie round-trip: /me works after login
        var me = await _client.GetAsync("/api/auth/me");
        me.EnsureSuccessStatusCode();
        var meData = await me.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.Equal("admin@test.local", meData!.Email);
        Assert.Equal("admin", meData.Role);
    }

    [Fact]
    public async Task Login_UserWithTenant_ReturnsCitiesAndTenantName()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "user@stuttgart.test", password = "UserPass1!" });
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(data);
        Assert.Equal("user", data!.Role);
        Assert.Equal(1, data.TenantId);
        Assert.Contains("Stuttgart", data.Cities);
        Assert.Equal("Stuttgart Hotels GmbH", data.TenantName);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "admin@test.local", password = "wrong-password" });
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_UnknownUser_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "nobody@test.local", password = "whatever" });
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_InvalidatesSession()
    {
        await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "admin@test.local", password = "AdminPass1!" });

        var logout = await _client.PostAsync("/api/auth/logout", null);
        logout.EnsureSuccessStatusCode();

        var me = await _client.GetAsync("/api/auth/me");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_ApiReturns401()
    {
        var response = await _client.GetAsync("/api/hotels");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_MeReturns401()
    {
        var response = await _client.GetAsync("/api/auth/me");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Change password (self-service) ───────────────────────────────

    [Fact]
    public async Task ChangePassword_CorrectCurrentPassword_UpdatesPasswordAndNewLoginWorks()
    {
        // user@stuttgart.test (seeded with UserPass1!) — test handler must act as that exact user
        var user = await _db.Users.FirstAsync(u => u.Email == "user@stuttgart.test");
        var client = _factory.CreateClientAs("user", tenantId: 1, cities: ["Stuttgart"], userId: user.Id);

        var change = await client.PostAsJsonAsync("/api/auth/change-password",
            new { current_password = "UserPass1!", new_password = "NewPass1!" });
        change.EnsureSuccessStatusCode();

        // Old password no longer works
        var loginOld = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "user@stuttgart.test", password = "UserPass1!" });
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, loginOld.StatusCode);

        // New password works
        var loginNew = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "user@stuttgart.test", password = "NewPass1!" });
        loginNew.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_Returns400()
    {
        var user = await _db.Users.FirstAsync(u => u.Email == "user@stuttgart.test");
        var client = _factory.CreateClientAs("user", tenantId: 1, cities: ["Stuttgart"], userId: user.Id);

        var response = await client.PostAsJsonAsync("/api/auth/change-password",
            new { current_password = "WrongPass1!", new_password = "NewPass1!" });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_ShortNewPassword_Returns400()
    {
        var user = await _db.Users.FirstAsync(u => u.Email == "user@stuttgart.test");
        var client = _factory.CreateClientAs("user", tenantId: 1, cities: ["Stuttgart"], userId: user.Id);

        var response = await client.PostAsJsonAsync("/api/auth/change-password",
            new { current_password = "UserPass1!", new_password = "short" });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Session invalidation on password change (per-request validation via real cookie) ──

    [Fact]
    public async Task ChangePassword_InvalidatesExistingSessionCookie()
    {
        // Real cookie login (no X-Test-Auth header) on a dedicated client
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "user@stuttgart.test", password = "UserPass1!" });
        login.EnsureSuccessStatusCode();

        // The cookie's IssuedUtc is persisted with second granularity — the change
        // must land in a LATER second than the login, or the old cookie would be
        // indistinguishable from one issued after the change.
        await Task.Delay(1100);

        // Change the password while the original cookie is still in the jar
        var change = await client.PostAsJsonAsync("/api/auth/change-password",
            new { current_password = "UserPass1!", new_password = "NewPass1!" });
        change.EnsureSuccessStatusCode();

        // The cookie issued before the change must no longer grant access
        var rejected = await client.GetAsync("/api/auth/me");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, rejected.StatusCode);

        // Re-login with the new password issues a fresh cookie that works —
        // even when it happens in the same second as the change.
        var loginNew = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "user@stuttgart.test", password = "NewPass1!" });
        loginNew.EnsureSuccessStatusCode();
        var me = await client.GetAsync("/api/auth/me");
        me.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task AdminResetPassword_InvalidatesUsersExistingSession()
    {
        // user@stuttgart.test logs in with a real cookie
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "user@stuttgart.test", password = "UserPass1!" });
        login.EnsureSuccessStatusCode();

        // Cookie IssuedUtc has second granularity — reset must land in a later
        // second than the login (see ChangePassword_InvalidatesExistingSessionCookie).
        await Task.Delay(1100);

        // Admin resets the password after the cookie was issued
        var user = await _db.Users.FirstAsync(u => u.Email == "user@stuttgart.test");
        var admin = AdminClient();
        var reset = await admin.PostAsJsonAsync($"/api/admin/users/{user.Id}/reset-password",
            new { password = "AdminReset1!" });
        reset.EnsureSuccessStatusCode();

        // The cookie issued before the reset must no longer grant access
        var rejected = await client.GetAsync("/api/auth/me");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, rejected.StatusCode);

        // Re-login with the reset password issues a fresh cookie that works
        var loginNew = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "user@stuttgart.test", password = "AdminReset1!" });
        loginNew.EnsureSuccessStatusCode();
        var me = await client.GetAsync("/api/auth/me");
        me.EnsureSuccessStatusCode();
    }

    // ── Deactivation & claim refresh (per-request validation via real cookie) ──

    [Fact]
    public async Task DeactivatedUser_SubsequentRequestReturns401()
    {
        // Real cookie login (no X-Test-Auth header) on a dedicated client
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "user@stuttgart.test", password = "UserPass1!" });
        login.EnsureSuccessStatusCode();

        // Deactivate the user after the cookie was issued
        var user = await _db.Users.FirstAsync(u => u.Email == "user@stuttgart.test");
        user.IsActive = false;
        await _db.SaveChangesAsync();

        // The still-valid cookie must no longer grant access
        var response = await client.GetAsync("/api/hotels?city=Stuttgart");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeactivatedTenant_UserRequestReturns401()
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "user@stuttgart.test", password = "UserPass1!" });
        login.EnsureSuccessStatusCode();

        // Deactivate the user's tenant after login
        var tenant = await _db.Tenants.FirstAsync(t => t.Id == 1);
        tenant.IsActive = false;
        await _db.SaveChangesAsync();

        var response = await client.GetAsync("/api/hotels?city=Stuttgart");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CityClaim_RefreshedAfterTenantCityChange()
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "user@stuttgart.test", password = "UserPass1!" });
        login.EnsureSuccessStatusCode();

        // Admin replaces the tenant's cities after login: Stuttgart → Tübingen
        var tenant = await _db.Tenants.FirstAsync(t => t.Id == 1);
        var oldCities = await _db.TenantCities.Where(tc => tc.TenantId == tenant.Id).ToListAsync();
        _db.TenantCities.RemoveRange(oldCities);
        _db.TenantCities.Add(new TenantCity { TenantId = tenant.Id, City = "Tübingen" });
        await _db.SaveChangesAsync();

        // The refreshed city claims must scope the user to the new city:
        // asking for hotels now returns the Tübingen hotel, not the stale Stuttgart data.
        var response = await client.GetAsync("/api/hotels");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<List<HotelResponse>>();
        Assert.NotNull(data);
        Assert.Single(data!);
        Assert.All(data!, h => Assert.Equal("Tübingen", h.City));
    }

    // ── Cities ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetCities_AsAdmin_ReturnsConfiguredCities()
    {
        var client = AdminClient();
        var response = await client.GetAsync("/api/cities");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<List<CityResponse>>();
        Assert.NotNull(data);
        Assert.Contains(data, c => c.Name == "Stuttgart");
    }

    [Fact]
    public async Task GetCities_AsUser_ReturnsOnlyOwnCity()
    {
        var client = UserClient();
        var response = await client.GetAsync("/api/cities");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<List<CityResponse>>();
        Assert.NotNull(data);
        Assert.Single(data!);
        Assert.Equal("Stuttgart", data![0].Name);
    }

    [Fact]
    public async Task GetCities_ReturnsDestLabel()
    {
        var client = AdminClient();
        var response = await client.GetAsync("/api/cities");
        var data = await response.Content.ReadFromJsonAsync<List<CityResponse>>();
        var stuttgart = data!.First(c => c.Name == "Stuttgart");
        Assert.Equal("Stuttgart, Germany", stuttgart.DestLabel);
    }

    // ── Hotels ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetHotels_FiltersByCity()
    {
        var client = UserClient();
        var response = await client.GetAsync("/api/hotels?city=Stuttgart");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<List<HotelResponse>>();
        Assert.NotNull(data);
        Assert.Equal(2, data!.Count);
        Assert.All(data, h => Assert.Equal("Stuttgart", h.City));
    }

    [Fact]
    public async Task GetHotels_User_ForeignCityParam_ReturnsEmpty()
    {
        // A Stuttgart user must never see Berlin hotels, even when asking for them
        var client = UserClient();
        var response = await client.GetAsync("/api/hotels?city=Berlin");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<List<HotelResponse>>();
        Assert.NotNull(data);
        Assert.Empty(data!);
    }

    [Fact]
    public async Task GetHotels_OrderedByName()
    {
        var client = UserClient();
        var response = await client.GetAsync("/api/hotels?city=Stuttgart");
        var data = await response.Content.ReadFromJsonAsync<List<HotelResponse>>();
        var names = data!.Select(h => h.Name).ToList();
        Assert.Equal(names.OrderBy(n => n), names);
    }

    [Fact]
    public async Task GetHotels_UnknownCityReturnsEmpty_AsAdmin()
    {
        var client = AdminClient();
        var response = await client.GetAsync("/api/hotels?city=Paris");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<List<HotelResponse>>();
        Assert.Empty(data!);
    }

    [Fact]
    public async Task GetHotels_AsAdmin_WithoutCity_ReturnsAll()
    {
        var client = AdminClient();
        var response = await client.GetAsync("/api/hotels");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<List<HotelResponse>>();
        Assert.Equal(4, data!.Count);
    }

    [Fact]
    public async Task Admin_SeesAllCities()
    {
        var client = AdminClient();
        var response = await client.GetAsync("/api/cities");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<List<CityResponse>>();
        Assert.NotNull(data);
        Assert.Equal(2, data!.Count);
        Assert.Contains(data, c => c.Name == "Stuttgart");
        Assert.Contains(data, c => c.Name == "Tübingen");
    }

    // ── Multi-city tenants ──────────────────────────────────────────

    [Fact]
    public async Task TenantWithTwoCities_SeesBothCities()
    {
        var client = MultiCityClient();
        var response = await client.GetAsync("/api/hotels");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<List<HotelResponse>>();
        Assert.NotNull(data);
        Assert.Equal(3, data!.Count); // 2 Stuttgart + 1 Tübingen
        Assert.Contains(data, h => h.City == "Stuttgart");
        Assert.Contains(data, h => h.City == "Tübingen");
        Assert.DoesNotContain(data, h => h.City == "Berlin");

        // Prices are scoped to both cities as well
        var pricesResponse = await client.GetAsync("/api/prices");
        var prices = await pricesResponse.Content.ReadFromJsonAsync<List<HotelPricesResponse>>();
        Assert.NotNull(prices);
        Assert.Equal(3, prices!.Count); // both Stuttgart hotels + Tübingen hotel
    }

    [Fact]
    public async Task TenantWithOneCity_SeesOnlyThatCity()
    {
        var client = UserClient(); // tenant 1 → only Stuttgart
        var response = await client.GetAsync("/api/hotels");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<List<HotelResponse>>();
        Assert.NotNull(data);
        Assert.Equal(2, data!.Count);
        Assert.All(data, h => Assert.Equal("Stuttgart", h.City));
        Assert.DoesNotContain(data, h => h.City == "Tübingen");
    }

    // ── Patch Hotel (admin-only) ────────────────────────────────────

    [Fact]
    public async Task PatchHotel_DeactivatesHotel_AsAdmin()
    {
        var client = AdminClient();
        var hotel = await _db.Hotels.FirstAsync(h => h.BookingId == "1001");
        var response = await client.PatchAsJsonAsync($"/api/hotels/{hotel.Id}", new { active = false });
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<HotelResponse>();
        Assert.False(data!.Active);
    }

    [Fact]
    public async Task PatchHotel_AsUser_Returns403()
    {
        var client = UserClient();
        var hotel = await _db.Hotels.FirstAsync(h => h.BookingId == "1001");
        var response = await client.PatchAsJsonAsync($"/api/hotels/{hotel.Id}", new { active = false });
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PatchHotel_NotFoundReturns404()
    {
        var client = AdminClient();
        var response = await client.PatchAsJsonAsync("/api/hotels/99999", new { active = false });
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Prices ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetPrices_ReturnsHotelsWithPrices()
    {
        var client = UserClient();
        var response = await client.GetAsync("/api/prices");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<List<HotelPricesResponse>>();
        Assert.NotNull(data);
        Assert.Equal(2, data!.Count); // Only Stuttgart hotels have prices
        Assert.All(data, h => Assert.NotEmpty(h.Prices));
    }

    [Fact]
    public async Task GetPrices_FilterByHotelIds()
    {
        var client = UserClient();
        var hotel = await _db.Hotels.FirstAsync(h => h.BookingId == "1001");
        var response = await client.GetAsync($"/api/prices?hotel_ids={hotel.Id}");
        var data = await response.Content.ReadFromJsonAsync<List<HotelPricesResponse>>();
        Assert.Single(data!);
        Assert.Equal(hotel.Id, data![0].HotelId);
    }

    [Fact]
    public async Task GetPrices_FilterByDateRange()
    {
        var client = UserClient();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = today.AddDays(3).ToString("yyyy-MM-dd");
        var to = today.AddDays(7).ToString("yyyy-MM-dd");

        var response = await client.GetAsync($"/api/prices?from={from}&to={to}");
        var data = await response.Content.ReadFromJsonAsync<List<HotelPricesResponse>>();

        Assert.NotNull(data);
        foreach (var hotel in data!)
        {
            foreach (var price in hotel.Prices)
            {
                Assert.True(string.Compare(price.Date, from) >= 0);
                Assert.True(string.Compare(price.Date, to) <= 0);
            }
        }
    }

    [Fact]
    public async Task GetPrices_DefaultsToDoubleRoomType()
    {
        var client = UserClient();
        var response = await client.GetAsync("/api/prices");
        var data = await response.Content.ReadFromJsonAsync<List<HotelPricesResponse>>();
        Assert.NotNull(data);
        Assert.All(data!, h => Assert.All(h.Prices, p => Assert.Equal("double", p.RoomType)));
    }

    [Fact]
    public async Task GetPrices_FilterByRoomTypeSingle()
    {
        var client = UserClient();
        var response = await client.GetAsync("/api/prices?room_type=single");
        var data = await response.Content.ReadFromJsonAsync<List<HotelPricesResponse>>();
        Assert.NotNull(data);
        Assert.NotEmpty(data!);
        Assert.All(data!, h => Assert.All(h.Prices, p => Assert.Equal("single", p.RoomType)));
    }

    [Fact]
    public async Task GetPrices_PricePointContainsRoomType()
    {
        var client = UserClient();
        var response = await client.GetAsync("/api/prices?room_type=double");
        var data = await response.Content.ReadFromJsonAsync<List<HotelPricesResponse>>();
        Assert.NotNull(data);
        Assert.NotEmpty(data!);
        var firstPrice = data![0].Prices[0];
        Assert.NotNull(firstPrice.RoomType);
        Assert.NotEmpty(firstPrice.RoomType);
    }

    [Fact]
    public async Task GetPrices_ReturnsCorrectSeedValuesForBothRoomTypes()
    {
        var client = UserClient();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var hotel = await _db.Hotels.FirstAsync(h => h.BookingId == "1001");

        var responseDouble = await client.GetAsync($"/api/prices?hotel_ids={hotel.Id}&room_type=double");
        var dataDouble = await responseDouble.Content.ReadFromJsonAsync<List<HotelPricesResponse>>();

        var responseSingle = await client.GetAsync($"/api/prices?hotel_ids={hotel.Id}&room_type=single");
        var dataSingle = await responseSingle.Content.ReadFromJsonAsync<List<HotelPricesResponse>>();

        Assert.NotNull(dataDouble);
        Assert.NotNull(dataSingle);
        Assert.Single(dataDouble!);
        Assert.Single(dataSingle!);

        // Verify seed values: double = 100 + i*5, single = 70 + i*3
        for (int i = 0; i < 15; i++)
        {
            var expectedDate = today.AddDays(i + 1).ToString("yyyy-MM-dd");
            var expectedDouble = 100 + i * 5;
            var expectedSingle = 70 + i * 3;

            var dPrice = dataDouble![0].Prices.First(p => p.Date == expectedDate);
            Assert.Equal(expectedDouble, dPrice.PriceEur);

            var sPrice = dataSingle![0].Prices.First(p => p.Date == expectedDate);
            Assert.Equal(expectedSingle, sPrice.PriceEur);
        }
    }

    [Fact]
    public async Task GetPrices_FilterByHotelIdsAndRoomType()
    {
        var client = UserClient();
        var hotel = await _db.Hotels.FirstAsync(h => h.BookingId == "1001");

        var response = await client.GetAsync($"/api/prices?hotel_ids={hotel.Id}&room_type=single");
        var data = await response.Content.ReadFromJsonAsync<List<HotelPricesResponse>>();

        Assert.NotNull(data);
        Assert.Single(data!);
        Assert.Equal(hotel.Id, data![0].HotelId);
        Assert.All(data!, h => Assert.All(h.Prices, p => Assert.Equal("single", p.RoomType)));
    }

    [Fact]
    public async Task GetPrices_UnknownRoomTypeReturnsEmpty()
    {
        var client = UserClient();
        var response = await client.GetAsync("/api/prices?room_type=triple");
        var data = await response.Content.ReadFromJsonAsync<List<HotelPricesResponse>>();
        Assert.NotNull(data);
        Assert.Empty(data!);
    }

    [Fact]
    public async Task GetPrices_EachDateHasBothRoomTypes()
    {
        var client = UserClient();
        var hotel = await _db.Hotels.FirstAsync(h => h.BookingId == "1001");

        var responseDouble = await client.GetAsync($"/api/prices?hotel_ids={hotel.Id}&room_type=double");
        var dataDouble = await responseDouble.Content.ReadFromJsonAsync<List<HotelPricesResponse>>();

        var responseSingle = await client.GetAsync($"/api/prices?hotel_ids={hotel.Id}&room_type=single");
        var dataSingle = await responseSingle.Content.ReadFromJsonAsync<List<HotelPricesResponse>>();

        Assert.NotNull(dataDouble);
        Assert.NotNull(dataSingle);
        Assert.Single(dataDouble!);
        Assert.Single(dataSingle!);

        var doubleDates = dataDouble![0].Prices.Select(p => p.Date).ToHashSet();
        var singleDates = dataSingle![0].Prices.Select(p => p.Date).ToHashSet();

        // Each date should be present in both room types
        Assert.Equal(doubleDates.Count, singleDates.Count);
        Assert.True(doubleDates.SetEquals(singleDates),
            "The same set of dates should exist for both single and double room types");
    }

    // ── IDOR-Schutz ─────────────────────────────────────────────────

    [Fact]
    public async Task Prices_HotelIdsFromForeignCity_ReturnsEmpty()
    {
        // Multi-city user (Stuttgart + Tübingen) asking for a Berlin hotel_id gets nothing
        var client = MultiCityClient();
        var berlinHotel = await _db.Hotels.FirstAsync(h => h.BookingId == "2001");

        var response = await client.GetAsync($"/api/prices?hotel_ids={berlinHotel.Id}");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<List<HotelPricesResponse>>();
        Assert.Empty(data!);
    }

    [Fact]
    public async Task GetPrices_User_CanOnlySeeOwnCityPrices()
    {
        var client = UserClient();
        var response = await client.GetAsync("/api/prices");
        var data = await response.Content.ReadFromJsonAsync<List<HotelPricesResponse>>();

        var stuttgartHotelIds = await _db.Hotels.Where(h => h.City == "Stuttgart").Select(h => h.Id).ToHashSetAsync();
        Assert.NotNull(data);
        Assert.All(data!, h => Assert.Contains(h.HotelId, stuttgartHotelIds));
    }

    // ── Watchlist ───────────────────────────────────────────────────

    [Fact]
    public async Task Watchlist_AddGetDelete_RoundTrip()
    {
        var client = UserClient();
        var hotel = await _db.Hotels.FirstAsync(h => h.BookingId == "1001");

        var put = await client.PutAsync($"/api/watchlist/{hotel.Id}", null);
        put.EnsureSuccessStatusCode();

        var get = await client.GetAsync("/api/watchlist");
        var ids = await get.Content.ReadFromJsonAsync<List<int>>();
        Assert.Contains(hotel.Id, ids!);

        var del = await client.DeleteAsync($"/api/watchlist/{hotel.Id}");
        del.EnsureSuccessStatusCode();

        var getAfter = await client.GetAsync("/api/watchlist");
        var idsAfter = await getAfter.Content.ReadFromJsonAsync<List<int>>();
        Assert.DoesNotContain(hotel.Id, idsAfter!);
    }

    [Fact]
    public async Task Watchlist_AddHotelFromForeignCity_Returns400()
    {
        var client = UserClient();
        var berlinHotel = await _db.Hotels.FirstAsync(h => h.BookingId == "2001");

        var response = await client.PutAsync($"/api/watchlist/{berlinHotel.Id}", null);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Watchlist_AddUnknownHotel_Returns404()
    {
        var client = UserClient();
        var response = await client.PutAsync("/api/watchlist/99999", null);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Watchlist_AsAdmin_ReturnsEmpty()
    {
        var client = AdminClient();
        var response = await client.GetAsync("/api/watchlist");
        response.EnsureSuccessStatusCode();
        var ids = await response.Content.ReadFromJsonAsync<List<int>>();
        Assert.Empty(ids!);
    }

    // ── Admin-only Endpoints ────────────────────────────────────────

    [Fact]
    public async Task Fetch_AsUser_Returns403()
    {
        var client = UserClient();
        var response = await client.PostAsync("/api/fetch", null);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Fetch_AsAdmin_Returns200()
    {
        // RapidAPI key is set to a dummy test key — the fetcher fails internally,
        // but the endpoint itself must be reachable for an admin.
        var client = AdminClient();
        var response = await client.PostAsync("/api/fetch?city=Stuttgart", null);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AdminTenants_AsUser_Returns403()
    {
        var client = UserClient();
        var response = await client.GetAsync("/api/admin/tenants");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminTenants_AsAdmin_ListsAndCreates()
    {
        var client = AdminClient();

        var list = await client.GetAsync("/api/admin/tenants");
        list.EnsureSuccessStatusCode();
        var tenants = await list.Content.ReadFromJsonAsync<List<TenantResponse>>();
        Assert.Equal(2, tenants!.Count);
        Assert.Contains(tenants, t => t.Name == "Stuttgart Hotels GmbH");
        Assert.Contains(tenants, t => t.Name == "Stuttgart-Tübingen Gruppe");
        Assert.Contains(tenants.First(t => t.Name == "Stuttgart Hotels GmbH").Cities, c => c == "Stuttgart");
        Assert.Contains(tenants.First(t => t.Name == "Stuttgart-Tübingen Gruppe").Cities, c => c == "Tübingen");

        var create = await client.PostAsJsonAsync("/api/admin/tenants",
            new { name = "Tübingen Hotels GmbH", cities = new[] { "Tübingen" } });
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<TenantResponse>();
        Assert.Contains("Tübingen", created!.Cities);
    }

    [Fact]
    public async Task AdminUsers_AsAdmin_CreateAndResetPassword()
    {
        var client = AdminClient();

        var create = await client.PostAsJsonAsync("/api/admin/users",
            new { email = "new@tuebingen.test", password = "StartPass1!", tenant_id = 1, role = "user" });
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal("new@tuebingen.test", created!.Email);
        Assert.Equal(1, created.TenantId);

        // New user can log in with the set password
        var login = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "new@tuebingen.test", password = "StartPass1!" });
        login.EnsureSuccessStatusCode();

        // Reset password
        var reset = await client.PostAsJsonAsync($"/api/admin/users/{created.Id}/reset-password",
            new { password = "NewPass2!" });
        reset.EnsureSuccessStatusCode();

        // Old password no longer works
        var loginOld = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "new@tuebingen.test", password = "StartPass1!" });
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, loginOld.StatusCode);

        // New password works
        var loginNew = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "new@tuebingen.test", password = "NewPass2!" });
        loginNew.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task AdminUsers_UserWithoutTenant_Returns400()
    {
        var client = AdminClient();
        var response = await client.PostAsJsonAsync("/api/admin/users",
            new { email = "no-tenant@test.local", password = "StartPass1!", role = "user" });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchUser_DeactivatesUser()
    {
        var client = AdminClient();
        var user = await _db.Users.FirstAsync(u => u.Email == "user@stuttgart.test");

        var response = await client.PatchAsJsonAsync($"/api/admin/users/{user.Id}", new { is_active = false });
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(data);
        Assert.False(data!.IsActive);
    }

    [Fact]
    public async Task PatchUser_SelfDeactivation_Returns400()
    {
        // AdminClient uses X-Test-UserId=1 — the seeded admin account.
        var client = AdminClient();
        var response = await client.PatchAsJsonAsync("/api/admin/users/1", new { is_active = false });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchUser_AsUser_Returns403()
    {
        var client = UserClient();
        var user = await _db.Users.FirstAsync(u => u.Email == "user@stuttgart.test");

        var response = await client.PatchAsJsonAsync($"/api/admin/users/{user.Id}", new { is_active = false });
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PatchUser_InvalidRole_Returns400()
    {
        var client = AdminClient();
        var user = await _db.Users.FirstAsync(u => u.Email == "user@stuttgart.test");

        var response = await client.PatchAsJsonAsync($"/api/admin/users/{user.Id}", new { role = "superuser" });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchUser_UserRoleRequiresTenant_Returns400()
    {
        // Switching an admin to role "user" requires an explicit active tenant_id.
        var client = AdminClient();
        var admin = await _db.Users.FirstAsync(u => u.Email == "admin@test.local");

        var response = await client.PatchAsJsonAsync($"/api/admin/users/{admin.Id}", new { role = "user" });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchUser_SelfDemotion_Returns400()
    {
        // AdminClient uses X-Test-UserId=1 — the seeded admin account.
        // Self-demotion would lock the admin out of /api/admin, so the
        // endpoint must reject it even when a valid tenant_id is supplied.
        var client = AdminClient();
        var response = await client.PatchAsJsonAsync("/api/admin/users/1", new { role = "user", tenant_id = 1 });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Status ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetStatus_AsAdmin_ReturnsGlobalAggregate()
    {
        var client = AdminClient();
        var response = await client.GetAsync("/api/status");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<StatusResponse>();
        Assert.NotNull(data);
        Assert.Equal(4, data!.TotalHotels);
        Assert.Equal(3, data.ActiveHotels);
        Assert.Equal(90, data.TotalPrices);
    }

    [Fact]
    public async Task GetStatus_User_IsScopedToOwnCities()
    {
        var client = UserClient();
        var response = await client.GetAsync("/api/status?city=Stuttgart");
        var data = await response.Content.ReadFromJsonAsync<StatusResponse>();
        Assert.NotNull(data);
        Assert.Equal("Stuttgart", data!.City);
        Assert.Equal(2, data.TotalHotels);
        Assert.Equal(2, data.ActiveHotels);
    }

    // ── Response type records ─────────────────────────────────────────

    private record VersionResponse([property: JsonPropertyName("version")] string Version);
    private record ConfigResponse([property: JsonPropertyName("dates_per_run")] int DatesPerRun);
    private record LoginResponse(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("tenant_id")] int? TenantId,
        [property: JsonPropertyName("tenant_name")] string? TenantName,
        [property: JsonPropertyName("cities")] List<string> Cities);
    private record CityResponse(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("dest_label")] string? DestLabel
    );
    private record HotelResponse(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("booking_id")] string BookingId,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("address")] string? Address,
        [property: JsonPropertyName("stars")] int? Stars,
        [property: JsonPropertyName("review_score")] double? ReviewScore,
        [property: JsonPropertyName("image_url")] string? ImageUrl,
        [property: JsonPropertyName("distance_km")] double? DistanceKm,
        [property: JsonPropertyName("active")] bool Active,
        [property: JsonPropertyName("city")] string City);
    private record HotelPricesResponse(
        [property: JsonPropertyName("hotel_id")] int HotelId,
        [property: JsonPropertyName("hotel_name")] string HotelName,
        [property: JsonPropertyName("stars")] int? Stars,
        [property: JsonPropertyName("prices")] List<PricePointResponse> Prices);
    private record PricePointResponse(
        [property: JsonPropertyName("date")] string Date,
        [property: JsonPropertyName("price_eur")] double PriceEur,
        [property: JsonPropertyName("room_type")] string RoomType);
    private record StatusResponse(
        [property: JsonPropertyName("city")] string? City,
        [property: JsonPropertyName("total_hotels")] int TotalHotels,
        [property: JsonPropertyName("active_hotels")] int ActiveHotels,
        [property: JsonPropertyName("total_prices")] int TotalPrices,
        [property: JsonPropertyName("dates_covered")] int DatesCovered,
        [property: JsonPropertyName("dates_total")] int DatesTotal,
        [property: JsonPropertyName("coverage_pct")] double CoveragePct
    );
    private record TenantResponse(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("cities")] List<string> Cities,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("created_at")] DateTime CreatedAt
    );
    private record UserResponse(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("tenant_id")] int? TenantId,
        [property: JsonPropertyName("tenant_name")] string? TenantName,
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("created_at")] DateTime CreatedAt
    );
}
