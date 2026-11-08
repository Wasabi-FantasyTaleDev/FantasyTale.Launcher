# Scene styles

`bubblefighter.fantasy.scene-styles` stores `main_style` and `plaza_style`
independently. Both default to `original`, which contributes no scene resource plan.
The `snow` value selects the Korean 2011 winter resources for the corresponding
scene. Initial compatibility is limited to cn-279, cn-324 and cn-406.

Version 1.0.2 requires Launcher 0.1.66 because its `archive-members` manifest uses
the generic `when_options` compound condition. Additional styles can extend the
declared enum and add matching plans without changing saved option keys or
introducing a scene-specific resource provider.

## Resource boundaries

`tools/scene-style-evidence.toml` pins the source and target executable timestamps,
decoded inventory counts and digests. The packager additionally verifies every
input archive digest and each payload's decoded digest and size. Game assets and
local inventory paths remain in ignored outputs.

Scene plans only change members under these local directories:

- `Scenes/stage_Main/` and `Dds/Scenes/stage_Main/`;
- `Scenes/stage_Plaza/` and `Dds/Scenes/stage_Plaza/`.

Music plans separately change `Sounds/SoundList.xml`. Outfit plans own the exact
`Models/Characters/NPC_Lomi/Default/` and `NPC_Pini/Default/` directories. CN2011
also receives missing Pini common motions and facial timelines in its existing
Models0004 PKN shard; target members that already exist remain authoritative.

Existing members remain in their original target shards and retain their target
spelling. New members use the evidence descriptor's scene-specific owning archive.
CN2011's early PKN shards are composed from their original contents. The later two
clients keep separate BFZ archives. Plans bind each replacement to its original
decoded digest; the shared composer rejects changed baselines and conflicting
edits before replacing a client archive.

The winter scene's eighteen external texture dependencies are packaged as local
DDS companions. This preserves other scenes' global texture pools. Their source
members and exact digests are declared alongside each style in the evidence file.

## Native texture lookup evidence

The WB material readers combine the current scene file's directory with the
material's stored basename before requesting its texture:

| Build | WB material reader | Directory helper | Texture loader |
| --- | --- | --- | --- |
| cn-279 | cn-279 sub_5649C0 | cn-279 sub_4F4D40 | cn-279 sub_52DBB0 |
| cn-324 | cn-324 sub_914760 | cn-324 sub_8B7580 | cn-324 sub_8D3110 |
| cn-406 | cn-406 sub_9EDA20 | cn-406 sub_956D70 | cn-406 sub_99E930 |

Each loader's first candidate is `Dds/` followed by the supplied path with its
extension replaced by `.dds`. The original supplied path follows, then registered
fallback directories. CN2011 stores the prefix and extension at 0x94F638 and
0x94F62C. CN2012 stores them at 0xA623D4 and 0xA623C8. CN2013 stores them at
0xB57BD4 and 0xB57BC8; cn-406 0x99E993, 0x99E9B6 and 0x99E9CE preserve the
actual basename/prefix/extension call sequence, including an argument omitted by
the decompiler's inferred string-helper prototype. All eighteen dependency
references in the source WB are basenames, so their scene-local DDS is attempted
before the global fallback.

The package changes no UI forms, SceneMeta, camera code or executable instructions.
CN2013's Resolution Extension continues to own its MainMenuStage2.ui loader wrapper
and extended canvas sizing. Original native resolutions retain their existing
resource and layout behavior.

## Packaging and validation

Create a new output directory using the reviewed inventories and extracted blobs:

```powershell
python tools/package-scene-styles.py --inventory <inventory.json> --blobs <blob-directory> --output <new-output-directory>
python -m unittest discover -s tools/tests -p test_scene_styles.py -v
```

The ZIP contains `plugin.toml` at its root for direct import. The package report
records the ZIP digest, per-plan archive/member counts and the unverified live
rendering status. Deterministic timestamps and sorted payload entries make repeat
packages reproducible.

An independent helper using the published resource composer has checked 288
manifest combinations and exercised 37 real compositions across the three builds,
covering independent scenes, music, NPC variants and combined plans. It checks
changed decoded hashes, every unchanged decoded member, original
archive preservation, CN2011 shared-shard merging, conflict rejection and stale
baseline rejection. The composer also checks stored bytes and metadata of
untouched archive members internally. These checks establish resource composition;
the real clients' rendering, camera placement and input hit targets still require
visual acceptance.

