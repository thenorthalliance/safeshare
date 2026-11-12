# SafeShare UX & brand alignment review, 2026-11-12

Code: `src/SafeShare` (ASP.NET Core 10 MVC), at commit `13b8679`
Reference (corrected, see below): NoA Ignite brand identity, exported from Frontify to `../brand-resources/` — `Colors - Identity - NoA Ignite.html`, `Typography - Identity - NoA Ignite.html`, `Logotype - Identity - NoA Ignite.html`, plus the licensed font files (`Final NoaType/`) and logo package (`NoA Ignite Logo Package.zip`)
Auditor: Claude (Cowork), at the request of Dawid Sokół

## Correction notice

The original version of this review (same day, earlier) compared SafeShare against `NoA-Visual-Identity-Brand-Guidelines-241112.pdf` — the **parent "NoA" network's** master visual identity guidelines, which cover the whole alliance (NoA Ignite, NoA Consulting, NoA Health, etc.) as one umbrella brand. SafeShare is explicitly a **NoA Ignite** project (its own README says so), and NoA Ignite has its own Frontify-hosted brand identity that overrides parts of the master brand — notably, it makes **pink the primary vibrant color**, with yellow demoted to secondary ("inherited from the master brand").

Once the correct reference (`brand-resources/`) was available, almost all of the original findings turned out to be false positives caused by checking against the wrong document. This revision replaces those findings. A code fix made under the wrong assumption (swapping the pink accent for purple, commit `9308c78`) was reverted (commit `49598ca`) once the mistake was found.

## Summary

Checked against NoA Ignite's own identity, SafeShare's color palette, typography, and logo are **correct almost across the board** — the codebase's `site.css` comments ("tokens taken from noaignite.com") were right all along. Only one finding survives from the original review, and it's independent of brand guidelines entirely: a grammar bug in user-facing copy.

## What needs attention (ranked)

| # | Area | Finding | Location | Status |
|---|---|---|---|---|
| 1 | Copy | Broken grammar in user-facing explainer text | `Views/Request/New.cshtml:51` | **Done** |
| 2 | Color (minor, optional) | `--noa-purple` is defined but unused — harmless, since purple isn't part of NoA Ignite's palette at all; nothing to swap it *to*, just dead code if kept | `wwwroot/css/site.css:41` | Open (optional cleanup) |
| 3 | Color | `--surface-alt` used an undocumented hex in both themes: `--noa-sand` (`#e7ded1`, light) matched no NoA Ignite neutral — it's a near-identical twin of the unrelated parent-brand PDF's Beige 2 — and `#263030` (dark) was a one-off custom shade with no named counterpart at all | `wwwroot/css/site.css:58,86,108` (pre-fix line numbers) | **Done** |
| 4 | Color | Dark-theme `--text-muted: #afadac` was labeled `--color-brand2-quaternary` in a comment, implying an extended/unverified palette not shown on the Colors page available to this review | `wwwroot/css/site.css:89` (pre-fix line number) | **Done** |

## Corrected findings (supersedes the original 8)

NoA Ignite's actual palette (Colors page, `brand-resources/Colors - Identity - NoA Ignite.html`):

- **Vibrant**: Pink `#FF46CD` (RGBA 255,70,205,1) — *"our main vibrant color and should take priority over the yellow color which is inherited from the master brand"* — and Yellow `#E4FB00`, secondary.
- **Neutrals** (quoted: *"has a touch of blue"*): Black `#000000`, Off black `#1A1A1A`, Light Gray `#D8D5D5`, Dark `#151D1F`, Mid-dark `#313B3B`, Off white `#EBEAEA`, Mid `#547178`, Mid-light `#C2E0E7`, Light `#E0F5F6`, White `#FFFFFF`.

Every one of these hex values is an **exact match** to the corresponding `--noa-*` token already in `site.css:29-41` (`--noa-dark: #151d1f`, `--noa-dark-mid: #313b3b`, `--noa-mid: #547178`, `--noa-mid-light: #c2e0e7`, `--noa-light: #e0f5f6`, `--noa-white-off: #ebeaea`, `--noa-grey: #d8d5d5`, `--noa-pink: #ff46cd`, `--noa-yellow: #e4fb00`). There is no purple anywhere in NoA Ignite's own system — it only exists in the unrelated parent-brand PDF, which is why `--noa-purple` sits unused in the codebase.

- **Original #1 and #2 (pink unapproved / purple unused) — invalidated.** Pink is NoA Ignite's own primary vibrant color, used correctly for the hover/accent role. Purple isn't part of this brand at all; its presence in the code is a leftover from an irrelevant reference, not a missing-color gap.
- **Original #3 (teal/mint "not in the palette") — invalidated.** `--noa-mid`/`--noa-mid-light`/`--noa-light` are exactly NoA Ignite's documented "Mid"/"Mid-light"/"Light" neutrals (the palette's own "touch of blue").
- **Original #4 (body copy should be NoA Serif) — invalidated.** Typography page: *"NoA Serif and NoA Grotesk is our primary typefaces... Inter is our secondary typeface. It's primary use for small body copy"* and *"We use Inter for extensive body text in small sizes, where accessibility and legibility are key."* Inter-for-body is exactly what the brand identity asks for, not a deviation.
- **Original #5 and #6 (weight mixing: `<strong>` bold, `.fingerprint`/`.check.verify` medium) — weakened to non-issues.** The parent-brand PDF's "always Regular, never mix weights" rule doesn't apply here. NoA Ignite's own guideline explicitly pairs multiple weights for digital content: *"we pair all three typefaces. We primary use NoA Serif Regular, NoA Grotesk Regular and Inter Regular and Medium"* (and even more weights for reports). The available font files confirm this — NoA Grotesk ships Light/Regular/Medium/Bold, Inter ships Light through Bold plus italics. Bold `<strong>` emphasis and Inter Medium accents are normal within this system, not a violation. (If anything, `<strong>` defaulting to the browser's Bold rather than the documented "Medium" pairing is a very minor nitpick, not worth a dedicated action item.)
- **Original #7 (wrong logo asset) — invalidated, confirmed by direct visual comparison.** Extracted the inline SVG from `Views/Shared/_Logo.cshtml`, rendered it, and compared it side-by-side with the official `NoA Ignite Primary Lockup Pos RGB.svg`/`.png` from the brand's own logo package. Both show the same design: the "N°A" grotesk mark paired with "Ignite" set in the serif — exactly matching the Logotype page's own description, *"consists of the NoA family name paired with a wordmark in our primary brand typeface."* The codebase's logo is correct; the earlier concern was comparing it against the parent brand's unrelated compact "N°A"-only mark.
- **Original #8 (grammar bug in `Request/New.cshtml:51`) — stands, unaffected by the reference-document mix-up.** Still: *"This features allows you to create request for a secret. You are the only person who have key..."* — still broken English, independent of any brand-guideline question.

