using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SafeShare.Data;
using SafeShare.Models;
using SafeShare.Services;

// The runtime image has no curl, so the Docker HEALTHCHECK runs the app itself in probe mode.
if (args.Contains("--healthcheck"))
{
    var port = (Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? "8080").Split(';', ',')[0];
    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var response = await http.GetAsync($"http://127.0.0.1:{port}/healthz");
        return response.IsSuccessStatusCode ? 0 : 1;
    }
    catch
    {
        return 1;
    }
}

var builder = WebApplication.CreateBuilder(args);

var settings = builder.Configuration.GetSection(SafeShareOptions.Section).Get<SafeShareOptions>() ?? new SafeShareOptions();
var dataDir = Path.GetFullPath(settings.DataDirectory, builder.Environment.ContentRootPath);
Directory.CreateDirectory(dataDir);

var dbPath = Path.Combine(dataDir, "safeshare.db");

builder.Services.Configure<SafeShareOptions>(builder.Configuration.GetSection(SafeShareOptions.Section));
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddDbContext<SecretDbContext>(o =>
    o.UseSqlite($"Data Source={dbPath};Cache=Shared"));
builder.Services.AddScoped<SecretService>();
builder.Services.AddScoped<RequestService>();
builder.Services.AddHostedService<ExpiredSecretCleanup>();

// Only antiforgery/TempData use these keys; secrets are encrypted in the browser with keys the server never sees.
builder.Services.AddDataProtection()
    .SetApplicationName("SafeShare")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));

builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));

// Base64 inflates the ciphertext by 4/3; leave headroom for the rest of the JSON.
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = settings.MaxSecretSizeKb * 1024L * 2 + 16 * 1024);

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("create", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("reveal", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
    // The requester's inbox polls this while waiting for the sender.
    o.AddPolicy("status", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SecretDbContext>();
    db.Database.Migrate();
    // WAL keeps readers from blocking the writer.
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}
app.UseStatusCodePagesWithReExecute("/Home/Status/{0}");

app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    // No inline or third-party script may run next to the decryption code.
    h.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
                              "connect-src 'self'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'";
    h["Referrer-Policy"] = "no-referrer";
    h.XContentTypeOptions = "nosniff";
    h.XFrameOptions = "DENY";
    h["X-Robots-Tag"] = "noindex, nofollow";
    if (!ctx.Request.Path.StartsWithSegments("/css") && !ctx.Request.Path.StartsWithSegments("/js"))
        h.CacheControl = "no-store";
    await next();
});

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();

app.MapGet("/healthz", () => Results.Ok("ok"));
app.MapControllers();

await app.RunAsync();
return 0;