## Christmas music evidence

The BGM worksheet in the original `Sounds/SoundList.xml` stores the description,
key, FMOD group, event and two parameters in columns 0 through 5. The winter
resource comments explicitly describe switching these rows for the Christmas
season and restoring them afterwards. All three target clients already contain
the carol events and sound banks, so the package changes the group/event cells
and preserves every other byte; it does not need to replace an encrypted FSB.

| Scene | Native keys | FMOD group | Event |
| --- | --- | --- | --- |
| Lobby | `m-lobbystage-rollback`, `m-lobbystage` | `BF/music/Pregame/carol` | `stage-lobby-carol` |
| Plaza | `m-multistage-rollback`, `m-multistage` | `BF/music/Pregame/carol` | `room-multi-carol` |

CN2011 requests `m-multistage` at cn-279 0x7A933F in cn-279 sub_7A8550.
The main-screen calls at cn-324 0x56D7FB and cn-324 0x5870B4 use cn-324
sub_4116D0; cn-406 0x55699E and cn-406 0x579C5B use cn-406 sub_421480.
The plaza's `LobbyBGMKey` comes from `Config/Lambient.xml`: cn-324 sub_729CF0
stores it at the configuration object plus 120 and cn-324 sub_57DFF0 submits
it to cn-324 sub_4116D0. CN2013's cn-406 sub_74C600 stores it at plus 236;
cn-406 sub_56AC20 copies it into the sound manager's pending key at plus 304.
The winter reader kr2011-winter sub_9577F0 likewise reads `LobbyBGMKey`.

`christmas_bgm` defaults to true. Three exact `when_options` combinations select
main-only, plaza-only or both rows, so only one plan writes the shared sound table.
Turning the option off preserves the client's original table. CN2011's original
`m-multistage` already selects a carol, so an unchanged row can be a valid no-op.

## NPC outfit evidence and selection

FighterConfig identifies Lomi as `NPC_Lomi` (103) and Pini as `NPC_Pini` (104).
The plaza creates these names in kr2011-winter sub_7EDD30, cn-324 sub_5D4410
and cn-406 sub_5D2680. Unequipped parts use Default paths in kr2011-winter
sub_867650; kr2011-winter sub_6A6E00 loads
`Models/Characters/%s/Default/%s.msh`. The winter Default Lomi meshes match
the retained `snata_Model` meshes exactly, and its Pini meshes match `Santa_Pini`.
This establishes an official resource replacement rather than a required runtime
holiday flag. The selected meshes retain their original version-3 format.

`lomi_outfit` selects `default`, `original` or `christmas`; `pini_outfit` selects
`default`, `original`, `christmas` or `zombie`. Both options start at Default,
which contributes no outfit plan and preserves the selected client's resources.
Lomi Original also preserves its target resources. Pini Original preserves the
already-normal CN2011 selection; its evidence rule limits the replacement to
CN2012/CN2013 using Korean 2015 resources (PE timestamp 0x55376C47).
Christmas uses the Korean 2011 winter resources, and Little zombie uses Chinese
2012 resources. The zombie meshes reference `zombie_Arm.PNG`, `zombie_Baby.PNG`,
`zombie_Legs.PNG` and `zombie_Head.PNG`. Pini Original is therefore an explicit
replacement on CN2012/CN2013 even when both scene settings remain Original.
CN2011's archived inventory is missing all Pini members; Christmas and Little
zombie plans supply the selected Default-path resources and missing common
animations, while Default and Original contribute no changes. CN2012/CN2013
already contain the zombie variant in their archived Default paths.

Every selected mesh's texture references are parsed and resolved before packaging.
Different or missing textures become companions beside the NPC mesh, using the
same native local-first texture lookup as other character resource plans. Global
texture pools and existing common motions remain unchanged.

`christmas_npcs` defaults to true and is a settings-page convenience. Switching
either the lobby or plaza to Snow, or enabling automatic outfits while either
scene is Snow, selects
Christmas for both NPCs. Subsequent manual choices take precedence. Configuration
acknowledgements and reloads synchronize saved values without triggering automatic
selection. Runtime resource conditions use the saved outfit enums directly.
