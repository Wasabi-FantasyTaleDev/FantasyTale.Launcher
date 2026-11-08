# Account level data

[Chinese](levels.zh.md)

A game declares `game.levels = "levels/levels.toml"`. The launcher loads that
manifest, its original or normalized CSV table, and local PNG images. Shared code
contains no game-id branches. These files supply presentation data only; private
game modules and native authentication remain separate.

## Bundled data

| Game | Ordinary level ids | Experience source | Icons |
| --- | --- | --- | --- |
| Bubble Fighter | 1–240 | `levels.csv`, cumulative minimum EXP | `rank.png`, 5 columns, 23×26 cells |
| Air Rider | 1–30 | `levels.csv`, minimum EXP converted from the preceding original `nextRp` | ASCII `default-NNN.png`; optional `star-NNN.png` |
| Tales Runner | 0–125 | original `essenlevelinfo.txt`, only `fdLevelKind = 1` | `rankicon.png` for 0–97, then `rankicon2.png` from 98 |

Bubble Fighter's atlas has 260 cells but the supplied EXP table ends at 240.
Unused cells do not establish extra levels. Names remain absent with
`names_pending = true` and a TODO. Exact cyan separator pixels were made
transparent during import; the supplied originals were left intact.

Air Rider's original table contains 30 entries. Row one's minimum is zero;
subsequent minima are the preceding row's `nextRp`. Star images are a visual
variant of those entries; no second EXP ladder is inferred. The white image is
retained as an explicit unranked badge. Every installed image filename is ASCII;
original Korean labels remain UTF-8 table content.

Tales Runner's source contains several kinds of records. Only kind 1 contributes
to the account level table. Source level ids are preserved: the initial red chick
is id 0 at EXP 0, supplied by `initial_level` / `initial_exp`; the original first
row is id 1 at EXP 25. Both rank images form one ordinary progression. Cells 98–100
of the first image are outside that progression; the question mark is cell 99,
and Piero is cell 100. Both images use 9 columns of 26×21 pixels. Extra art beyond
the current source rows stays unused until corresponding rows arrive.

## Updating without recompilation

Replace the installed game's `levels/essenlevelinfo.txt`, `rankicon.png`, and
`rankicon2.png`, then reopen the launcher. The second atlas has no fixed last level;
new kind-1 rows map automatically as long as the original grid layout is retained.
If the grid changes or a third image is added, update only `levels.toml`: change
cell dimensions or add another `[[atlases]]` range. Give the previous range an
explicit `last_level` when adding a new sheet. BF/AR thresholds and icons can also
be edited in their data files without source changes. `game.levels` may point at a
different manifest for a custom distribution.

`tools/import-level-assets.py --source <input> --games <games>` prepares the initial
bundled inputs with Python and Pillow. It is unnecessary for subsequent TR source
updates. The checked-in web preview JSON is a development snapshot; installed
launchers parse source tables at runtime rather than using that snapshot.

## Manifest schema 1

`[source]` specifies `file`, optional `encoding` (`utf-8`, `utf-16le`, `utf-16be`),
`skip_lines`, zero-based `level_column` and `exp_column`, optional `name_column`,
and optional paired `filter_column` / `filter_value`. CSV quoting and doubled quotes
are supported; blank and `//` comment lines are skipped. Optional `initial_level`
and `initial_exp` define an omitted starting row. `display_offset` changes only the
visible number and defaults to zero. Experience is cumulative and inclusive.

`optional_assets = true` declares that the table and images are distributed with
release packages rather than with the source tree. When that key is set and the
table or any referenced image is absent, the game loads without level display
instead of failing. Present files are still validated in full, and manifests
without the key still fail on any missing file. The bundled manifests set it
because the original tables and rank artwork are not part of the public source;
release packages include them. Declaration types, keys, numeric ranges and local
paths are validated even when assets are missing. Available tables and PNG headers
are also checked. Cross-asset relationships, such as a level's rectangle within
an absent atlas, can only be checked once the missing asset is supplied. Access
denied errors and directories in place of files are errors, not optional absence.

Each `[[atlases]]` gives `file`, `first_level`, optional `last_level`, optional
`first_cell`, `columns`, `cell_width`, `cell_height`, optional origin `x` / `y`, and
optional `variant` (default `default`). Each `[[icons]]` instead provides `level`,
`file`, optional crop `x` / `y` / `width` / `height`, and optional `variant`.
Every ordinary level needs a default icon. Overlapping mappings, duplicate levels,
non-increasing EXP, unsafe paths and rectangles outside the image fail validation.

Limits: 256 KiB manifest, 4 MiB table, 16384 source lines, 4096 matching levels,
32 atlases, 64 badges, 16 MiB PNG files, and dimensions at most 8192 pixels. EXP is
parsed as nonnegative signed 64-bit data; bridge thresholds use decimal strings,
and the frontend uses BigInt. No image, name or EXP is guessed for unknown rows.

## Per-server accounts and icon overrides

The account stays attached to its selected server. `stats.level` is authoritative;
when absent, `stats.exp` can derive the ordinary rank. Unknown explicit levels show
numeric information without inventing an icon. Inconsistent level/EXP pairs do not
show a misleading progress bar. A supplied HTTPS `levelIconUrl` takes precedence;
otherwise the bundled rank data supplies the icon.

`[[badges]]` defines `id`, `icon_id`, `name`, image and optional crop. TR uses:

| Badge | Icon id | Crop in rankicon.png |
| --- | --- | --- |
| piero | 10000 | x=26, y=231, width=26, height=21 |
| unknown | 10001 | x=0, y=231, width=26, height=21 |

These are presentation overrides, not ordinary levels or authorization. A server
can return `rankBadge: "piero"` or `levelIconId: 10000` while keeping the player's
ordinary `stats.level` and `stats.exp`. Only the icon changes; normal EXP and progress
remain intact. `rankVariant: "star"` selects a configured icon variant. Clearing the
override restores the normal icon. The reserved icon ids must not collide with
ordinary levels. Legacy responses that place a reserved value in `stats.level` are
accepted for display, deriving the underlying ordinary level from EXP when available.

The account page displays the original rank name when present and an EXP progress
bar when a consistent next boundary exists. The compact home row keeps the icon,
level and server-provided statistics without introducing another settings row.
