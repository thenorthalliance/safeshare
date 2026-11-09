using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SafeShare.Data;
using SafeShare.Models;

namespace SafeShare.Services;

public record OpenArtifactResult(RevealStatus Status, Artifact? Artifact = null, int RemainingAttempts = 0);

public class ArtifactService(SecretDbContext db, IOptions<SafeShareOptions> options, TimeProvider clock)
{
    private readonly SafeShareOptions _options = options.Value;

    public async Task<(Artifact Artifact, string ManageToken)> CreateAsync(CreateArtifactBody body, TimeSpan lifetime, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var manageToken = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));

        var artifact = new Artifact
        {
            Id = await Ids.NewUniqueAsync(id => db.Artifacts.AnyAsync(a => a.Id == id, ct)),
            Ciphertext = body.Ciphertext!,
            Iv = body.Iv!,
            Salt = body.Salt!,
            AuthHash = body.AuthHash!,
            ManageTokenHash = SHA256.HashData(Convert.FromHexString(manageToken)),
            CreatedUtc = now,
            ExpiresUtc = now + lifetime,
        };

        db.Artifacts.Add(artifact);
        await db.SaveChangesAsync(ct);
        return (artifact, manageToken);
    }

    /// <summary>Returns the artifact if it exists and its timer hasn't fired (expired ones are removed on access).</summary>
    public async Task<Artifact?> FindAsync(string id, CancellationToken ct = default)
    {
        var artifact = await db.Artifacts.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (artifact is null)
            return null;

        if (artifact.IsExpired(clock.GetUtcNow().UtcDateTime))
        {
            db.Artifacts.Remove(artifact);
            await db.SaveChangesAsync(ct);
            return null;
        }
        return artifact;
    }

    /// <summary>
    /// Releases the ciphertext to a caller that proves knowledge of the password. Unlike a secret, the artifact stays.
    /// Too many wrong attempts in a row destroy it, which caps online password guessing; a correct password resets
    /// the counter, so customers who mistype now and then over the artifact's lifetime don't destroy it.
    /// </summary>
    public async Task<OpenArtifactResult> OpenAsync(string id, byte[] authToken, CancellationToken ct = default)
    {
        var artifact = await FindAsync(id, ct);
        if (artifact is null)
            return new(RevealStatus.NotFound);

        var max = _options.MaxFailedAttempts;

        if (!Ids.HashMatches(authToken, artifact.AuthHash))
        {
            // Increment in SQL so concurrent wrong attempts can't share one count (see SecretService.RevealAsync).
            await db.Artifacts.Where(a => a.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.FailedAttempts, x => x.FailedAttempts + 1), ct);
            var failed = await db.Artifacts.Where(a => a.Id == id)
                .Select(a => (int?)a.FailedAttempts)
                .FirstOrDefaultAsync(ct);
            if (failed is null)
                return new(RevealStatus.NotFound);

            var remaining = max - failed.Value;
            if (remaining > 0)
                return new(RevealStatus.WrongKey, RemainingAttempts: remaining);

            await db.Artifacts.Where(a => a.Id == id).ExecuteDeleteAsync(ct);
            return new(RevealStatus.Destroyed);
        }

        // Conditional, so an artifact that reached the attempt limit while this request was in flight is refused.
        var now = clock.GetUtcNow().UtcDateTime;
        var updated = await db.Artifacts
            .Where(a => a.Id == id && a.FailedAttempts < max)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.FailedAttempts, 0)
                .SetProperty(a => a.ViewCount, a => a.ViewCount + 1)
                .SetProperty(a => a.LastViewedUtc, now), ct);
        return updated == 1 ? new(RevealStatus.Ok, artifact) : new(RevealStatus.NotFound);
    }

    public async Task<Artifact?> FindForManageAsync(string id, string token, CancellationToken ct = default)
    {
        var artifact = await FindAsync(id, ct);
        return artifact is not null && TokenMatches(artifact, token) ? artifact : null;
    }

    public async Task<bool> DeleteAsync(string id, string token, CancellationToken ct = default)
    {
        var artifact = await FindForManageAsync(id, token, ct);
        if (artifact is null)
            return false;

        db.Artifacts.Remove(artifact);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static bool TokenMatches(Artifact artifact, string token)
    {
        byte[] raw;
        try
        {
            raw = Convert.FromHexString(token);
        }
        catch (FormatException)
        {
            return false;
        }
        return Ids.HashMatches(raw, artifact.ManageTokenHash);
    }
}
