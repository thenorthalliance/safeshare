# SafeShare security audit, 2026-10-26

Site: https://safeshare.ignexternal.duckdns.org
Code: `C:\git\safeshare` at commit `cbad892` (origin `github.com/thenorthalliance/safeshare`)
Auditor: Claude (Cowork), at the request of Mateusz Lucjanek

## Summary

SafeShare is in good shape. Its encryption design is sound: AES-256-GCM, HKDF with separate labels, PBKDF2-SHA256 at 600k iterations, and ECDH P-256 bound to both keys and the request ID. The server only ever sees ciphertext. The CSP is strict and allows no inline script, and one-time reveal is enforced with a conditional delete. No critical or high-severity issues were found.

There are **three medium findings**:

1. A race in the failed-attempt counter weakens the 5-attempt limit on passphrase guessing.
2. The "request a secret" flow does not authenticate the sender. Anyone who has the request link can send a forged secret, or fill the request first to block the real sender.
3. The app's plain-HTTP port is published on the Docker host. If it can be reached directly, it bypasses TLS and lets clients forge the IP address used for rate limiting. This could not be checked from here.

The rest are low-severity hardening items: three missing headers, the antiforgery cookie's Secure flag, a token in the URL path, and security.txt.

Local, approximate grades from the observed headers (not the services' own output):

- **MDN Observatory-style:** 110 / A+, provided NPM's "Force SSL" redirect is on. Without the redirect it is 80 / B+.
- **securityheaders.com header set:** B. Only `Permissions-Policy` is missing.

## What needs attention (ranked)

| # | Severity | Finding | Fix | Effort (est.) |
|---|---|---|---|---|
| 1 | Medium | Failed-attempt counter race | Atomic increment | 1 h |
| 2 | Medium | Request flow: no sender authentication, first sender wins | Warn in the UI; consider a sender code | 1–4 h |
| 3 | Medium (unverified) | App port 3452 published on the Docker host | Remove the published port, or firewall it | 0.5 h |
| 4 | Low | Anyone with the ID can destroy a secret with 5 wrong attempts | Accept and document, or rate-limit per ID | 0.5 h |
| 5 | Low | No `Permissions-Policy`, `Cross-Origin-Opener-Policy` or `Cross-Origin-Resource-Policy` | Add to the header middleware | 0.25 h |
| 6 | Low | Antiforgery cookie not marked `Secure` (framework default) | `Cookie.SecurePolicy = Always` | 0.25 h |
| 7 | Low | Manage token sits in the URL path, so it ends up in NPM access logs | Move it to the fragment and POST it | 1 h |
| 8 | Low | HSTS `preload` without `includeSubDomains`; no HSTS on JS/CSS | Ignore for duckdns; review for noaignite.tech | — |
| 9 | Info | No `/.well-known/security.txt` | Add one | 0.25 h |
| 10 | Info | README names an internal Git host and admin account | Remove from README | 5 min |
| 11 | Info | `/Home/Error` answers 200 when opened directly | Set the status to 500 | 5 min |
| 12 | Info | Dependency vulnerability scan not run | Run `dotnet list package --vulnerable` | 5 min |

### 1. Failed-attempt counter race (Medium)

`SecretService.RevealAsync` reads the row, runs `secret.FailedAttempts++` and saves. The model has no concurrency token, so EF writes `SET FailedAttempts = <value read>`. When several wrong attempts arrive at the same time, they all read N and all write N+1. A burst of 20 parallel guesses (the per-IP limit), spread over several IPs, costs only one attempt, so the "5 attempts, then destroyed" rule stops limiting online passphrase guessing.

The risk only applies when an attacker already has the link **and** the key but not the passphrase. That is exactly the case the passphrase exists for.

Fix: increment and check atomically in SQL.

```csharp
if (!Ids.HashMatches(authToken, secret.AuthHash))
{
    await db.Secrets.Where(s => s.Id == id)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.FailedAttempts, x => x.FailedAttempts + 1), ct);
    var failed = await db.Secrets.Where(s => s.Id == id).Select(s => (int?)s.FailedAttempts).FirstOrDefaultAsync(ct);
    if (failed is null) return new(RevealStatus.NotFound);
    var remaining = _options.MaxFailedAttempts - failed.Value;
    if (remaining > 0) return new(RevealStatus.WrongKey, RemainingAttempts: remaining);
    await db.Secrets.Where(s => s.Id == id).ExecuteDeleteAsync(ct);
    return new(RevealStatus.Destroyed);
}
// success path: only release while still under the limit
var deleted = await db.Secrets
    .Where(s => s.Id == id && s.FailedAttempts < _options.MaxFailedAttempts)
    .ExecuteDeleteAsync(ct);
```

### 2. Request flow: anyone with the request link can send (Medium, design)

The request link is described as safe to e-mail. Anyone who sees it can:

- **Forge a secret.** The requester's public key is public, so a third party can encrypt any message to it, for example "here is the new admin password: …". The requester has no way to tell who sent it. The security code proves the requester's key to the sender, not the other way round.
- **Squat on the request.** Only one secret is accepted, so the first sender wins. Junk sent first blocks the real sender, who gets "already sent".

Fix options:

- Cheap: on the inbox "revealed" page, show a warning that SafeShare does not verify who sent the secret, and that the sender should be confirmed out of band. Show the same warning on the request-created page.
- Stronger: have the requester's browser generate a short random **sender code**. It is stored as a hash on the server, sent to the sender over a second channel, and required by `/api/requests/{id}/fulfill`. That blocks both forging and squatting by link-only observers.

### 3. App port published on the Docker host (Medium, unverified)

`deploy/stack.yml` publishes `${APP_PORT:-3452}:8080` and sets `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`. That setting trusts `X-Forwarded-For` and `X-Forwarded-Proto` from **any** sender. Through NPM this is fine, because NPM appends the real client IP and ASP.NET takes the right-most value.

If port 3452 can be reached directly, from the LAN or the internet, a client can:

- use SafeShare over plain HTTP, skipping TLS and HSTS;
- send its own `X-Forwarded-For` header on each request, which defeats all three rate limits.

This could not be tested from here.

Fix, either of these:

- Put NPM and SafeShare on a shared Docker network, point NPM at `safeshare:8080`, and remove the `ports:` block.
- Firewall port 3452 so only the NPM host can reach it. Then also restrict `ForwardedHeadersOptions.KnownProxies` (or `KnownNetworks`) to NPM's address instead of relying on the environment variable.

### 4. Destroying a secret with only its ID (Low, design)

`/s/{id}/reveal` counts wrong tokens for any caller. Someone who sees only the link, without the key, can destroy the secret with 5 bad requests. That includes a person or bot watching the "link" channel when link and key are sent separately.

This is the flip side of finding 1's limit, so it is a trade-off: accept it and say so on `/about`, or add a per-secret cooldown (for example 1 attempt per 10 s per ID) before counting.

### 5. Missing headers (Low)

The middleware in `Program.cs` already sets a strong CSP plus `X-Frame-Options`, `nosniff` and `Referrer-Policy: no-referrer`. Add these:

```csharp
h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
h["Cross-Origin-Opener-Policy"] = "same-origin";
h["Cross-Origin-Resource-Policy"] = "same-origin";
```

`clipboard-write` is left alone on purpose, because the Copy buttons use it. Optionally, also add `upgrade-insecure-requests` and `require-trusted-types-for 'script'` to the CSP. The code uses only `textContent` and `createElement`, so Trusted Types should not break anything. Test before shipping.

### 6. Antiforgery cookie without `Secure` (Low)

`@Html.AntiForgeryToken()` sets `.AspNetCore.Antiforgery.*`. ASP.NET's default `CookieSecurePolicy` for this cookie is `None`, so the cookie is not marked Secure. This was inferred from the framework default: the audit could not read `Set-Cookie`. HSTS already blocks plain HTTP, so the impact is small.

```csharp
builder.Services.AddAntiforgery(o => o.Cookie.SecurePolicy = CookieSecurePolicy.Always);
builder.Services.Configure<CookieTempDataProviderOptions>(o => o.Cookie.SecurePolicy = CookieSecurePolicy.Always);
```

### 7. Manage token in the URL path (Low)

`/s/{id}/manage/{token}` puts the token in the request line, which NPM writes to its access log. Anyone who can read those logs can destroy (not read) any secret. The fix is to use `/s/{id}/manage#token` and send the token in the POST body, the same way the share key works.

### 8. HSTS details (Low)

NPM sends `max-age=63072000; preload`.

- `preload` without `includeSubDomains` is not accepted by the preload list, and a `duckdns.org` subdomain can't be preloaded anyway. It is harmless.
- JS and CSS responses carry no HSTS header. NPM's "Cache Assets" location drops it. The HTML pages have it, so browsers still get the policy.
- When the site moves to `noaignite.tech`, enable "HSTS Subdomains" only once every subdomain is HTTPS-only.

### 9–12. Small items

- **security.txt:** add `/.well-known/security.txt` with `Contact:` and `Expires:` so researchers know where to report issues.
- **README:** it names `https://git.kumin.duckdns.org/admin/mypastebin` (an internal host and an admin account). Remove this if the GitHub repo is or will be public.
- **`/Home/Error`:** it returns status 200. Set `Response.StatusCode = 500` in `HomeController.Error()`.
- **Dependencies:** EF Core and Sqlite are 10.0.11 and SQLitePCLRaw is 2.1.12. No advisory database could be reached from this session, so run this on your PC:
  `dotnet list src/SafeShare/SafeShare.csproj package --vulnerable --include-transitive`.
  The Docker base images use floating `10.0` tags. Rebuild regularly (Portainer "re-pull and redeploy", or a weekly GitOps poll) so .NET security patches actually reach production.

## What was checked and found sound

- **Crypto (`share.js`, `request.js`):**
  - 256-bit random key in the URL fragment (never sent to the server), random 16-byte salt and 12-byte IV, AES-256-GCM.
  - HKDF with separate `encrypt` and `auth` labels, so the server's auth hash can't produce the encryption key.
  - Passphrase: PBKDF2-SHA256 at 600k iterations, mixed into HKDF.
  - Request flow: non-extractable ECDH P-256 key in IndexedDB, ephemeral sender key, HKDF info bound to both public keys, GCM additional data bound to the request ID. The 80-bit security code is adequate against key substitution.
  - The key is removed from the address bar after reveal (`history.replaceState`).
- **One-time semantics:** conditional `ExecuteDelete` makes reveal and retrieve one-shot under concurrency, and fulfil uses a conditional update.
- **Server-side checks:** constant-time hash comparisons (`FixedTimeEquals`), random IDs (about 58 bits) and manage tokens (192 bits) from `RandomNumberGenerator`, strict length checks on every crypto field, a request body size cap, and an auto-burn job plus expiry checks on every access.
- **Web layer:**
  - CSP `default-src 'self'` with no `unsafe-inline` or `unsafe-eval`, `frame-ancestors 'none'`, `base-uri 'none'`.
  - The same header set on the home page, inner pages, the 404 page and API-like routes.
  - `Cache-Control: no-store` on pages, `X-Robots-Tag: noindex`, `Referrer-Policy: no-referrer`.
  - Antiforgery on every POST, including the JSON APIs.
  - No CORS headers, no third-party scripts (so SRI is not needed), no mixed content. All DOM output uses `textContent`, and Razor encodes the request description.
- **Container:** runs as a non-root `$APP_UID`, has a health check, and the data protection keys are used only for CSRF and TempData.
- **Repository:** no credentials in tracked files (git grep for password, token, key and private-key patterns), and `bin/`, `obj/` and `.vs/` are git-ignored.

## Method and limitations

- **Code review:** read every tracked source file (controllers, services, models, `Program.cs`, the views, `common.js`, `share.js`, `request.js`, Dockerfile, compose and stack files) on your computer, read-only.
- **Live site:** about 15 ordinary GET requests from the browser pane on your computer to `/`, `/share`, `/about`, `/requests`, a 404 path, `/s/…` and `/r/…` with made-up IDs, `/robots.txt`, `/.well-known/security.txt`, `/sitemap.xml`, `/healthz`, `/js/common.js`, `/css/site.css` and `/Home/Error`. No secrets were created or opened, nothing was submitted, and no third-party scanner was used.
  - The cloud workspace and your computer's sandbox are both blocked from this host by egress policy, so the skill's `collect_site.py` could not run.
  - The browser can't show `Set-Cookie` or the TLS certificate. TLS version, certificate expiry, the HTTP→HTTPS redirect and the CORS response to a foreign `Origin` are therefore **not run**. The grades use `websec.py` from the maintenance-audit skill on the observed headers.
- **Not covered:**
  - This is a code and configuration review plus a passive header check. It is not a penetration test, and nothing was actively attacked.
  - Not reviewed: the NPM, Portainer and Proxmox configuration and their admin access, the Docker host, firewall and backups, and whether port 3452 is reachable.
  - Also not covered: browser extensions or endpoint compromise on users' machines. Like any web-based end-to-end tool, SafeShare trusts the JavaScript the server sends, so anyone who controls the server or NPM could change it to capture **future** secrets. Protecting the host and the NPM admin login is part of SafeShare's security.
- **Not done:** the full 59-point maintenance checkup, which is built for Azure DevOps, Azure and Grafana projects.
