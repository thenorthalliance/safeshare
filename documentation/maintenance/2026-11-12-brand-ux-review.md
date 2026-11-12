# SafeShare UX & brand alignment review, 2026-11-12

Code: `src/SafeShare` (ASP.NET Core 10 MVC), at commit `13b8679`
Reference: `NoA Visual Identity Guidelines` v1.0 (2024-11-12), `NoA-Visual-Identity-Brand-Guidelines-241112.pdf`
Auditor: Claude (Cowork), at the request of Dawid Sokół

## Summary

SafeShare's UI follows the right *shape* of the NoA brand system (two-typeface model, uppercase functional labels, light/dark theme, small corner radii, yellow as the lone highlight accent) but has drifted from this specific guideline document on color and typography specifics, and likely ships the wrong logo asset. One confirmed copy/grammar bug was also found.

**Three findings worth prioritizing:**

1. An unapproved pink (`#ff46cd`) — not present anywhere in the guideline's palette — drives every interactive accent (button hover, nav underline, security-code shadow), while the guideline's actual secondary color (purple, `#A681F7`) is defined in code but never used.
2. Body copy renders in Inter, not NoA Serif; the guideline requires NoA Serif for all three text roles (headlines, secondary headlines, body copy).
3. `_Logo.cshtml` is commented as sourced "from noaignite.com" and renders a wide wordmark (~3.8:1 aspect ratio) rather than this guideline's compact "N°A" logotype (~2:1, degree mark over the O) — worth confirming with the brand team whether the older lockup is still sanctioned.

## What needs attention (ranked)

