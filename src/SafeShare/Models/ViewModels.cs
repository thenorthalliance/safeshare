namespace SafeShare.Models;

// byte[] properties are (de)serialized as standard base64 by System.Text.Json.
// LifetimeMinutes: null means "no auto-burn timer".

public class CreateSecretBody
{
    public byte[]? Ciphertext { get; set; }
    public byte[]? Iv { get; set; }
    public byte[]? Salt { get; set; }
    public byte[]? AuthHash { get; set; }
    public bool HasPassphrase { get; set; }
    public int? LifetimeMinutes { get; set; }
}

public record CreateSecretResponse(string Id, string ManageToken, DateTime? ExpiresUtc);

public class RevealBody
{
    public byte[]? AuthToken { get; set; }
}

public record RevealResponse(byte[] Ciphertext, byte[] Iv);

public record ApiError(string Error, int? RemainingAttempts = null);

public class CreateRequestBody
{
    public byte[]? PublicKey { get; set; }
    public byte[]? TokenHash { get; set; }
    public string? Description { get; set; }
    public int? LifetimeMinutes { get; set; }
}

public record CreateRequestResponse(string Id, DateTime? ExpiresUtc);

public class FulfillRequestBody
{
    public byte[]? EphemeralPublicKey { get; set; }
    public byte[]? Salt { get; set; }
    public byte[]? Iv { get; set; }
    public byte[]? Ciphertext { get; set; }
    public int? LifetimeMinutes { get; set; }
}

public record FulfillResponse(DateTime? ExpiresUtc);

public class TokenBody
{
    public byte[]? Token { get; set; }
}

public record RequestStatusResponse(string Status, DateTime? ExpiresUtc, DateTime? FulfilledUtc);

public record RetrieveResponse(byte[] EphemeralPublicKey, byte[] Salt, byte[] Iv, byte[] Ciphertext);

public class TimerFieldModel
{
    public bool AllowNoExpiry { get; set; }
    public int DefaultMinutes { get; set; }
    public int MaxMinutes { get; set; }
    public string Label { get; set; } = "Auto-burn timer";
}

public class HomeViewModel
{
    public int MaxSecretSizeKb { get; set; }
    public TimerFieldModel Timer { get; set; } = new();
}

public class OpenViewModel
{
    public string Id { get; set; } = default!;
    public bool HasPassphrase { get; set; }
    public string Salt { get; set; } = default!;
    public DateTime? ExpiresUtc { get; set; }
}

public class ManageViewModel
{
    public string Id { get; set; } = default!;
    public string Token { get; set; } = default!;
    public Secret? Secret { get; set; }
}

public class NewRequestViewModel
{
    public TimerFieldModel Timer { get; set; } = new();
}

public class FulfillViewModel
{
    public string Id { get; set; } = default!;
    public string? Description { get; set; }
    public string PublicKey { get; set; } = default!;
    public DateTime? ExpiresUtc { get; set; }
    public int MaxSecretSizeKb { get; set; }
    public TimerFieldModel Timer { get; set; } = new();
}
