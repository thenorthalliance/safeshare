# SafeShare (.NET 10)

A self-hosted, end-to-end encrypted **one-time secret** tool built on ASP.NET Core 10 MVC with Razor
views. It replaces the old PHP app MyPastebin. Storage is one SQLite file, and the whole app ships as a
single Docker image.

All encryption happens in the browser (WebCrypto). The server stores only ciphertext. Neither a copy
of the database or backups, nor the server or proxy logs, are enough to read a secret.

## Features

### Share a secret (`/`, the start page)
Use this when you have the secret and want to hand it over.

1. The browser generates a random 256-bit key and encrypts with AES-256-GCM. An optional passphrase
   is stretched with PBKDF2-SHA256 (600k iterations) and mixed in with HKDF.
2. The key goes in the URL **fragment** (`/s/<id>#<key>`), which browsers never send to the server.
   Share it as **one link**, or send the **link and key separately** through two channels. Opening
   `/s/<id>` without the key shows a textbox for it.
3. Opening the link shows a confirmation page first, so chat link previews can't use up the secret.
   The browser proves it holds the key with an HKDF-derived token. The server then returns the
   ciphertext and **deletes it in the same step**.
4. After 5 wrong key or passphrase attempts, the secret is destroyed. The creator gets a private link
   to destroy it early.

### Request a secret (`/request`)
Use this when someone without a trusted channel to you (a client or a vendor) needs to send you a secret.

1. The requester's browser creates an **ECDH P-256 key pair**. The private key is stored
   non-extractable in that browser's IndexedDB. The server gets only the public key and a hash of an
   access token.
2. The requester sends the request link (`/r/<id>`). It contains nothing secret, so e-mail is fine.
3. Both sides see a **security code** (the first 80 bits of SHA-256 of the public key, as
   `XXXX-XXXX-XXXX-XXXX`) and confirm it by phone or video. This catches a swapped key. The sender
   must tick "confirmed" before sending.
4. The sender's browser encrypts with an ephemeral ECDH key, HKDF-SHA256 (bound to both public keys)
   and AES-256-GCM (bound to the request id). Each request accepts exactly one secret.
5. The requester opens `/r/<id>/inbox` in the same browser. The page polls until the secret arrives,
   then retrieves and decrypts it once. The server deletes the ciphertext, and the browser deletes the
   private key. `/requests` lists all requests stored in that browser. A request can be cancelled
   at any time.

### Auto-burn timer
- Every secret and request can have a timer of **any length** (minutes, hours or days). When it runs
  out, the item is burned whether or not anyone opened it. Pages show a live countdown.
- The timer is **optional** unless the admin sets `AllowNoExpiry=false`. Without a timer, an item
  lives until it is opened, cancelled or destroyed.
- For requests, the requester and the sender can each set a timer. The **shorter one wins**.
- A background job burns expired items every `CleanupIntervalMinutes` (default 1). Expired items are
  also refused immediately when accessed.

## Branding

The UI follows the NoA Ignite brand from noaignite.com: its colour tokens (dark `#151D1F`, pink
`#FF46CD` for hover and accents, yellow `#E4FB00` for highlights and warnings, grey/sand surfaces),
4px radii, dark "contained" buttons that turn pink on hover, uppercase grotesk labels, serif headings,
Inter for body text, and the NoA logo (inline SVG, adapts to dark mode). It's all defined as CSS variables
at the top of `wwwroot/css/site.css`.

**Brand fonts:** noaGrotesk and noaSerif are licensed and not included in the repo. Get the `.woff2`
files from the brand/design team and add them as:

```
src/SafeShare/wwwroot/fonts/noaGrotesk-Regular.woff2
src/SafeShare/wwwroot/fonts/noaGrotesk-Medium.woff2
src/SafeShare/wwwroot/fonts/noaGrotesk-Bold.woff2
src/SafeShare/wwwroot/fonts/noaSerif-Regular.woff2
src/SafeShare/wwwroot/fonts/noaSerif-Bold.woff2
```

