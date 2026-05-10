using HotelScraper.Api.Configuration;
using HotelScraper.Api.Data;
using HotelScraper.Api.Endpoints;
using HotelScraper.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quartz;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ──────────────────────────────────────────────────────
builder.Services.Configure<ScraperOptions>(builder.Configuration.GetSection(ScraperOptions.Section));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<ScraperOptions>>().Value);

var scraperSection = builder.Configuration.GetSection(ScraperOptions.Section);
var options = scraperSection.Get<ScraperOptions>() ?? new ScraperOptions();

// ── Database ───────────────────────────────────────────────────────────
var dbPath = ResolveDbPath(options.DatabaseUrl);
builder.Services.AddDbContextFactory<AppDbContext>(opts =>
    opts.UseSqlite($"Data Source={dbPath}"));

// ── HTTP Client (typed) ────────────────────────────────────────────────
builder.Services.AddHttpClient<BookingApiService>(client =>
{
    client.DefaultRequestHeaders.Add("X-RapidAPI-Key", options.RapidApiKey);
});

// ── Application Services ───────────────────────────────────────────────
builder.Services.AddScoped<PriceFetcherService>();

// ── Scheduler (Quartz.NET) ─────────────────────────────────────────────
if (!string.IsNullOrWhiteSpace(options.RapidApiKey) && options.RapidApiKey != "your_rapidapi_key_here")
{
    builder.Services.AddQuartz(q =>
    {
        var jobKey = new JobKey("daily_fetch");
        q.AddJob<DailyFetchJob>(j => j.WithIdentity(jobKey));

        q.AddTrigger(t => t
            .ForJob(jobKey)
            .WithIdentity("daily_fetch_trigger")
            .WithCronSchedule($"0 0 {options.FetchHour} * * ?"));
    });
    builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);
}
else
{
    // Even if scheduler is disabled, register a dummy Quartz setup to avoid DI errors
    builder.Services.AddQuartz(q => { });
    builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);
}

// ── OpenAPI ────────────────────────────────────────────────────────────
builder.Services.AddOpenApi();

var app = builder.Build();

// ── Database initialization ────────────────────────────────────────────
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    await RunMigrationsAsync(db, options);
}

// ── Middleware pipeline ────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Serve React frontend static files
var staticDir = Path.Combine(app.Environment.ContentRootPath, "wwwroot");

if (Directory.Exists(staticDir))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();

    // SPA fallback: serve index.html for non-API routes
    app.MapFallbackToFile("index.html");
}
else
{
    app.MapGet("/", () => Results.Ok(new
    {
        message = "Hotel Price Tracker API running. Frontend not built yet — run 'npm run build' in frontend/."
    }));
}

// ── API routes ─────────────────────────────────────────────────────────
app.MapHotelEndpoints();
app.MapPriceEndpoints();

app.Run();

// ── Helpers ────────────────────────────────────────────────────────────

static string ResolveDbPath(string databaseUrl)
{
    // Convert Python-style "sqlite+aiosqlite:///./data/hotel_prices.db" → full path
    var path = databaseUrl
        .Replace("sqlite+aiosqlite:///", "")
        .Replace("sqlite:///", "");

    if (path.StartsWith("./"))
        path = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));

    var dir = Path.GetDirectoryName(path);
    if (!string.IsNullOrWhiteSpace(dir))
        Directory.CreateDirectory(dir);

    return path;
}

static async Task RunMigrationsAsync(AppDbContext db, ScraperOptions options)
{
    try
    {
        using var cmd = db.Database.GetDbConnection().CreateCommand();
        await db.Database.OpenConnectionAsync();

        // Check if city column exists
        cmd.CommandText = "PRAGMA table_info(hotels)";
        using var reader = await cmd.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(1));
        await reader.DisposeAsync();

        // Migration: add 'city' column
        if (!columns.Contains("city"))
        {
            try
            {
                cmd.CommandText = "ALTER TABLE hotels ADD COLUMN city TEXT NOT NULL DEFAULT ''";
                await cmd.ExecuteNonQueryAsync();

                var defaultCity = options.CityList[0];
                cmd.CommandText = $"UPDATE hotels SET city = '{defaultCity.Replace("'", "''")}' WHERE city = ''";
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = "DROP INDEX IF EXISTS ix_hotels_booking_id";
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = "CREATE INDEX IF NOT EXISTS ix_hotels_city ON hotels (city)";
                await cmd.ExecuteNonQueryAsync();

                cmd.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS uq_booking_city ON hotels (booking_id, city)";
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("duplicate column"))
            {
                // Column already exists (e.g., created by EnsureCreatedAsync) — skip
            }
        }
        else
        {
            var defaultCity = options.CityList[0];
            cmd.CommandText = $"UPDATE hotels SET city = '{defaultCity.Replace("'", "''")}' WHERE city = '' OR city IS NULL";
            await cmd.ExecuteNonQueryAsync();
        }

        // Migration: add 'distance_km' column
        if (!columns.Contains("distance_km"))
        {
            try
            {
                cmd.CommandText = "ALTER TABLE hotels ADD COLUMN distance_km FLOAT";
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("duplicate column"))
            {
                // Column already exists — skip
            }
        }
    }
    catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("no such table"))
    {
        // Table doesn't exist yet (fresh in-memory DB) — EnsureCreatedAsync will handle it
    }
}

// Required for integration testing
public partial class Program { }
