using HotelScraper.Api.Configuration;
using HotelScraper.Api.Data;
using HotelScraper.Api.Endpoints;
using HotelScraper.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
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

// Serve React frontend static files — search multiple possible locations
var staticDir = FindStaticDir(app.Environment.ContentRootPath);

if (staticDir is not null)
{
    app.UseDefaultFiles();
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(staticDir)
    });

    // SPA fallback: serve index.html for non-API routes
    app.MapFallbackToFile("index.html", new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(staticDir)
    });
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

static string? FindStaticDir(string contentRoot)
{
    // Priority: wwwroot/ (publish target), backend/static/ (Vite default output), frontend/dist/
    var candidates = new[]
    {
        Path.Combine(contentRoot, "wwwroot"),
        Path.Combine(contentRoot, "..", "..", "..", "..", "backend", "static"),
        Path.Combine(contentRoot, "..", "..", "..", "..", "frontend", "dist"),
    };

    foreach (var dir in candidates)
    {
        var full = Path.GetFullPath(dir);
        if (Directory.Exists(full) && File.Exists(Path.Combine(full, "index.html")))
            return full;
    }

    return null;
}

static string ResolveDbPath(string databaseUrl)
{
    // Convert Python-style "sqlite+aiosqlite:///./data/hotel_prices.db" → full path
    // Also strip "Data Source=" prefix if present (from connection-string-style config)
    var path = databaseUrl
        .Replace("sqlite+aiosqlite:///", "")
        .Replace("sqlite:///", "")
        .Replace("Data Source=", "");

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

        // Migration: add 'room_type' column to prices + ensure correct unique index
        cmd.CommandText = "PRAGMA table_info(prices)";
        using var priceColumnsReader = await cmd.ExecuteReaderAsync();
        var priceColumns = new List<string>();
        while (await priceColumnsReader.ReadAsync())
            priceColumns.Add(priceColumnsReader.GetString(1));
        await priceColumnsReader.DisposeAsync();

        if (!priceColumns.Contains("room_type"))
        {
            try
            {
                cmd.CommandText = "ALTER TABLE prices ADD COLUMN room_type TEXT NOT NULL DEFAULT 'double'";
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("duplicate column"))
            {
                // Column already exists — skip
            }
        }

        // Ensure the unique index is on (hotel_id, date, room_type), not the old (hotel_id, date).
        // This runs every startup to fix databases that were partially migrated or
        // created with an older model that had a 2-column unique constraint.
        // First, discover any 2-column unique index on prices (regardless of its name).
        cmd.CommandText = "PRAGMA index_list(prices)";
        using var idxListReader = await cmd.ExecuteReaderAsync();
        var indexNames = new List<(string Name, bool IsUnique)>();
        while (await idxListReader.ReadAsync())
            indexNames.Add((idxListReader.GetString(1), idxListReader.GetInt32(2) == 1));
        await idxListReader.DisposeAsync();

        var indexesToDrop = new List<string>();
        foreach (var (idxName, isUnique) in indexNames)
        {
            if (!isUnique) continue;

            using var infoCmd = db.Database.GetDbConnection().CreateCommand();
            infoCmd.CommandText = $"PRAGMA index_info({idxName})";
            using var infoReader = await infoCmd.ExecuteReaderAsync();
            var idxColumns = new List<string>();
            while (await infoReader.ReadAsync())
                idxColumns.Add(infoReader.GetString(2));
            await infoReader.DisposeAsync();

            // Detect old 2-column unique index: has hotel_id + date but NOT room_type
            if (idxColumns.Count == 2
                && idxColumns.Contains("hotel_id", StringComparer.OrdinalIgnoreCase)
                && idxColumns.Contains("date", StringComparer.OrdinalIgnoreCase)
                && !idxColumns.Contains("room_type", StringComparer.OrdinalIgnoreCase))
            {
                indexesToDrop.Add(idxName);
            }
        }

        foreach (var oldIdx in indexesToDrop)
        {
            cmd.CommandText = $"DROP INDEX IF EXISTS {oldIdx}";
            await cmd.ExecuteNonQueryAsync();
        }

        // Create the correct 3-column unique index if it doesn't exist
        cmd.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS uq_hotel_date_room ON prices (hotel_id, date, room_type)";
        await cmd.ExecuteNonQueryAsync();
    }
    catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("no such table"))
    {
        // Table doesn't exist yet (fresh in-memory DB) — EnsureCreatedAsync will handle it
    }
}

// Required for integration testing
public partial class Program { }
