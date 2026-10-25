namespace SafeShare.Models;

public class SafeShareOptions
{
    public const string Section = "SafeShare";

    /// <summary>Directory holding the SQLite database and data-protection keys.</summary>
    public string DataDirectory { get; set; } = "App_Data";

    /// <summary>Maximum size of the plaintext secret.</summary>
    public int MaxSecretSizeKb { get; set; } = 64;

    /// <summary>Wrong key/passphrase attempts allowed before a shared secret is destroyed.</summary>
    public int MaxFailedAttempts { get; set; } = 5;

    public int CleanupIntervalMinutes { get; set; } = 1;

    public string SiteName { get; set; } = "SafeShare";

    /// <summary>Whether users may switch the auto-burn timer off (secret then lives until read or destroyed).</summary>
    public bool AllowNoExpiry { get; set; } = true;

    /// <summary>Timer value pre-filled in forms.</summary>
    public int DefaultLifetimeMinutes { get; set; } = 24 * 60;

    /// <summary>Longest timer a user may choose.</summary>
    public int MaxLifetimeMinutes { get; set; } = 30 * 24 * 60;

    public TimerFieldModel TimerField() => new()
    {
        AllowNoExpiry = AllowNoExpiry,
        DefaultMinutes = Math.Clamp(DefaultLifetimeMinutes, 1, MaxLifetimeMinutes),
        MaxMinutes = MaxLifetimeMinutes,
    };

    /// <summary>
    /// Validates a user-chosen timer. <paramref name="minutes"/> null means "no timer".
    /// </summary>
    public bool TryResolveLifetime(int? minutes, out TimeSpan? lifetime, out string? error)
    {
        lifetime = null;
        error = null;

        if (minutes is null)
        {
            if (!AllowNoExpiry)
                error = "An auto-burn timer is required.";
            return error is null;
        }
        if (minutes < 1 || minutes > MaxLifetimeMinutes)
        {
            error = $"The timer must be between 1 minute and {MaxLifetimeMinutes} minutes.";
            return false;
        }

        lifetime = TimeSpan.FromMinutes(minutes.Value);
        return true;
    }
}
