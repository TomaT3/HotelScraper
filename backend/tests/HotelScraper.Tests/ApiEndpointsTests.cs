using HotelScraper.Api;
using HotelScraper.Api.Configuration;
using HotelScraper.Api.Data;
using HotelScraper.Api.Dtos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace HotelScraper.Tests;

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
        });
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

    // ── Version & Config ──────────────────────────────────────────────

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

    // ── Cities ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetCities_ReturnsConfiguredCities()
    {
        var response = await _client.GetAsync("/api/cities");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<List<CityResponse>>();
        Assert.NotNull(data);
        Assert.Contains(data, c => c.Name == "Stuttgart");
    }

    [Fact]
    public async Task GetCities_ReturnsDestLabel()
    {
        var response = await _client.GetAsync("/api/cities");
        var data = await response.Content.ReadFromJsonAsync<List<CityResponse>>();
        var stuttgart = data!.First(c => c.Name == "Stuttgart");
        Assert.Equal("Stuttgart, Germany", stuttgart.DestLabel);
    }

    // ── Hotels ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetHotels_FiltersByCity()
    {
        var response = await _client.GetAsync("/api/hotels?city=Stuttgart");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<List<HotelResponse>>();
        Assert.NotNull(data);
        Assert.Equal(2, data!.Count);
        Assert.All(data, h => Assert.Equal("Stuttgart", h.City));
    }

    [Fact]
    public async Task GetHotels_OrderedByName()
    {
        var response = await _client.GetAsync("/api/hotels?city=Stuttgart");
        var data = await response.Content.ReadFromJsonAsync<List<HotelResponse>>();
        var names = data!.Select(h => h.Name).ToList();
        Assert.Equal(names.OrderBy(n => n), names);
    }

    [Fact]
    public async Task GetHotels_UnknownCityReturnsEmpty()
    {
        var response = await _client.GetAsync("/api/hotels?city=Paris");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<List<HotelResponse>>();
        Assert.Empty(data!);
    }

    // ── Patch Hotel ───────────────────────────────────────────────────

    [Fact]
    public async Task PatchHotel_DeactivatesHotel()
    {
        var hotel = await _db.Hotels.FirstAsync(h => h.BookingId == "1001");
        var response = await _client.PatchAsJsonAsync($"/api/hotels/{hotel.Id}", new { active = false });
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<HotelResponse>();
        Assert.False(data!.Active);
    }

    [Fact]
    public async Task PatchHotel_NotFoundReturns404()
    {
        var response = await _client.PatchAsJsonAsync("/api/hotels/99999", new { active = false });
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Prices ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPrices_ReturnsHotelsWithPrices()
    {
        var response = await _client.GetAsync("/api/prices");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<List<HotelPricesResponse>>();
        Assert.NotNull(data);
        Assert.Equal(2, data!.Count); // Only Stuttgart hotels have prices
        Assert.All(data, h => Assert.NotEmpty(h.Prices));
    }

    [Fact]
    public async Task GetPrices_FilterByHotelIds()
    {
        var hotel = await _db.Hotels.FirstAsync(h => h.BookingId == "1001");
        var response = await _client.GetAsync($"/api/prices?hotel_ids={hotel.Id}");
        var data = await response.Content.ReadFromJsonAsync<List<HotelPricesResponse>>();
        Assert.Single(data!);
        Assert.Equal(hotel.Id, data![0].HotelId);
    }

    [Fact]
    public async Task GetPrices_FilterByDateRange()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = today.AddDays(3).ToString("yyyy-MM-dd");
        var to = today.AddDays(7).ToString("yyyy-MM-dd");

        var response = await _client.GetAsync($"/api/prices?from={from}&to={to}");
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
        var response = await _client.GetAsync("/api/prices");
        var data = await response.Content.ReadFromJsonAsync<List<HotelPricesResponse>>();
        Assert.NotNull(data);
        Assert.All(data!, h => Assert.All(h.Prices, p => Assert.Equal("double", p.RoomType)));
    }

    [Fact]
    public async Task GetPrices_FilterByRoomTypeSingle()
    {
        var response = await _client.GetAsync("/api/prices?room_type=single");
        var data = await response.Content.ReadFromJsonAsync<List<HotelPricesResponse>>();
        Assert.NotNull(data);
        Assert.NotEmpty(data!);
        Assert.All(data!, h => Assert.All(h.Prices, p => Assert.Equal("single", p.RoomType)));
    }

    // ── Status ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStatus_ReturnsGlobalAggregate()
    {
        var response = await _client.GetAsync("/api/status");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<StatusResponse>();
        Assert.NotNull(data);
        Assert.Equal(3, data!.TotalHotels);
        Assert.Equal(2, data.ActiveHotels);
        Assert.Equal(60, data.TotalPrices);
    }

    [Fact]
    public async Task GetStatus_CityScoped()
    {
        var response = await _client.GetAsync("/api/status?city=Stuttgart");
        var data = await response.Content.ReadFromJsonAsync<StatusResponse>();
        Assert.NotNull(data);
        Assert.Equal("Stuttgart", data!.City);
        Assert.Equal(2, data.TotalHotels);
        Assert.Equal(2, data.ActiveHotels);
    }

    // ── Response type records ─────────────────────────────────────────

    private record VersionResponse([property: JsonPropertyName("version")] string Version);
    private record ConfigResponse([property: JsonPropertyName("dates_per_run")] int DatesPerRun);
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
}
