using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SafeShare.Data;
using SafeShare.Models;

namespace SafeShare.Services;

public enum RevealStatus
{
    Ok,
    NotFound,
    WrongKey,
    Destroyed,
}

public record RevealResult(RevealStatus Status, Secret? Secret = null, int RemainingAttempts = 0);

public class SecretService(SecretDbContext db, IOptions<SafeShareOptions> options, TimeProvider clock)
{
    private readonly SafeShareOptions _options = options.Value;

    public async Task<(Secret Secret, string ManageToken)> CreateAsync(CreateSecretBody body, TimeSpan? lifetime, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var manageToken = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));

        var secret = new Secret
        {
            Id = await Ids.NewUniqueAsync(id => db.Secrets.AnyAsync(s => s.Id == id, ct)),
            Ciphertext = body.Ciphertext!,
            Iv = body.Iv!,
            Salt = body.Salt!,
            AuthHash = body.AuthHash!,
            HasPassphrase = body.HasPassphrase,
            ManageTokenHash = SHA256.HashData(Convert.FromHexString(manageToken)),
            CreatedUtc = now,
            ExpiresUtc = now + lifetime,
        };

        db.Secrets.Add(secret);
        await db.SaveChangesAsync(ct);
        return (secret, manageToken);
    }

    /// <summary>Returns the secret if it exists and its timer hasn't fired (expired ones are removed on access).</summary>
    public async Task<Secret?> FindAsync(string id, CancellationToken ct = default)
    {
        var secret = await db.Secrets.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (secret is null)
            return null;

        if (secret.IsExpired(clock.GetUtcNow().UtcDateTime))
        {
            db.Secrets.Remove(secret);
            await db.SaveChangesAsync(ct);
            return null;
        }
        return secret;
    }

    /// <summary>
    /// Releases the ciphertext exactly once, and only to a caller that proves knowledge of the key.
    /// Too many wrong attempts destroy the secret, which caps online passphrase guessing.
    /// </summary>
    public async Task<RevealResult> RevealAsync(string id, byte[] authToken, CancellationToken ct = default)
    {
        var secret = await FindAsync(id, ct);
        if (secret is null)
            return new(RevealStatus.NotFound);

        var max = _options.MaxFailedAttempts;

        if (!Ids.HashMatches(authToken, secret.AuthHash))
        {
            // Increment in SQL, not in memory: with a read-modify-write, concurrent wrong attempts all read the
            // same count and together cost only one attempt, which would defeat the limit on passphrase guessing.
            await db.Secrets.Where(s => s.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.FailedAttempts, x => x.FailedAttempts + 1), ct);
            var failed = await db.Secrets.Where(s => s.Id == id)
                .Select(s => (int?)s.FailedAttempts)
                .FirstOrDefaultAsync(ct);
            if (failed is null)
                return new(RevealStatus.NotFound);   // burned or revealed meanwhile

            var remaining = max - failed.Value;
            if (remaining > 0)
                return new(RevealStatus.WrongKey, RemainingAttempts: remaining);

            await db.Secrets.Where(s => s.Id == id).ExecuteDeleteAsync(ct);
            return new(RevealStatus.Destroyed);
        }

        // The conditional delete is what makes it one-time: of two concurrent readers only one removes the row.
        // It also refuses a secret that reached the attempt limit while this request was in flight.
        var deleted = await db.Secrets.Where(s => s.Id == id && s.FailedAttempts < max).ExecuteDeleteAsync(ct);
        return deleted == 1 ? new(RevealStatus.Ok, secret) : new(RevealStatus.NotFound);
    }

    public async Task<Secret?> FindForManageAsync(string id, string token, CancellationToken ct = default)
    {
        var secret = await FindAsync(id, ct);
        return secret is not null && TokenMatches(secret, token) ? secret : null;
    }

    public async Task<bool> BurnAsync(string id, string token, CancellationToken ct = default)
    {
        var secret = await FindForManageAsync(id, token, ct);
        if (secret is null)
            return false;

        db.Secrets.Remove(secret);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static bool TokenMatches(Secret secret, string token)
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
        return Ids.HashMatches(raw, secret.ManageTokenHash);
    }
}
