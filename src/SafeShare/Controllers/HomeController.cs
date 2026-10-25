using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SafeShare.Models;
using SafeShare.Services;

namespace SafeShare.Controllers;

public class HomeController(SecretService secrets, IOptions<SafeShareOptions> options) : Controller
{
    private const int IvSize = 12;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int GcmTagSize = 16;

    private readonly SafeShareOptions _options = options.Value;

    [HttpGet("/share")]
    public IActionResult Index() => View(new HomeViewModel
    {
        MaxSecretSizeKb = _options.MaxSecretSizeKb,
        Timer = _options.TimerField(),
    });

    /// <summary>Stores an already-encrypted secret. The plaintext and key never reach this endpoint.</summary>
    [HttpPost("/api/secrets")]
    [EnableRateLimiting("create")]
    public async Task<IActionResult> Create([FromBody] CreateSecretBody body, CancellationToken ct)
    {
        if (body.Iv?.Length != IvSize || body.Salt?.Length != SaltSize || body.AuthHash?.Length != HashSize)
            return BadRequest(new ApiError("Malformed encryption parameters."));
        if (body.Ciphertext is null || body.Ciphertext.Length <= GcmTagSize)
            return BadRequest(new ApiError("Secret cannot be empty."));
        if (body.Ciphertext.Length > _options.MaxSecretSizeKb * 1024 + GcmTagSize)
            return BadRequest(new ApiError($"Secret is larger than {_options.MaxSecretSizeKb} KB."));
        if (!_options.TryResolveLifetime(body.LifetimeMinutes, out var lifetime, out var error))
            return BadRequest(new ApiError(error!));

        var (secret, manageToken) = await secrets.CreateAsync(body, lifetime, ct);
        return Ok(new CreateSecretResponse(secret.Id, manageToken, AsUtc(secret.ExpiresUtc)));
    }

    [HttpGet("/about")]
    public IActionResult About() => View(_options);

    [Route("/Home/Error")]
    public IActionResult Error() => View("Status", 500);

    [Route("/Home/Status/{code:int}")]
    public IActionResult Status(int code) => View("Status", code);

    internal static DateTime? AsUtc(DateTime? value) =>
        value is null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
}
