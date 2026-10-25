using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SafeShare.Data;
using SafeShare.Models;

namespace SafeShare.Services;

/// <summary>Burns secrets and requests whose auto-burn timer has fired.</summary>
public class ExpiredSecretCleanup(
    IServiceScopeFactory scopeFactory,
    IOptions<SafeShareOptions> options,
    TimeProvider clock,
    ILogger<ExpiredSecretCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, options.Value.CleanupIntervalMinutes));
        using var timer = new PeriodicTimer(interval);

        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SecretDbContext>();
                var now = clock.GetUtcNow().UtcDateTime;

                var secrets = await db.Secrets.Where(s => s.ExpiresUtc <= now).ExecuteDeleteAsync(stoppingToken);
                var requests = await db.Requests.Where(r => r.ExpiresUtc <= now).ExecuteDeleteAsync(stoppingToken);
                if (secrets + requests > 0)
                    logger.LogInformation("Burned {Secrets} expired secret(s) and {Requests} expired request(s)", secrets, requests);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Expired secret cleanup failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
