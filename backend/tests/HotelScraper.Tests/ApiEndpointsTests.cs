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
            claims.Add(new Claim("city", city[0]!));

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
                opts.SearchCities = "Stuttgart";
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
    /// Creates a client authenticated as the given role/tenant/city via the test handler.
    /// Pass role "admin" without tenant/city for a global admin.
    /// </summary>
    public HttpClient CreateClientAs(string role = "user", int? tenantId = null, string? city = null, int? userId = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeader, "true");
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, role);
        if (tenantId.HasValue)
            client.DefaultRequestHeaders.Add(TestAuthHandler.TenantIdHeader, tenantId.Value.ToString());
        if (city is not null)
            client.DefaultRequestHeaders.Add(TestAuthHandler.CityHeader, city);
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
            new Hotel { BookingId = "2001", Name = "Berlin Central Hotel", City = "Berlin", Stars = 4, Active = false }
        );

        _db.Tenants.Add(new Tenant { Name = "Stuttgart Hotels GmbH", City = "Stuttgart" });

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
            TenantId = 1,
        });

        _db.Settings.Add(new Setting { Key = "dest_label:Stuttgart", Value = "Stuttgart, Germany" });

        await _db.SaveChangesAsync();

        // Seed prices for Stuttgart hotels
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var hotels = await _db.Hotels.Where(h => h.City == "Stuttgart").ToListAsync();
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

    private HttpClient UserClient() => _factory.CreateClientAs("user", tenantId: 1, city: "Stuttgart");
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
    public async Task Login_UserWithTenant_ReturnsCityAndTenantName()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "user@stuttgart.test", password = "UserPass1!" });
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(data);
        Assert.Equal("user", data!.Role);
        Assert.Equal(1, data.TenantId);
        Assert.Equal("Stuttgart", data.City);
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
    public async Task GetHotels_User_IgnoresForeignCityParam()
    {
        // A Stuttgart user must never see Berlin hotels, even when asking for them
        var client = UserClient();
        var response = await client.GetAsync("/api/hotels?city=Berlin");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<List<HotelResponse>>();
        Assert.NotNull(data);
        Assert.All(data!, h => Assert.Equal("Stuttgart", h.City));
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
        Assert.Equal(3, data!.Count);
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
    public async Task GetPrices_HotelIdsFromForeignCity_ReturnsEmpty()
    {
        var client = UserClient();
        var berlinHotel = await _db.Hotels.FirstAsync(h => h.BookingId == "2001");

        // A Stuttgart user asking for a Berlin hotel_id must get nothing back
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
        Assert.Single(tenants!);
        Assert.Equal("Stuttgart Hotels GmbH", tenants![0].Name);

        var create = await client.PostAsJsonAsync("/api/admin/tenants",
            new { name = "Tübingen Hotels GmbH", city = "Tübingen" });
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<TenantResponse>();
        Assert.Equal("Tübingen", created!.City);
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

    // ── Status ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetStatus_AsAdmin_ReturnsGlobalAggregate()
    {
        var client = AdminClient();
        var response = await client.GetAsync("/api/status");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<StatusResponse>();
        Assert.NotNull(data);
        Assert.Equal(3, data!.TotalHotels);
        Assert.Equal(2, data.ActiveHotels);
        Assert.Equal(60, data.TotalPrices);
    }

    [Fact]
    public async Task GetStatus_User_IsScopedToOwnCity()
    {
        var client = UserClient();
        var response = await client.GetAsync("/api/status");
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
        [property: JsonPropertyName("city")] string? City);
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
        [property: JsonPropertyName("city")] string City,
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
