using System.ComponentModel.DataAnnotations;

namespace SafeShare.Models;

/// <summary>
/// A request for someone else to send a secret. The requester's browser holds the ECDH P-256 private key
/// (non-extractable, in IndexedDB); the server only stores the public key and, once the sender has filled
/// the request, ciphertext that only that private key can decrypt.
/// </summary>
public class SecretRequest
{
    [Key]
    [MaxLength(16)]
    public string Id { get; set; } = default!;

    /// <summary>Requester's public key, uncompressed P-256 point (65 bytes).</summary>
    public byte[] PublicKey { get; set; } = default!;

    /// <summary>What the requester asks for. Stored in plaintext, so it must not contain anything sensitive.</summary>
    [MaxLength(200)]
    public string? Description { get; set; }

    /// <summary>SHA-256 of the requester's access token; the token itself only lives in the requester's browser.</summary>
    public byte[] TokenHash { get; set; } = default!;

    public DateTime CreatedUtc { get; set; }

    /// <summary>When the auto-burn timer fires; null when no timer applies.</summary>
    public DateTime? ExpiresUtc { get; set; }

    public DateTime? FulfilledUtc { get; set; }

    // Filled in by the sender.
    public byte[]? EphemeralPublicKey { get; set; }
    public byte[]? Salt { get; set; }
    public byte[]? Iv { get; set; }
    public byte[]? Ciphertext { get; set; }

    public bool IsFulfilled => Ciphertext is not null;

    public bool IsExpired(DateTime nowUtc) => ExpiresUtc <= nowUtc;
}
