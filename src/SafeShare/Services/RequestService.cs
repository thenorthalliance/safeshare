using Microsoft.EntityFrameworkCore;
using SafeShare.Data;
using SafeShare.Models;

namespace SafeShare.Services;

public enum FulfillStatus
{
    Ok,
    NotFound,
    AlreadyFulfilled,
}

public class RequestService(SecretDbContext db, TimeProvider clock)
{
    public async Task<SecretRequest> CreateAsync(CreateRequestBody body, TimeSpan? lifetime, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var request = new SecretRequest
        {
            Id = await Ids.NewUniqueAsync(id => db.Requests.AnyAsync(r => r.Id == id, ct)),
            PublicKey = body.PublicKey!,
            TokenHash = body.TokenHash!,
            Description = string.IsNullOrWhiteSpace(body.Description) ? null : body.Description.Trim(),
            CreatedUtc = now,
            ExpiresUtc = now + lifetime,
        };

        db.Requests.Add(request);
        await db.SaveChangesAsync(ct);
        return request;
    }

    /// <summary>Returns the request if it exists and its timer hasn't fired (expired ones are removed on access).</summary>
    public async Task<SecretRequest?> FindAsync(string id, CancellationToken ct = default)
    {
        var request = await db.Requests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null)
            return null;

        if (request.IsExpired(clock.GetUtcNow().UtcDateTime))
        {
            db.Requests.Remove(request);
            await db.SaveChangesAsync(ct);
            return null;
        }
        return request;
    }

    public async Task<SecretRequest?> FindForRequesterAsync(string id, byte[] token, CancellationToken ct = default)
    {
        var request = await FindAsync(id, ct);
        return request is not null && Ids.HashMatches(token, request.TokenHash) ? request : null;
    }

    /// <summary>
    /// Stores the sender's ciphertext. Each request accepts exactly one secret. The new timer is the stricter
    /// of the requester's timer and the sender's timer.
    /// </summary>
    public async Task<(FulfillStatus Status, DateTime? ExpiresUtc)> FulfillAsync(
        string id, FulfillRequestBody body, TimeSpan? senderLifetime, CancellationToken ct = default)
    {
        var request = await FindAsync(id, ct);
        if (request is null)
            return (FulfillStatus.NotFound, null);
        if (request.IsFulfilled)
            return (FulfillStatus.AlreadyFulfilled, null);

        var now = clock.GetUtcNow().UtcDateTime;
        var senderExpiry = now + senderLifetime;
        var expires = (request.ExpiresUtc, senderExpiry) switch
        {
            (null, var s) => s,
            (var r, null) => r,
            (var r, var s) => r < s ? r : s,
        };

        // Conditional update so two senders racing on the same link can't overwrite each other.
        var updated = await db.Requests
            .Where(r => r.Id == id && r.Ciphertext == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.EphemeralPublicKey, body.EphemeralPublicKey)
                .SetProperty(r => r.Salt, body.Salt)
                .SetProperty(r => r.Iv, body.Iv)
                .SetProperty(r => r.Ciphertext, body.Ciphertext)
                .SetProperty(r => r.FulfilledUtc, now)
                .SetProperty(r => r.ExpiresUtc, expires), ct);

        return updated == 1 ? (FulfillStatus.Ok, expires) : (FulfillStatus.AlreadyFulfilled, null);
    }

    /// <summary>Hands the ciphertext to the requester once and deletes the request in the same step.</summary>
    public async Task<SecretRequest?> RetrieveAsync(string id, byte[] token, CancellationToken ct = default)
    {
        var request = await FindForRequesterAsync(id, token, ct);
        if (request is null || !request.IsFulfilled)
            return null;

        var deleted = await db.Requests.Where(r => r.Id == id && r.Ciphertext != null).ExecuteDeleteAsync(ct);
        return deleted == 1 ? request : null;
    }

    public async Task<bool> CancelAsync(string id, byte[] token, CancellationToken ct = default)
    {
        var request = await FindForRequesterAsync(id, token, ct);
        if (request is null)
            return false;

        db.Requests.Remove(request);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
