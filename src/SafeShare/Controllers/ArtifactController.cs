using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SafeShare.Models;
using SafeShare.Services;
using static SafeShare.Controllers.HomeController;

namespace SafeShare.Controllers;

/// <summary>
/// "Share an artifact": a password-protected HTML page (e.g. a UX prototype) that customers can open as often as
/// they like until it expires or its creator deletes it. Encryption and decryption happen in the browser.
/// </summary>
public class ArtifactController(ArtifactService artifacts, IOptions<SafeShareOptions> options) : Controller
{
    /// <summary>Path of the sandboxed frame that renders a decrypted artifact. Program.cs gives it its own headers.</summary>
    public const string FramePath = "/a/_frame";
    public const string CreatePath = "/api/artifacts";

    private const int IvSize = 12;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int GcmTagSize = 16;

    private readonly SafeShareOptions _options = options.Value;

    [HttpGet("/artifact")]
    public IActionResult New() => View(new NewArtifactViewModel
    {
        MaxArtifactSizeKb = _options.MaxArtifactSizeKb,
        Timer = ArtifactTimer(),
    });

    /// <summary>Stores an already-encrypted artifact. The HTML and the password never reach this endpoint.</summary>
    [HttpPost(CreatePath)]
    [EnableRateLimiting("create")]
    public async Task<IActionResult> Create([FromBody] CreateArtifactBody body, CancellationToken ct)
    {
        if (body.Iv?.Length != IvSize || body.Salt?.Length != SaltSize || body.AuthHash?.Length != HashSize)
            return BadRequest(new ApiError("Malformed encryption parameters."));
        if (body.Ciphertext is null || body.Ciphertext.Length <= GcmTagSize)
            return BadRequest(new ApiError("Artifact cannot be empty."));
        if (body.Ciphertext.Length > _options.MaxArtifactSizeKb * 1024 + GcmTagSize)
            return BadRequest(new ApiError($"Artifact is larger than {_options.MaxArtifactSizeKb / 1024.0:0.#} MB after compression."));
        // Shared artifacts live through many views, so they always get a timer, whatever AllowNoExpiry says.
        if (body.LifetimeMinutes is null)
            return BadRequest(new ApiError("An expiry time is required."));
        if (!_options.TryResolveLifetime(body.LifetimeMinutes, out var lifetime, out var error))
            return BadRequest(new ApiError(error!));

        var (artifact, manageToken) = await artifacts.CreateAsync(body, lifetime!.Value, ct);
        return Ok(new CreateArtifactResponse(artifact.Id, manageToken, AsUtc(artifact.ExpiresUtc)!.Value));
    }

    /// <summary>Password prompt and viewer. Does not change anything, so link previews are harmless.</summary>
    [HttpGet("/a/{id}")]
    public async Task<IActionResult> Open(string id, CancellationToken ct)
    {
        var artifact = await artifacts.FindAsync(id, ct);
        if (artifact is null)
            return Gone();

        return View(new OpenArtifactViewModel
        {
            Id = artifact.Id,
            Salt = Convert.ToBase64String(artifact.Salt),
            ExpiresUtc = artifact.ExpiresUtc,
        });
    }

    [HttpPost("/a/{id}/open")]
    [EnableRateLimiting("reveal")]
    public async Task<IActionResult> OpenPost(string id, [FromBody] RevealBody request, CancellationToken ct)
    {
        if (request.AuthToken?.Length != HashSize)
            return BadRequest(new ApiError("Malformed request."));

        var result = await artifacts.OpenAsync(id, request.AuthToken, ct);
        return result.Status switch
        {
            RevealStatus.Ok => Ok(new RevealResponse(result.Artifact!.Ciphertext, result.Artifact.Iv)),
            RevealStatus.WrongKey => StatusCode(StatusCodes.Status403Forbidden,
                new ApiError("Wrong password.", result.RemainingAttempts)),
            RevealStatus.Destroyed => StatusCode(StatusCodes.Status410Gone,
                new ApiError("Too many wrong passwords. The shared page has been deleted.")),
            _ => NotFound(new ApiError("This shared page doesn't exist, has expired, or was deleted.")),
        };
    }

    /// <summary>
    /// Empty shell that the viewer loads in a sandboxed iframe and fills with the decrypted HTML via postMessage.
    /// It runs on an opaque origin, so the artifact's scripts can't reach this app's cookies, storage or pages.
    /// </summary>
    [HttpGet(FramePath)]
    public IActionResult Frame() => View();

    [HttpGet("/a/{id}/manage/{token}")]
    public async Task<IActionResult> Manage(string id, string token, CancellationToken ct) =>
        View(new ManageArtifactViewModel { Id = id, Token = token, Artifact = await artifacts.FindForManageAsync(id, token, ct) });

    [HttpPost("/a/{id}/manage/{token}")]
    [ActionName("Manage")]
    public async Task<IActionResult> Delete(string id, string token, CancellationToken ct)
    {
        TempData["flash"] = await artifacts.DeleteAsync(id, token, ct)
            ? "The shared artifact has been deleted."
            : "This artifact no longer exists.";
        // The query string lets artifact.js drop it from this browser's list.
        return Redirect($"/artifact?deleted={Uri.EscapeDataString(id)}");
    }

    private TimerFieldModel ArtifactTimer()
    {
        var timer = _options.TimerField();
        timer.AllowNoExpiry = false;
        timer.Label = "Available for";
        return timer;
    }

    private ViewResult Gone()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        return View("Gone");
    }
}
