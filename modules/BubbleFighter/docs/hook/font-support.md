# Chinese font support

Version 1.0.6 deploys the Chinese client font with complete modern Hangul to the
native font filenames as well as `Data/ngulim.ttf`. Earlier 1.0.5 only replaced
the latter, which the Chinese language tables select. Enabling that package
while playing Korean therefore left the visible native font unchanged.

The original KR2015 `Config/Fonts_kr.csv` maps Default, DefaultBold and NexonBTBold
to `NexonBTBold.otf`; its other aliases select twelve additional TTF/OTF files.
The Indonesian `Config/Fonts_id.csv` instead selects `NanumGothicBold.ttf`.
These destinations are deployment data, with the Indonesian destination scoped
to id-400 and id-1809. Version 1.0.10 also supports kr-1256: kr-1256
sub_1051C20 loads `Config/Fonts_kr.csv`, whose default aliases select
`NANUMBARUNGOTHICBOLD_0.OTF`. Its separate fusion preserves that donor's Korean
glyphs. `tools/font-variants-kr1256.toml` records the original donor digest.

kr-812 sub_AE60D0 selects the language-specific table, reads `fontName` and
`fileName`, prefixes the filename with `Data/`, and calls kr-812 sub_B8EDF0.
That function opens the pathname through kr-812 sub_B8EAE0/sub_B8C6D0 and probes
the registered font drivers. The filename extension does not select its format;
the same TrueType payload can therefore occupy the table's OTF destinations.

Since 1.0.8, each of the ten available native fonts has a separate fusion. Every
fusion retains the Chinese DFYuanW7-GBK non-Korean outlines and metrics, including
Latin characters. Korean glyphs come from the corresponding Arita, Nexon or
Nanum original, preserving its native shape and weight. NexonGothic's 2,350
syllables take priority; missing modern syllables use the matching Arita weight.
The five referenced but absent original fonts (Malgun, Ulrungdo, Ydygo150,
Ydyg530 and Ydygo550) retain an explicit complete-Korean fallback, as does
ngulim. Nanum destinations are limited to the two Indonesian builds.

`tools/font-variants.toml` pins the Chinese base and each donor by SHA-256.
`tools/build-client-font-variants.py` generates private assets and a deployment
report. TrueType donors with matching units preserve their exact decomposed
outlines; other donors are scaled to the Chinese units. CFF curves are converted
to quadratic contours with a bounded approximation and corrected contour
direction. CFF outline origins are retained even when their recorded left-side
bearings disagree. Imported hint programs are discarded because they reference
a different font's control-value table. The base's hints and layout remain.
Every retained non-Korean outline and metric is compared before and after
serialization; every Korean glyph is checked against its selected donor, with
conversion bounds checked to 1.25 Chinese font units. The output must contain
all 11,172 modern Hangul syllables. Source fonts are never modified.

File deployment restores preceding files on exit and removes destinations that
were absent. Format probing in the loader permits a generated TrueType font at
an original OTF destination without changing the native language table.

Version 1.0.10 selects files and patches using the selected game language,
independently of the Launcher interface. Non-Chinese languages use the accepted
1.0.9 fused fonts, with prebaked Chinese/Latin outlines and each donor's original
Korean outlines. Chinese languages deploy the original, unmodified Chinese font
and always apply the Chinese weight repair. `force_bold` defaults to false and
only adds a weight override on the non-Chinese route. Saved choices remain
authoritative. Include/exclude language tags are case-insensitive, disjoint
manifest metadata. File filtering precedes collision detection and recoverable
deployment. Runtime 3.7.3 and Launcher 0.1.68 are required.
kr-812 sub_AE3E50 assigns the normal 400 weight, then overrides it with 700 when
the native language group is 2; the optional patch bypasses that last condition.
kr-1256 sub_1052710 has no Chinese group branch: it normalizes the requested
weight to 400/700 and stores it at style offset 56. Its build-scoped patch changes
the 400 normalization immediate to 700, retaining the store and calling
convention. Executable fixtures check both incoming weights, option values and
Chinese/non-Chinese tags independently of the older clients' locators.
Regression tests cover native Korean and Indonesian defaults with both option
states, disabled selections and transaction recovery. A local audit additionally
reads all original font tables and checks every TTF/OTF destination before testing
deployment and byte-exact recovery in temporary client copies. Live rendering
remains a player acceptance check.

Native regression cases execute the verified conditional store on an executable
test page, with the published manifest controlling patch preparation and writes.
They cover all three builds, Chinese and non-Chinese language tags, an absent tag,
and both option values.

The Chinese route covers `ngulim.ttf` on legacy clients and the Korean default
plus `GB13000.ttf` and `DF_GB_Y9.ttf` on kr-1256. The latter paths preserve Chinese
outlines when a translation supplies Chinese language tables. Original files
remain read-only inputs, and every replacement is restored after exit.

## Source distribution

For a prebaked fusion, run `embolden-client-font.py --source <GB13000.ttf>
--output <new-base.ttf> --freetype-library <local-library> --reference-ppem 11
--native-radius 16`, then `merge-client-fonts.py --base <new-base.ttf> --korean
<native-font> --output <new-fusion.ttf>`. Use the donor digests from the variant
definitions, and retain the original Chinese file separately for Chinese game
languages. All paths name private, read-only inputs or new ignored outputs.

Original client fonts and generated TTF/OTF/TTC payloads are private distribution
inputs, not source-code fixtures. The public repository contains only font
processing scripts, configuration, input hashes and generated-fixture tests.
Generate assets locally from separately supplied client fonts. Source archives
must be made from tracked files and must not include ignored `fonts/`, build
artifacts, client directories or local rollback packages. The source license
does not grant redistribution rights for the original client fonts.

## Prebaked Chinese outlines

Version 1.0.9 uses a separate stroked Chinese base while retaining the 1.0.8
Korean outlines. `tools/embolden-client-font.py` generates that private base,
using an explicitly supplied FreeType library at generation time. The generator
does not modify installed plugins or any runtime weight option.
The preceding 1.0.8 package and source must be retained before generating a
candidate. Output paths must be new, and original client files remain read-only.

kr-812 sub_AE6820 initializes the bold stroker through kr-812 sub_B90FB0 with
radius 16 in 26.6 pixel units, butt caps and round joins. kr-812 sub_AE2D30 and
kr-812 sub_AE32A0 apply that stroker when the selected weight is 700. The offline
generator scales this quarter-pixel radius into font units at an explicit
reference size, initially 11 pixels, the minimum in kr-812 sub_AE3E50. Since a
font's stored outlines scale with text size while the original stroke has a
fixed pixel radius, the candidate approximates the native effect at that
reference size; it is not pixel-identical across all sizes.

Only codepoints outside the Korean ranges are stroked. Composite source glyphs
are decomposed from an immutable source so their components are never stroked
twice. Horizontal advances remain unchanged, and side bearings track the new
outline bounds to preserve its drawing origin. Blank glyphs remain blank.
Generated geometry is checked after serialization, and existing Korean outlines
must remain exact. Run the fixture checks with `FT_TEST_LIBRARY` pointing to the
offline FreeType library. That library is not a launcher or game dependency.