They're picked up automatically, with no code change. Until then, labels use a metric-matched Arial
fallback and headings use Georgia. Inter is bundled (SIL Open Font License, see `wwwroot/fonts/OFL-Inter.txt`).

## Requirements: HTTPS

WebCrypto only works in a **secure context**. Serve the app over HTTPS (or `http://localhost` for
testing); otherwise the page shows a warning and refuses to encrypt. Behind a reverse proxy, set
`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`.

## Run with Docker

```bash
docker build -t safeshare .
docker run -d --name safeshare -p 8080:8080 -v safeshare-data:/data safeshare
# or
docker compose up -d --build
```

`/data` holds `safeshare.db` (ciphertext and public keys only) and `keys/` (ASP.NET data-protection
keys, used only for CSRF tokens). Mount a volume there to keep pending items when the container is
recreated. The schema is managed with EF Core migrations, which are applied automatically on startup.

## Deploy on Proxmox / Portainer (from Git)

Portainer → Stacks → Add stack → **Repository**:

| Field | Value |
|---|---|
| Repository URL | `https://git.kumin.duckdns.org/admin/mypastebin` |
| Reference | `refs/heads/main` |
| Compose path | `deploy/stack.yml` (you have a reverse proxy) or `deploy/stack-caddy.yml` (bundled Caddy for HTTPS) |
| Authentication | on: your Git username + an access token with read access to the repository |
| Environment variables | `SITE_HOST` (Caddy variant, required), optional `CADDY_FLAGS`, `APP_PORT`, `SITE_NAME`, `ALLOW_NO_EXPIRY`, `DEFAULT_LIFETIME_MINUTES`, `MAX_LIFETIME_MINUTES` |

Portainer clones the repository and builds the image on the Docker host. To update, push to `main`
and use **Pull and redeploy** on the stack, with re-pull/rebuild enabled. You can also turn on
GitOps updates (polling or webhook) to redeploy automatically. Data in the `safeshare-data` volume is
kept, and migrations run on startup.

## Configuration

Set these as environment variables (`SafeShare__Key`) or in `appsettings.json`:

| Setting                            | Default      | Meaning                                                    |
|------------------------------------|--------------|------------------------------------------------------------|
| `SafeShare__SiteName`               | `SafeShare`  | Name shown in the header and title                         |
| `SafeShare__MaxSecretSizeKb`        | `64`         | Max plaintext size                                         |
| `SafeShare__MaxFailedAttempts`      | `5`          | Wrong key or passphrase attempts before a shared secret is destroyed |
| `SafeShare__AllowNoExpiry`          | `true`       | Whether users may switch the auto-burn timer off           |
| `SafeShare__DefaultLifetimeMinutes` | `1440`       | Timer value pre-filled in forms                            |
| `SafeShare__MaxLifetimeMinutes`     | `43200`      | Longest timer a user may choose (30 days)                  |
| `SafeShare__CleanupIntervalMinutes` | `1`          | How often expired items are burned                         |
| `SafeShare__DataDirectory`          | `/data`      | Location of the DB and keys                                |

Rate limits per IP: 30 creates/min, 20 reveal attempts/min, 120 status polls/min.

## Local development

```bash
cd src/SafeShare
dotnet run          # http://localhost:5080, data stored in src/SafeShare/App_Data

# after changing the model:
dotnet tool install -g dotnet-ef
dotnet ef migrations add <Name> -o Data/Migrations
```

## Project layout

```
src/SafeShare/
  Controllers/      HomeController (share form, POST /api/secrets), SecretController (/s/{id}),
                    RequestController (/request, /requests, /r/{id}, /r/{id}/inbox, /api/requests/...)
  Data/             EF Core DbContext + migrations (SQLite)
  Models/           Secret, SecretRequest, options, request/view models
  Services/         SecretService, RequestService, background auto-burn job
  Views/            Razor views (_TimerField and _Expiry partials are shared)
  wwwroot/js/       common.js (encoding, timer, countdown, IndexedDB), share.js, request.js
```
