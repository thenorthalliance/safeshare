using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SafeShare.Models;
using SafeShare.Services;
using static SafeShare.Controllers.HomeController;

namespace SafeShare.Controllers;

/// <summary>
/// "Request a secret": the requester publishes a public key, the sender encrypts to it, and only the
/// requester's browser (which holds the private key) can decrypt.
/// </summary>
public class RequestController(RequestService requests, IOptions<SafeShareOptions> options) : Controller
{
    private const int PublicKeySize = 65;   // uncompressed P-256 point: 0x04 || X || Y
    private const int HashSize = 32;
    private const int SaltSize = 16;
    private const int IvSize = 12;
    private const int GcmTagSize = 16;

    private readonly SafeShareOptions _options = options.Value;

    [HttpGet("/request")]
    public IActionResult New() => View(new NewRequestViewModel
    {
        Timer = _options.TimerField(),
    });

    [HttpGet("/requests")]
    public IActionResult Mine() => View();

    [HttpPost("/api/requests")]
    [EnableRateLimiting("create")]
    public async Task<IActionResult> Create([FromBody] CreateRequestBody body, CancellationToken ct)
    {
        if (!IsPublicKey(body.PublicKey) || body.TokenHash?.Length != HashSize)
            return BadRequest(new ApiError("Malformed key parameters."));
        if (body.Description?.Length > 200)
            return BadRequest(new ApiError("Description is too long (max 200 characters)."));
        if (!_options.TryResolveLifetime(body.LifetimeMinutes, out var lifetime, out var error))
            return BadRequest(new ApiError(error!));

        var request = await requests.CreateAsync(body, lifetime, ct);
        return Ok(new CreateRequestResponse(request.Id, AsUtc(request.ExpiresUtc)));
    }

    /// <summary>The page the requester sends to the person who has the secret.</summary>
    [HttpGet("/r/{id}")]
    public async Task<IActionResult> Fulfill(string id, CancellationToken ct)
    {
        var request = await requests.FindAsync(id, ct);
        if (request is null || request.IsFulfilled)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return View("Gone");
        }

        return View(new FulfillViewModel
        {
            Id = request.Id,
            Description = request.Description,
            PublicKey = Convert.ToBase64String(request.PublicKey),
            ExpiresUtc = request.ExpiresUtc,
            MaxSecretSizeKb = _options.MaxSecretSizeKb,
            Timer = _options.TimerField(),
        });
    }

    [HttpPost("/api/requests/{id}/fulfill")]
    [EnableRateLimiting("create")]
    public async Task<IActionResult> FulfillPost(string id, [FromBody] FulfillRequestBody body, CancellationToken ct)
    {
        if (!IsPublicKey(body.EphemeralPublicKey) || body.Salt?.Length != SaltSize || body.Iv?.Length != IvSize)
            return BadRequest(new ApiError("Malformed encryption parameters."));
        if (body.Ciphertext is null || body.Ciphertext.Length <= GcmTagSize)
            return BadRequest(new ApiError("Secret cannot be empty."));
        if (body.Ciphertext.Length > _options.MaxSecretSizeKb * 1024 + GcmTagSize)
            return BadRequest(new ApiError($"Secret is larger than {_options.MaxSecretSizeKb} KB."));
        if (!_options.TryResolveLifetime(body.LifetimeMinutes, out var lifetime, out var error))
            return BadRequest(new ApiError(error!));

        var (status, expires) = await requests.FulfillAsync(id, body, lifetime, ct);
        return status switch
        {
            FulfillStatus.Ok => Ok(new FulfillResponse(AsUtc(expires))),
            FulfillStatus.AlreadyFulfilled => Conflict(new ApiError("A secret has already been sent for this request.")),
            _ => NotFound(new ApiError("This request doesn't exist or has expired.")),
        };
    }

    /// <summary>The requester's own page. Everything it shows comes from this browser's IndexedDB and the status API.</summary>
    [HttpGet("/r/{id}/inbox")]
    public IActionResult Inbox(string id) => View(model: id);

    [HttpPost("/api/requests/{id}/status")]
    [EnableRateLimiting("status")]
    public async Task<IActionResult> Status(string id, [FromBody] TokenBody body, CancellationToken ct)
    {
        var request = body.Token is null ? null : await requests.FindForRequesterAsync(id, body.Token, ct);
        if (request is null)
            return NotFound(new ApiError("This request is gone: it expired, was cancelled, or was already opened."));

        return Ok(new RequestStatusResponse(
            request.IsFulfilled ? "fulfilled" : "pending", AsUtc(request.ExpiresUtc), AsUtc(request.FulfilledUtc)));
    }

    [HttpPost("/api/requests/{id}/retrieve")]
    [EnableRateLimiting("reveal")]
    public async Task<IActionResult> Retrieve(string id, [FromBody] TokenBody body, CancellationToken ct)
    {
        var request = body.Token is null ? null : await requests.RetrieveAsync(id, body.Token, ct);
        if (request is null)
            return NotFound(new ApiError("There is no secret waiting for this request."));

        return Ok(new RetrieveResponse(request.EphemeralPublicKey!, request.Salt!, request.Iv!, request.Ciphertext!));
    }

    [HttpPost("/api/requests/{id}/cancel")]
    public async Task<IActionResult> Cancel(string id, [FromBody] TokenBody body, CancellationToken ct) =>
        body.Token is not null && await requests.CancelAsync(id, body.Token, ct)
            ? Ok()
            : NotFound(new ApiError("This request no longer exists."));

    private static bool IsPublicKey(byte[]? key) => key?.Length == PublicKeySize && key[0] == 0x04;
}
