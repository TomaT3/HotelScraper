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
        var pricesIdMissingPk = false;
        while (await priceColumnsReader.ReadAsync())
        {
            var colName = priceColumnsReader.GetString(1);
            priceColumns.Add(colName);
            // Detect if 'id' column is missing PRIMARY KEY (corrupted by a previous table rebuild)
            if (string.Equals(colName, "id", StringComparison.OrdinalIgnoreCase) && priceColumnsReader.GetInt32(5) == 0)
                pricesIdMissingPk = true;
        }
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

        // Ensure the unique constraint is on (hotel_id, date, room_type), not the old (hotel_id, date).
        // This runs every startup to fix databases created with an older model.
        // Inline UNIQUE constraints (sqlite_autoindex_*) require a table rebuild;
        // regular unique indexes can be dropped directly.
        cmd.CommandText = "PRAGMA index_list(prices)";
        using var idxListReader = await cmd.ExecuteReaderAsync();
        var uniqueIndexes = new List<(string Name, bool IsAuto)>();
        while (await idxListReader.ReadAsync())
        {
            var idxName = idxListReader.GetString(1);
            var isUnique = idxListReader.GetInt32(2) == 1;
            if (isUnique)
                uniqueIndexes.Add((idxName, idxName.StartsWith("sqlite_autoindex_", StringComparison.Ordinal)));
        }
        await idxListReader.DisposeAsync();

        var oldAutoIndex = (string?)null;
        var oldRegularIndexes = new List<string>();

        foreach (var (idxName, isAuto) in uniqueIndexes)
        {
            using var infoCmd = db.Database.GetDbConnection().CreateCommand();
            infoCmd.CommandText = $"PRAGMA index_info({idxName})";
            using var infoReader = await infoCmd.ExecuteReaderAsync();
            var idxColumns = new List<string>();
            while (await infoReader.ReadAsync())
                idxColumns.Add(infoReader.GetString(2));
            await infoReader.DisposeAsync();

            // Detect old 2-column unique constraint: has hotel_id + date but NOT room_type
            var isOldConstraint = idxColumns.Count == 2
                && idxColumns.Contains("hotel_id", StringComparer.OrdinalIgnoreCase)
                && idxColumns.Contains("date", StringComparer.OrdinalIgnoreCase)
                && !idxColumns.Contains("room_type", StringComparer.OrdinalIgnoreCase);

            if (!isOldConstraint) continue;

            if (isAuto)
                oldAutoIndex = idxName;
            else
                oldRegularIndexes.Add(idxName);
        }

        // Drop regular indexes (safe to DROP INDEX)
        foreach (var oldIdx in oldRegularIndexes)
        {
            cmd.CommandText = $"DROP INDEX IF EXISTS {oldIdx}";
            await cmd.ExecuteNonQueryAsync();
        }

        // Inline UNIQUE constraint — must rebuild the table to remove it.
        // Also rebuild if the id column is missing PRIMARY KEY (corrupted by a previous table rebuild).
        if (oldAutoIndex is not null || pricesIdMissingPk)
        {
            // Gather current column definitions
            cmd.CommandText = "PRAGMA table_info(prices)";
            using var colReader = await cmd.ExecuteReaderAsync();
            var colDefs = new List<(string Name, string Type, int NotNull, string Default, int Pk)>();
            while (await colReader.ReadAsync())
            {
                colDefs.Add((
                    colReader.GetString(1),
                    colReader.GetString(2),
                    colReader.GetInt32(3),
                    colReader.IsDBNull(4) ? "" : colReader.GetString(4),
                    colReader.GetInt32(5)
                ));
            }
            await colReader.DisposeAsync();

            // Build CREATE TABLE for the replacement table with 3-column UNIQUE
            var colLines = new List<string>();
            var colNames = new List<string>();
            foreach (var (name, type, notNull, def, pk) in colDefs)
            {
                colNames.Add(name);
                var line = $"\"{name}\" {type}";
                if (notNull != 0) line += " NOT NULL";
                if (def.Length > 0) line += $" DEFAULT {def}";
                if (pk != 0) line += " PRIMARY KEY AUTOINCREMENT";
                colLines.Add(line);
            }
            colLines.Add("CONSTRAINT uq_hotel_date_room UNIQUE (\"hotel_id\", \"date\", \"room_type\")");

            var colsList = string.Join(", ", colLines);
            var colsCsv = string.Join(", ", colNames.Select(c => $"\"{c}\""));

            cmd.CommandText = $"CREATE TABLE prices_new ({colsList})";
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = $"INSERT INTO prices_new SELECT {colsCsv} FROM prices";
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = "DROP TABLE prices";
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = "ALTER TABLE prices_new RENAME TO prices";
            await cmd.ExecuteNonQueryAsync();

            // Recreate non-unique indexes that were on the old table
            cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_prices_HotelId ON prices (hotel_id)";
            await cmd.ExecuteNonQueryAsync();
            cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_prices_Date ON prices (date)";
            await cmd.ExecuteNonQueryAsync();
        }
        else
        {
            // No inline constraint to rebuild — just ensure the 3-column unique index exists
            cmd.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS uq_hotel_date_room ON prices (hotel_id, date, room_type)";
            await cmd.ExecuteNonQueryAsync();
        }
    }
    catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("no such table"))
    {
        // Table doesn't exist yet (fresh in-memory DB) — EnsureCreatedAsync will handle it
    }
}

// Required for integration testing
public partial class Program { }
