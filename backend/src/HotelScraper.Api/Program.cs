using HotelScraper.Api.Configuration;
using HotelScraper.Api.Data;
using HotelScraper.Api.Endpoints;
using HotelScraper.Api.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using System.Security.Claims;
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
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentTenantService>();
builder.Services.AddScoped<AuthService>();

// ── Authentication & Authorization ─────────────────────────────────────
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "HotelScraper.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = 401; // API statt Redirect
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = 403;
            return Task.CompletedTask;
        };

        // Re-validate the principal against the database on every request. The cookie
        // itself is valid for 30 days (sliding), so without this check a deactivated
        // user — or a user of a deactivated tenant — would keep access until expiry.
        options.Events.OnValidatePrincipal = async ctx =>
        {
            var userIdClaim = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userIdClaim is null || !int.TryParse(userIdClaim, out var userId))
            {
                ctx.RejectPrincipal();
                ctx.HttpContext.Response.Cookies.Delete(options.Cookie.Name);
                return;
            }

            // Resolve a fresh DbContext via the factory — never capture a scoped
            // service here (captive dependency).
            var dbFactory = ctx.HttpContext.RequestServices.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null || !user.IsActive)
            {
                ctx.RejectPrincipal();
                ctx.HttpContext.Response.Cookies.Delete(options.Cookie.Name);
                return;
            }

            if (user.TenantId.HasValue)
            {
                var tenant = await db.Tenants.FindAsync(user.TenantId.Value);
                if (tenant is null || !tenant.IsActive)
                {
                    ctx.RejectPrincipal();
                    ctx.HttpContext.Response.Cookies.Delete(options.Cookie.Name);
                    return;
                }

                // Refresh the city claim so tenant city changes take effect immediately.
                if (ctx.Principal?.Identity is ClaimsIdentity identity)
                {
                    var oldCityClaim = identity.FindFirst("city");
                    if (oldCityClaim is null || oldCityClaim.Value != tenant.City)
                    {
                        if (oldCityClaim is not null)
                            identity.RemoveClaim(oldCityClaim);
                        identity.AddClaim(new Claim("city", tenant.City));
                        ctx.ReplacePrincipal(ctx.Principal!);
                        ctx.ShouldRenew = true;
                    }
                }
            }
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("admin", policy => policy.RequireRole("admin"));
});

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
    await db.Database.MigrateAsync();

    // Seed initial admin account from environment (only if none exists)
    var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    var adminEmail = builder.Configuration["ADMIN_EMAIL"] ?? options.AdminEmail;
    var adminPassword = builder.Configuration["ADMIN_PASSWORD"] ?? options.AdminPassword;

    if (!await db.Users.AnyAsync(u => u.Role == "admin"))
    {
        if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
        {
            logger.LogWarning(
                "ADMIN_EMAIL / ADMIN_PASSWORD not set — no admin account seeded. " +
                "Set both environment variables to create the initial admin.");
        }
        else
        {
            db.Users.Add(new AppUser
            {
                Email = adminEmail.Trim(),
                PasswordHash = auth.HashPassword(adminPassword),
                Role = "admin",
            });
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded initial admin account '{Email}'", adminEmail.Trim());
        }
    }
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
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapHotelEndpoints();
app.MapPriceEndpoints();
app.MapWatchlistEndpoints();
app.MapAdminEndpoints();

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
