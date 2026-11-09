using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SafeShare.Controllers;
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
builder.Services.AddScoped<ArtifactService>();
builder.Services.AddHostedService<ExpiredSecretCleanup>();

// Only antiforgery/TempData use these keys; secrets are encrypted in the browser with keys the server never sees.
builder.Services.AddDataProtection()
    .SetApplicationName("SafeShare")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));

builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));

// Base64 inflates the ciphertext by 4/3; leave headroom for the rest of the JSON.
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = settings.MaxSecretSizeKb * 1024L * 2 + 16 * 1024);
// Artifacts are much larger than secrets; only their upload endpoint gets the higher limit (see below).
var artifactBodyLimit = settings.MaxArtifactSizeKb * 1024L * 4 / 3 + 64 * 1024;

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
    if (ctx.Request.Path.Equals(ArtifactController.FramePath, StringComparison.OrdinalIgnoreCase))
    {
        // The frame that renders a shared artifact runs the artifact's own HTML and scripts, which may use inline
        // code and CDNs. The CSP sandbox (no allow-same-origin) puts it on an opaque origin, so that code can't read
        // this app's cookies, storage or pages, and it can only be embedded by this app's own viewer page.
        h.ContentSecurityPolicy = "sandbox allow-scripts allow-forms allow-modals allow-popups allow-popups-to-escape-sandbox allow-downloads; " +
                                  "default-src * data: blob: 'unsafe-inline' 'unsafe-eval'; frame-ancestors 'self'";
        h.XFrameOptions = "SAMEORIGIN";
    }
    else
    {
        // No inline or third-party script may run next to the decryption code.
        h.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
                                  "connect-src 'self'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'";
        h.XFrameOptions = "DENY";
    }
    h["Referrer-Policy"] = "no-referrer";
    h.XContentTypeOptions = "nosniff";
    h["X-Robots-Tag"] = "noindex, nofollow";
    // Clipboard is left allowed on purpose: the Copy buttons use navigator.clipboard.writeText.
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
    h["Cross-Origin-Opener-Policy"] = "same-origin";
    h["Cross-Origin-Resource-Policy"] = "same-origin";
    if (!ctx.Request.Path.StartsWithSegments("/css") && !ctx.Request.Path.StartsWithSegments("/js"))
        h.CacheControl = "no-store";

    if (HttpMethods.IsPost(ctx.Request.Method) && ctx.Request.Path.Equals(ArtifactController.CreatePath, StringComparison.OrdinalIgnoreCase)
        && ctx.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodyLimit)
    {
        bodyLimit.MaxRequestBodySize = artifactBodyLimit;
    }
    await next();
});

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();

app.MapGet("/healthz", () => Results.Ok("ok"));
app.MapControllers();

await app.RunAsync();
return 0;
