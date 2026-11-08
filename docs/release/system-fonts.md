# System fonts and private builds

[Chinese](system-fonts.zh.md)

Public source contains no bundled font. Body text uses installed Windows fonts;
icons use Segoe Fluent Icons with Segoe MDL2 Assets fallback. Clock and blocked
use inline SVG only when no private font is supplied. The account SVG is unchanged.

For local builds, place a separately obtained font at
`web/private-assets/SEGOEICONS.TTF`. This entire directory is ignored by Git.
Vite detects the file at startup, includes it as a hashed build asset and uses
the original clock and blocked glyphs. No font is downloaded by the build.
The normal launcher packaging command runs this same frontend build.
Set `FT_SYSTEM_FONTS_ONLY=1` to explicitly disable the private font. Restart the
development server after changing the file or option.

On 2026-10-21, local cmap inspection found all 66 mapped glyphs in Fluent Icons
and 64 in MDL2; MDL2 lacks clock U+E917 and blocked U+E733. This checks the local
font versions, not all historical Windows 10 revisions. Windows 10 visual
acceptance remains separate. Public-source builds can have different system icon
shapes; private builds preserve the former bundled-font behavior.

Type checking and both production build modes pass. Private output matches the
supplied font's SHA-256 exactly. System-only output contains no font files.
The final local output was restored to private-font mode. Frozen releases are
unchanged. The original font is preserved privately and remains in Git history;
the public-history and distribution-permission reviews remain separate gates.
