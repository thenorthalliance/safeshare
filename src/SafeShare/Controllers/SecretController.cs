using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SafeShare.Models;
using SafeShare.Services;

namespace SafeShare.Controllers;

[Route("s/{id}")]
public class SecretController(SecretService secrets) : Controller
{
    /// <summary>Landing page for a share link. Does not consume the secret, so chat link previews can't burn it.</summary>
    [HttpGet("")]
    public async Task<IActionResult> Open(string id, CancellationToken ct)
    {
        var secret = await secrets.FindAsync(id, ct);
        if (secret is null)
            return Gone();

        return View(new OpenViewModel
        {
            Id = secret.Id,
            HasPassphrase = secret.HasPassphrase,
            Salt = Convert.ToBase64String(secret.Salt),
            ExpiresUtc = secret.ExpiresUtc,
        });
    }

    [HttpPost("reveal")]
    [EnableRateLimiting("reveal")]
    public async Task<IActionResult> Reveal(string id, [FromBody] RevealBody request, CancellationToken ct)
    {
        if (request.AuthToken?.Length != 32)
            return BadRequest(new ApiError("Malformed request."));

        var result = await secrets.RevealAsync(id, request.AuthToken, ct);
        return result.Status switch
        {
            RevealStatus.Ok => Ok(new RevealResponse(result.Secret!.Ciphertext, result.Secret.Iv)),
            RevealStatus.WrongKey => StatusCode(StatusCodes.Status403Forbidden,
                new ApiError("Wrong key or passphrase.", result.RemainingAttempts)),
            RevealStatus.Destroyed => StatusCode(StatusCodes.Status410Gone,
                new ApiError("Too many wrong attempts. The secret has been destroyed.")),
            _ => NotFound(new ApiError("This secret doesn't exist, has expired, or has already been viewed.")),
        };
    }

    [HttpGet("manage/{token}")]
    public async Task<IActionResult> Manage(string id, string token, CancellationToken ct) =>
        View(new ManageViewModel { Id = id, Token = token, Secret = await secrets.FindForManageAsync(id, token, ct) });

    [HttpPost("manage/{token}")]
    [ActionName("Manage")]
    public async Task<IActionResult> Burn(string id, string token, CancellationToken ct)
    {
        TempData["flash"] = await secrets.BurnAsync(id, token, ct)
            ? "The secret has been destroyed."
            : "This secret no longer exists.";
        return Redirect("/");
    }

    private ViewResult Gone()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        return View("Gone");
    }
}
