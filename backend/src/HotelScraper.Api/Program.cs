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
    // Strip "Data Source=" prefix if present, then resolve relative paths
    var path = databaseUrl.Replace("Data Source=", "");

    if (!Path.IsPathRooted(path))
        path = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));

    var dir = Path.GetDirectoryName(path);
    if (!string.IsNullOrWhiteSpace(dir))
        Directory.CreateDirectory(dir);

    return path;
}

// Required for integration testing
public partial class Program { }
