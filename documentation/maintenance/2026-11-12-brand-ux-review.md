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

| # | Area | Finding | Location |
|---|---|---|---|
| 1 | Copy | Broken grammar in user-facing explainer text | `Views/Request/New.cshtml:51` |
| 2 | Color (minor, optional) | `--noa-purple` is defined but unused — harmless, since purple isn't part of NoA Ignite's palette at all; nothing to swap it *to*, just dead code if kept | `wwwroot/css/site.css:41` |

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

## New opportunity (not a fix, an upgrade)

`brand-resources/Final NoaType/` now contains the actual **licensed** NoA Serif 2.0 and NoA Grotesk 2.0 webfont files (`.woff`/`.woff2`), which `site.css`'s own comment says were missing ("NOT included in this repository... until then, the metric-matched Arial fallbacks... keep the layout identical"). Copying `NoaSerif2.0/web/*.woff2` and `NoaGrotesk2.0/web/*.woff2` into `src/SafeShare/wwwroot/fonts/` (matching the filenames `@font-face` already expects) would drop the Georgia/Arial fallback and render in the real brand typefaces — purely additive, no CSS changes needed. Not filed as a ranked finding since it's an enhancement, not a drift from guidelines, but worth doing given the assets are now on hand.

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