### 3–4. Dark-theme follow-up: two gaps found while checking dark-mode compliance — Done

A later, more targeted check of just the dark-theme tokens (prompted by "is dark theme compliant with NoA colors?") turned up two values that matched *none* of NoA Ignite's ten documented neutrals (Black, Off black, Light Gray, Dark, Mid-dark, Off white, Mid, Mid-light, Light, White), in a doc that had otherwise checked out token-for-token:

- `--surface-alt` used `var(--noa-sand)` (`#e7ded1`) in light theme and a hardcoded `#263030` in dark theme. Neither is one of the ten documented neutrals. `#e7ded1` is suspiciously close to the unrelated parent-brand PDF's Beige 2 (`#E7DED0`) — almost certainly another leftover from the original wrong-reference pass, confirmed absent from NoA Ignite's own Colors page (grepped the saved export directly; "sand" only appears as a CSS class-name substring, `block-dosanddonts-images`, not an actual color).
- Dark-theme `--text-muted: #afadac` carried a comment, `--color-brand2-quaternary`, implying it came from an extended/secondary palette this review never had access to.

**Fix applied:** `--surface-alt` now reuses `--noa-grey` (Light Gray `#d8d5d5`) in light theme, and `--noa-dark-mid` (Mid-dark `#313b3b`, same as `--surface`) in dark theme — accepting that NoA Ignite's own neutral steps don't offer a finer gradient between Dark and Mid-dark, rather than inventing one. `--text-muted` in dark theme now reuses `--noa-grey` as well. The now-unused `--noa-sand` token was removed. Verified live: the served CSS reflects all four changes, and both dark-theme blocks (`@media (prefers-color-scheme: dark)` and `:root[data-theme="dark"]`) stay identical, per the file's own "keep the two token blocks identical" comment.

## New opportunity (not a fix, an upgrade) — Done

`brand-resources/Final NoaType/` contained the actual **licensed** NoA Serif 2.0 and NoA Grotesk 2.0 webfont files, which `site.css`'s comment said were missing. Copied `NoaGrotesk2.0/web/{Regular,Medium,Bold}.woff2` and `NoaSerif2.0/web/{Regular,Bold}.woff2` into `src/SafeShare/wwwroot/fonts/` under the filenames `@font-face` already expected — purely additive, no CSS rule changes. Verified live: all five files now serve `200` from the rebuilt container, matching the source byte sizes. README and the `site.css` header comment were updated to stop saying these fonts are missing/licensed-elsewhere, and to note they're proprietary (not OFL, unlike Inter) — don't reuse outside NoA Ignite projects.

## What's aligned well

- Pink as the primary interactive accent (button hover, nav underline, security-code shadow) — correct per NoA Ignite's own color hierarchy.
- Yellow used as the secondary vibrant/highlight accent — correct.
- The full neutral family (dark/mid/light/off-white/off-black/etc.) — exact hex matches.
- Inter for body copy — explicitly the documented secondary typeface for this exact purpose.
- The `_Logo.cshtml` primary lockup SVG — confirmed, via direct visual comparison, to be the correct NoA Ignite asset.
- Theme-aware `fill="currentColor"` logo recoloring (white-on-dark, black-on-light only) — matches the Logotype page's "the logo should only appear in black and white" don't.
- Uppercase, all-caps NoA Grotesk for nav/buttons/labels — matches *"To maintain consistency, Noa Grotesk should always be set in all caps."*

## Method and limitations

- `brand-resources/` pages read: Colors, Typography, Logotype (full text extracted from the saved Frontify HTML exports; image-only sections — e.g. the itemized Color/Logotype "Do/Don'ts" examples — were not individually inspected, since the surrounding prose already settled every claim in the original review).
- Logo comparison was empirical: the inline SVG was extracted from `_Logo.cshtml`, rendered to PNG via `qlmanage`, and visually compared against `NY logo/Primary Lockup/Digital/{SVG,PNG}/NoA Ignite Primary Lockup Pos RGB.*` from the official logo package — not just a textual aspect-ratio guess (which is what led the original review astray).
- Font weights were cross-checked against the actual files shipped in `Final NoaType/{NoaSerif2.0,NoaGrotesk2.0,NoaInter4.1}/web/`, not just guideline prose.
- Graphic System and Icons pages in `brand-resources/` were not reviewed — out of scope for this correction, since the original review made no claims there.