| # | Area | Finding | Location |
|---|---|---|---|
| 1 | Color | Unapproved pink (`#ff46cd`) drives all interactive accents; not in this guideline's palette | `wwwroot/css/site.css:39,67,201,427` |
| 2 | Color | Purple (`#A681F7`, the guideline's documented secondary vibrant color) is defined but never used anywhere | `wwwroot/css/site.css:41` |
| 3 | Color | Teal/mint family (`--noa-mid`, `--noa-mid-light`, `--noa-light`) drives dividers and dark-theme hovers; absent from this guideline's palette | `wwwroot/css/site.css:32-34,70,74,91,95,98,113,117,120` |
| 4 | Typography | Body copy uses Inter, not NoA Serif (guideline requires NoA Serif for headlines, secondary headlines, *and* body copy) | `wwwroot/css/site.css:49,136` |
| 5 | Typography | `<strong>` used extensively in body copy with no weight reset — renders default bold, mixing weights in primary-brand context | Most views, e.g. `Views/Home/Index.cshtml:84,88-95` |
| 6 | Typography | `.fingerprint` / `.check.verify` use medium (500) weight in a user-facing (not strictly "functional/report") context | `wwwroot/css/site.css:330,418` |
| 7 | Logo | `_Logo.cshtml` renders the older noaignite.com wordmark, not this guideline's compact "N°A" logotype | `Views/Shared/_Logo.cshtml:1-2` |
| 8 | Copy | Broken grammar in user-facing explainer text | `Views/Request/New.cshtml:51` |

## Details

### 1–3. Color palette drift

The guideline's complete palette (Color → Complete Palette, p.19) is: Yellow `#E4FB00`, Purple `#A681F7` (the vibrant pair, yellow primary per p.17/22), Beige 1 `#CEBEA1`, Beige 2 `#E7DED0`, Black `#000000`, Off-Black `#1A1A1A`, Dark Grey 1 `#292929`, Dark Grey 2 `#545050`, Mid Grey `#AFADAC`, Light Grey `#D8D5D5`, Off-White `#EBEAEA`, White `#FFFFFF`.

`site.css` matches yellow and purple exactly as CSS custom properties, but:

- **Purple is defined and never referenced again** (`--noa-purple: #a681f7;` at `site.css:41` is the only occurrence in the file). The guideline's documented secondary brand color is entirely absent from the live UI.
- **An unapproved pink (`#ff46cd`) drives every interactive accent**: primary-button hover (`--btn-hover-bg`, used at `site.css:67,379`), nav-link hover underline (`site.css:201`), and the security-code "fingerprint" box-shadow (`site.css:427`). Pink does not appear anywhere in this guideline's palette. The guideline's Color → Don'ts page explicitly calls out non-approved color combinations and (for the logotype) custom colors as things to avoid.
- **A teal/mint family** (`--noa-mid: #547178`, `--noa-mid-light: #c2e0e7`, `--noa-light: #e0f5f6`) drives dividers and dark-theme hover states (`site.css:70,74,91,95,98,113,117,120`) — also not part of this palette. The CSS's own comments attribute these tokens to noaignite.com's general site rather than this specific guideline document.
- Yellow is otherwise used correctly as the sole "highlight" accent (warnings, security-code background), consistent with the guideline's "yellow should be primary" / "pair one vibrant with 1–2 neutrals" guidance (Color → Color Usage, p.22).

### 4–6. Typography drift

The guideline (Typography → Hierarchy/Weight Usage, p.27-31) specifies two brand typefaces — NoA Serif and NoA Grotesk — and is explicit that:

- Primary headlines, secondary headlines, **and body copy** all use **NoA Serif Regular**.
- NoA Grotesk is reserved for "Functional" usage: buttons, labels, numbers.
- "We always use the regular version of both... in all our primary touch points" and "Do not mix different weights in primary brand usage" are both stated directly (p.30/32).

In the codebase:

- `--font-body: "Inter", ...` (`site.css:49`) is applied to `body` (`site.css:136`), so all paragraph/body text renders in Inter — a third, non-brand typeface — rather than NoA Serif. This is a bigger deviation than the guideline's explicit "don't use NoA Grotesk as body copy" don't anticipates, since it isn't using either brand typeface for body text at all. (The README does disclose this as a deliberate choice pending licensed font files, but it still diverges from this specific guideline.)
- `<strong>` tags appear in body copy across nearly every view (`Home/Index.cshtml`, `Home/About.cshtml`, `Request/New.cshtml`, `Request/Inbox.cshtml`, `Request/Mine.cshtml`, `Request/Fulfill.cshtml`, `Secret/Manage.cshtml`, `Secret/Open.cshtml`) with no CSS rule resetting their weight, so they render at the browser's default bold. This directly runs into the guideline's "do not mix different weights in primary brand usage" don't, and happens dozens of times across the app.
- `.fingerprint` (the security-code display) and `.check.verify` use `font-weight: 500` (medium) at `site.css:330,418`. The guideline only sanctions medium/bold weights for "Functional" usage in rare report-like contexts (p.31) — a public-facing security code that every user reads is closer to primary brand communication than that narrow carve-out.
- What *is* aligned: headings (`h1`–`h3`) correctly use weight 400/Regular only (`site.css:146`), and uppercase nav/button/field labels correctly use the grotesk-equivalent sans in a genuinely "Functional" context (buttons, nav, field labels, table headers) — consistent with the guideline.

### 7. Logo asset

`Views/Shared/_Logo.cshtml:1` is commented *"NoA Ignite logo (from noaignite.com)"* and renders an SVG with `viewBox="0 0 121 32"` — a wide wordmark, roughly 3.8:1 width:height. The guideline's approved logotype (Logotype → Primary Placement/Minimum Size, p.9-11) is the compact "N°A" mark (degree symbol over the O), shown throughout the guideline at roughly a 2:1 aspect ratio. These appear to be different assets. The theme-aware `fill="currentColor"` approach (switching only between white-on-dark and black-on-light) is itself consistent with the guideline's approved monochrome logo usage — the concern is specifically whether this is the right logo file at all, not how it's recolored. Worth confirming with the brand team whether the wider noaignite.com lockup is still sanctioned, since this guideline document would supersede it if not.

### 8. Copy bug

`Views/Request/New.cshtml:51`:

> "This features allows you to create request for a secret. *You* are the only person who have key, which makes it extremely secure."

Grammatically broken; should read something close to "This feature lets you request a secret. You're the only one who has the key, which makes it extremely secure."

## What's aligned well

- Yellow used correctly and exclusively as the "highlight" accent (warnings, security-code background), matching the guideline's "yellow is primary, used sparingly as an accent" direction.
- Light/dark theme toggle only swaps between approved monochrome (white/black) logo renditions — no arbitrary logo recoloring.
- Uppercase, tight-tracking functional labels (nav, buttons, field labels, table headers) correctly use the grotesk-style sans, matching the guideline's "Functional" typeface usage.
- Small, consistent corner radii (`--radius`, `--radius-input`, `--radius-card`) match the brand's documented flat, low-radius style.
- Headings consistently use Regular weight only, with no bold headline variants in primary brand usage.

## Action items

Concrete, independent fixes, roughly ordered by impact. Each is scoped to land as its own small change.

| # | Action | Fix | File(s) | Effort | Status |
|---|---|---|---|---|---|
| 1 | Drop the unapproved pink hover accent | Replace `--noa-pink` as the hover color for `--btn-hover-bg`, the nav-link hover underline, and the `.fingerprint` shadow with an approved token — purple (`--noa-purple`) if an accent color is wanted, or a neutral (Dark Grey 2 / Mid Grey) if not | `wwwroot/css/site.css:39,67,201,427` | 15 min | **Done** — replaced with `--noa-purple` in all three spots; `--noa-pink` removed entirely |
| 2 | Put purple to use, or remove it | If purple becomes the new hover/accent color from #1, this resolves itself. Otherwise delete the unused `--noa-purple` token rather than leave documented-but-dead code | `wwwroot/css/site.css:41` | 5 min | **Done** — resolved as a side effect of #1; purple is now the hover/accent color |
| 3 | Replace the teal/mint neutral family | Swap `--noa-mid`, `--noa-mid-light`, `--noa-light` (dividers, dark-theme hover states) for guideline greys: Dark Grey 2 `#545050`, Mid Grey `#AFADAC`, Light Grey `#D8D5D5` as appropriate per light/dark theme | `wwwroot/css/site.css:32-34,70,74,91,95,98,113,117,120` | 30 min (touches both theme blocks; re-check contrast) | Open |
| 4 | Switch body copy to NoA Serif | Point `--font-body` (or the `body` rule directly) at `var(--font-serif)` instead of `"Inter"`; keep Inter loaded only as the serif's eventual fallback chain if desired | `wwwroot/css/site.css:49,136` | 30 min (single variable swap, but reflows every page — visual QA pass needed) | Open |
| 5 | Stop `<strong>` from rendering bold | Add `strong, b { font-weight: inherit; }` (or similar reset) and, if emphasis still needs to read visually, use color/underline instead of weight | `wwwroot/css/site.css` (new rule near the `a`/`code` base rules) | 10 min | Open |
| 6 | Drop medium weight from `.fingerprint` / `.check.verify` | Change `font-weight: 500` to `400` (or remove the explicit weight so it inherits Regular) | `wwwroot/css/site.css:330,418` | 5 min | Open |
| 7 | Replace the logo asset | Swap `_Logo.cshtml`'s SVG for the guideline's compact "N°A" logotype. **Blocked on receiving the approved SVG/asset from the brand team** — this can't be redrawn from the PDF alone with confidence | `Views/Shared/_Logo.cshtml` | Blocked (design asset needed); wiring it in once received is ~10 min | Blocked |
| 8 | Fix the grammar bug | Reword "This features allows you to create request for a secret. *You* are the only person who have key..." to something like "This feature lets you request a secret. You're the only one who has the key..." | `Views/Request/New.cshtml:51` | 2 min | Open |

#1 and #2 are done (verified by rebuilding and checking the served CSS). Everything except #7 can be done without external input; #7 needs the brand team's actual logo file before it can be wired in.

## Method and limitations

- Guideline pages reviewed: 1-35 (Foundation, Logotype, Color, Typography, Gradient intro) of a 52-page PDF; Layout System and Gradient detail sections (p.36+) were not reviewed in depth.
- Compared against `wwwroot/css/site.css` (full file) and the Razor views under `Views/` (full file list; `_Layout.cshtml`, `_Logo.cshtml`, `Home/Index.cshtml`, `Secret/Open.cshtml`, `Request/New.cshtml` read in full; other views spot-checked via grep for weight/emphasis usage).
- No live site or rendered-screenshot comparison was performed — this is a static code-vs-document comparison, not a visual regression check.
