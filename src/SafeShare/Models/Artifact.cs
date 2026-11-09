using System.ComponentModel.DataAnnotations;

namespace SafeShare.Models;

/// <summary>
/// A password-protected HTML artifact (e.g. a UX prototype) shared with customers. Unlike a <see cref="Secret"/>
/// it can be opened any number of times; it is deleted only when its timer fires or its creator deletes it.
/// The encryption key is derived in the browser from the password, so the server only ever sees ciphertext.
/// </summary>
public class Artifact
{
    [Key]
    [MaxLength(16)]
    public string Id { get; set; } = default!;

    /// <summary>AES-256-GCM ciphertext (of a gzipped JSON payload with title and HTML) including the 16-byte tag.</summary>
    public byte[] Ciphertext { get; set; } = default!;

    /// <summary>12-byte AES-GCM nonce.</summary>
    public byte[] Iv { get; set; } = default!;

    /// <summary>16-byte salt for PBKDF2 and HKDF. Not secret.</summary>
    public byte[] Salt { get; set; } = default!;

    /// <summary>
    /// SHA-256 of the auth token the browser derives from the password via a separate HKDF label. The server checks
    /// it before releasing the ciphertext, which lets it count and limit wrong password attempts.
    /// </summary>
    public byte[] AuthHash { get; set; } = default!;

    /// <summary>SHA-256 of the creator's manage token (used to delete the artifact early).</summary>
    public byte[] ManageTokenHash { get; set; } = default!;

    public DateTime CreatedUtc { get; set; }

    /// <summary>When the artifact is deleted automatically. Always set: artifacts require a timer.</summary>
    public DateTime ExpiresUtc { get; set; }

    /// <summary>Wrong password attempts since the last successful open.</summary>
    public int FailedAttempts { get; set; }

    public int ViewCount { get; set; }

    public DateTime? LastViewedUtc { get; set; }

    public bool IsExpired(DateTime nowUtc) => ExpiresUtc <= nowUtc;
}
