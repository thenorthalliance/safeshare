# SafeShare code architecture review, 2026-11-12

Code: `src/SafeShare` (ASP.NET Core 10 MVC), at commit `13b8679`
Auditor: Claude (Cowork), at the request of Dawid Sokół

## Summary

The codebase is small, disciplined, and unusually good for a security-sensitive app: correct use of `ExecuteUpdateAsync`/`ExecuteDeleteAsync` for race-free one-time-reveal semantics, constant-time hash comparison, minimal API surface, and no unneeded abstraction layers. No critical or high-severity architecture issues were found.

There are **two findings worth prioritizing** (both operational/deployment-shaped rather than code bugs):

1. No `ForwardedHeaders` middleware configured, while rate limiting is keyed on `RemoteIpAddress` — behind the normal reverse-proxy deployment topology this either locks out all clients at once or lets clients spoof their rate-limit bucket.
2. No `UseHttpsRedirection()`/`UseHsts()`/cookie security policy in `Program.cs` — the app's entire trust model depends on TLS (the key lives in the URL fragment), but nothing in-app enforces or documents that assumption.

The rest are minor layering/consistency notes, not correctness bugs.

## What needs attention (ranked)

| # | Severity | Finding | Location |
|---|---|---|---|
| 1 | Medium | No `ForwardedHeaders` middleware; rate limiting keyed on raw `RemoteIpAddress` | `Program.cs:56-64` |
| 2 | Medium | No `UseHttpsRedirection()`/`UseHsts()`, no explicit cookie security policy | `Program.cs` |
| 3 | Low | Options POCO mixes config, validation, and view-model-factory concerns | `Models/Options.cs:29,39-58` |
| 4 | Low | `HomeController.About` passes the raw options type as its view model, inconsistent with every other action | `HomeController.cs:49` |
| 5 | Low | Boundary validation (`IsPublicKey`, ciphertext/hash length checks) duplicated near-identically across controllers | `HomeController.cs:35-42`, `RequestController.cs:37-43,74-81` |

## Details

### 1. No `ForwardedHeaders` middleware (Medium)

`Program.cs:56-64` applies rate limiting keyed on `RemoteIpAddress`. If deployed behind a reverse proxy — the normal self-hosted topology for this app, and implied by the Dockerfile's `/data` volume setup — every request's remote IP is the proxy's. All clients then share a single rate-limit bucket (global lockout after 30 creates/min), or, if the proxy blindly forwards a client-supplied `X-Forwarded-For`, a client can self-select an unlimited bucket. No `UseForwardedHeaders()` is configured anywhere in the pipeline.

Fix: add `app.UseForwardedHeaders(...)` early in the pipeline, scoped to the known proxy (`KnownProxies`/`KnownNetworks`), before the rate limiter reads the connection's remote IP.

### 2. No HTTPS redirection / HSTS / cookie policy (Medium)

There's no `app.UseHttpsRedirection()` or `app.UseHsts()` in `Program.cs`, and no `CookiePolicyOptions` configured. Given this is an E2EE app whose entire trust model depends on TLS (the decryption key lives in the URL fragment, which must never leak over plaintext HTTP), this is a meaningful gap even if TLS termination is assumed to happen at a reverse proxy — nothing in the app enforces or documents that assumption.

### 3. Options POCO mixes concerns (Low)

`Models/Options.cs:39-58` (`TryResolveLifetime`) performs business-rule validation, and `:29` (`TimerField()`) acts as a view-model factory, both living on what is otherwise a plain configuration-binding POCO (`SafeShareOptions`). Harmless at the current size, but blurs config/validation/view-model layering; a `LifetimePolicy` class or a validation extension method would be cleaner if this options type grows further.

### 4. Inconsistent view model for `/about` (Low)

`HomeController.About` (`HomeController.cs:49`) passes the raw `SafeShareOptions` instance as the view model, whereas every other action in the same controller (and in `RequestController`/`SecretController`) uses a dedicated `*ViewModel` type. Minor, but notably inconsistent with the rest of the codebase's pattern, and couples the `About` view directly to the internal options shape.

### 5. Duplicated boundary validation (Low)

`IsPublicKey` and the ciphertext/hash-length checks appear near-identically in both `HomeController.cs:35-42` and `RequestController.cs:37-43,74-81`. Controllers manually null- and length-check every field rather than using `[Required]`/data-annotation validation or a shared validator. This is consistent and not wrong — just mild duplication across two files.

## What was checked and found sound

- **One-time-reveal semantics**: conditional `ExecuteUpdateAsync`/`ExecuteDeleteAsync` make reveal and retrieve race-free under concurrency.
- **Timing safety**: constant-time hash comparisons (`FixedTimeEquals`) in `Ids.cs:21-22` and `SecretService.TokenMatches` (`SecretService.cs:117-129`).
- **Async usage**: consistent and idiomatic throughout — no sync-over-async, no blocking `.Result`/`.Wait()`, `CancellationToken` threaded end-to-end from controller to EF Core. No `ConfigureAwait(false)` anywhere, which is correct for ASP.NET Core (no sync context to avoid).
- **Abstraction level**: appropriately minimal — no repository/unit-of-work interfaces wrapping EF Core, no `IFooService` interfaces for `SecretService`/`RequestService` with only one implementation (concrete classes injected directly, `Program.cs:38-39`). Correctly *not* over-engineered.
- **`ExpiredSecretCleanup`** (`Services/ExpiredSecretCleanup.cs`): scoped `DbContext` per tick, bulk deletes, catches non-cancellation exceptions so one bad tick doesn't kill the hosted service.
- **Migration hygiene**: single `Initial` migration, indices on `ExpiresUtc` for both tables match the cleanup job's query pattern (`SecretDbContext.cs:13,16`); `SecretRequest.IsFulfilled` is correctly excluded from the EF model via `.Ignore()` since it's computed from `Ciphertext`.
- **Logging**: minimal and doesn't leak ciphertext, tokens, or hashes — only counts are logged (`ExpiredSecretCleanup.cs:31`).

## Method and limitations

- Read-only review of the tracked source under `src/SafeShare` (controllers, services, models, `Program.cs`, EF Core context and migrations), performed by a subagent with file-search access.
- No live site or runtime behavior was tested as part of this review — see the separate `2026-10-26-security-audit.md` for a prior header/live-endpoint pass.
- Not a security review in its own right; overlaps with but does not replace the dedicated security audit.
