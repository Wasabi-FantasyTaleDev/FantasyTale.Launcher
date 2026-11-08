# Numeric health (experimental)

[Chinese](README.zh.md)

Version 0.2.10 defaults to the original Indonesian position, yellow digits, normalized health and an enabled percent sign. Saved option overrides remain unchanged when switching versions. The maintainer confirmed both percent artwork and original placement on an x64 kr-812 retail client after selecting 0.2.9-rc.1; the earlier missing symbol on ARM64 remains unresolved. Raw HP deliberately omits the percent sign because it is a health value rather than a percentage.

Candidate 0.2.9-rc.1 adds the supplied percent artwork after normalized HP. The
`show_percent` option defaults to true; raw HP never receives a percent sign.
The package includes the original 20x18 PNG and a generated one-frame SMF under
`assets/`. Both are loaded from the absolute plugin directory through the game's
resource manager. No game archive or loose client file is replaced. A private
character table maps the symbol to that frame. The native text renderer measures
the current digits so the symbol follows their actual width and scaling. Its
formatting wrapper receives `%%` to render a literal percent.

Activation requires the bounded PNG header/dimensions and exact generated SMF
descriptor. Missing or invalid assets reject activation before patches are
registered. Frame ownership, count, rectangle and texture are checked again at
draw time; pending resources suppress only the symbol with bounded diagnostics.
Local activation requires the matching `assets/` directory. Its availability in
the private workspace does not establish redistribution rights. The reviewed
public source export excludes these sprite assets; its synthetic tests require
no private artwork and do not produce a complete, distributable plugin package.
The x64 retail percent and placement acceptance above supersedes this candidate gate.
The ARM64 retail run validated a resident one-frame texture, but the maintainer
still saw no percent. Architecture is a comparison variable, not a confirmed
cause. The candidate logs the measured digit and symbol sizes and final symbol
coordinates once. An x64 comparison uses the same assets and native draw path.

Version 0.2.8 defaults to the original Indonesian placement above the HP gauge.
id-1809 `sub_789AB0` uses gauge anchor +(125 - 7*digit_count, -101); both colors
now share that calculation, keeping the right edge aligned as HP changes digit
count. The optional custom placement retains the previous offset controls and
their values. Offsets apply only when custom placement is selected. White was
the default color in that release. The maintainer confirmed this placement in a kr-812 retail
match. This positioning change does not resolve yellow texture visibility.

One plugin identity supports future build descriptors. Runtime addresses are read
from `client-versions.toml` beside the DLL rather than compiled into the hook.
The file includes measured CN2011 through CN2019 research rows, including
separate resource-release identities; currently only the measured fixed-base
kr-812 image is accepted. Timestamp, packed/reconstructed image size,
HUD call bytes, optional resource signatures and the maximum-health vtable entry
are verified before patching.
The selected player is captured from EDI, not the HUD index holder. A not-yet-ready
maximum-health entry retries within the existing bounded unpacking deadline.

Supported descriptors declare `health_accessor_abi = "thiscall_f64"` or
`"edi_field_f64"`. The EDI form passes player + `health_field_offset` and preserves
EDI while retaining the floating return. `maximum_health_source = "vtable_f64"`
requires `maximum_entry_rva`, `maximum_health_rva` and `maximum_vtable_slot`, a byte
offset aligned to four bytes in 4..4096. The active KR descriptor uses 356;
the drawing path no longer assumes that slot for every build. `player_capture`
selects either preserved caller EDI (`caller_edi`) or the HUD's first pointer
(`hud_owner`). Owner capture rejects unreadable/null owners instead of falling
back to EDI. Supported descriptors must declare `hud_register = "ebp"` or
`"ebx"`; patch staging selects the matching x86 forwarding stub. Both retain
the seven original argument slots and the caller's callee-saved registers.
Alternatively `maximum_health_source = "player_field_f32"` reads a float at
player + `maximum_health_field_offset` and forbids the three vtable fields.
Field offsets must be in 1..65536. Unreadable fields and nonpositive/nonfinite
maximums suppress additional drawing. Other register or accessor ABIs still
require their own verified contract.
Controlled x86 tests use a different vtable slot and invalid EDI to distinguish
the two capture paths. These tests do not enable any research-only descriptor.
Distribute the DLL and its matching descriptor together.

Native sprite layout also comes from the descriptor. Supported rows require
`native_style_slot` (18..30), `native_style_sprite_index` (after the selected
128-character map, below 4096), `sprite_frames_offset`, `sprite_texture_offset`
and `texture_resident_offset`. Member offsets must be aligned to four bytes in
4..4096; texture cannot overlap the two frame-list pointers. Private font tables
are bounded to 4096 words. The active KR row retains style 22, sprite word 3079
and offsets 120/136/144. Earlier researched clients can select style 21, word
2822 and offsets 48/60/72 without changing the DLL. This does not replace their
required sprite loading/string/options ABI verification or retail acceptance.

The descriptor also records `resource_string_inline_capacity` and
`resource_options_size`. The measured research values are seven UTF-16 code
units and 32/40/88 bytes for CN2011/CN2012/CN2013 respectively; KR2015 uses
seven and 88. The loader allocates the selected options size, but the research
rows remain disabled until their manager and callable entry contracts are
accepted.

CN research records distinguish floating HP from the original integer Life
counter divided by 20. The original Life text remains untouched. The EDI HP and
field-maximum adapters have controlled x86 coverage, but CN player/anchor
provenance and resource contracts remain unaccepted; these rows cannot load.

Version 0.2.5 restores the initial native white slot-5 drawing path and offsets
(80, -30), after yellow resource and later-HUD experiments failed immediate retail
visibility. The initial white implementation was verified for full health and
self-inflicted damage; this restored package still needs fresh retail acceptance.
It preserves the original life counter, opacity and scale. It displays truncated
current/max HP times 100, or raw HP when configured. Invalid data suppresses the
extra draw and logs once. First-draw coordinates and opacity/scale are logged once.

The yellow resource was observed once alongside temporary white comparison digits
after a window change, but later runs remained invisible despite resident textures
and successful native draw returns. A later-HUD experiment also had a reported
brief torn image; causality is not established. These experiments are withdrawn
from the active implementation. Yellow styling still requires platform-specific follow-up on ARM64.
Percent-symbol presentation is implemented in 0.2.9-rc.1 with the supplied artwork.
The plugin does not replace original client assets. Redistribution of the supplied
artwork remains subject to separate provenance and permission review.

Build numeric_health.vcxproj in Release|Win32. Place the built numeric_health.dll,
`client-versions.toml`, plugin.toml, plugin-ui.toml and the assets folder for import. The separate native tests cover the actual x86
forwarding stub, raw/normalized values, invalid data, original draw preservation,
image rejection and delayed maximum-health readiness. Synthetic tests do not prove
visible retail rendering. Validate first-match visibility, respawn, spectating,
mode multipliers, scaling, exit/re-entry and combinations before release.
Changes require a client restart; no live unload is provided.

The restored retail run displayed white 71 after a wait, confirmed by the
maintainer and a screenshot. Immediate visibility remains unresolved, including
on this original white path.

Version 0.2.6 adds optional `trace_timing`, disabled by default. At most seven
snapshots per process record time since plugin entry and first intercepted draw,
call count, validation result, position, opacity, scale and HP/max HP. Checkpoints
are first draw and 1/5/15/30/60/120 seconds after it; missed checkpoints coalesce
into one current record. These are submission observations, not visible-pixel
confirmation. The trace does not reset or grow indefinitely on match re-entry.
