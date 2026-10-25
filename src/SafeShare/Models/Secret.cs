using System.ComponentModel.DataAnnotations;

namespace SafeShare.Models;

/// <summary>
/// An end-to-end encrypted, one-time secret. The server only ever sees ciphertext: the key is generated
/// in the browser and travels in the URL fragment, which browsers never send to the server.
/// </summary>
public class Secret
{
    [Key]
    [MaxLength(16)]
    public string Id { get; set; } = default!;

    /// <summary>AES-256-GCM ciphertext including the 16-byte authentication tag.</summary>
    public byte[] Ciphertext { get; set; } = default!;

    /// <summary>12-byte AES-GCM nonce.</summary>
    public byte[] Iv { get; set; } = default!;

    /// <summary>16-byte salt used for HKDF (and PBKDF2 when a passphrase is set). Not secret.</summary>
    public byte[] Salt { get; set; } = default!;

    public bool HasPassphrase { get; set; }

    /// <summary>
    /// SHA-256 of the auth token the browser derives from the key (and passphrase) via a separate HKDF label.
    /// Lets the server check the reader holds the right key before releasing the ciphertext, without being able
    /// to derive the encryption key itself.
    /// </summary>
    public byte[] AuthHash { get; set; } = default!;

    /// <summary>SHA-256 of the creator's manage token (used to burn the secret early).</summary>
    public byte[] ManageTokenHash { get; set; } = default!;

    public DateTime CreatedUtc { get; set; }

    /// <summary>When the auto-burn timer fires; null when the creator chose no timer.</summary>
    public DateTime? ExpiresUtc { get; set; }

    public int FailedAttempts { get; set; }

    public bool IsExpired(DateTime nowUtc) => ExpiresUtc <= nowUtc;
}
