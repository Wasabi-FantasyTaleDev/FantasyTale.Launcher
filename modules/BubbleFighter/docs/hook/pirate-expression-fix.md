# Pirate expression resources

The confirmed defect in the inspected Chinese 2011–2015 and 2019 clients is the absence of
all 193 `Models/Characters/Dao/raccoon2/*.face` timelines. The affected Dao-based
raccoon face already has its mesh and 18 expression textures under
`Models/Characters/Dao/Default/raccoon/`. All 19 decoded members match kr-812
byte for byte in every target. All 30 textures referenced by the kr-812 timelines
also match; 17 are in that raccoon directory and 13 are ordinary Dao textures.
The original event order and the original references are retained.

The repair imports the missing timelines only. Replacing the Korean NPC mesh or
textures would duplicate bytes already present and would not restore the missing
animation-to-expression links by itself. In the inspected 2011–2015 clients,
existing Uni, Bazzi and Kephi raccoon timelines and facial assets match kr-812.
These findings establish a Dao-family resource defect across the nine inspected
builds; they do not establish a
separate defect in every pirate appearance.

## Character and equipment identities

These pirates reuse the character and equipment system. The numbers below are
character types and equipment item IDs, not new playable character IDs or NPC
instance IDs.

| Character type | Skeleton/resource family | Head | Shirt | Pants | Gloves | Shoes | Expression directory |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 5 | Uni | 100183 | 100187 | 100191 | 100195 | 100199 | `Uni/raccoon1` |
| 1 | Dao | 100184 | 100188 | 100192 | 100196 | 100200 | `Dao/raccoon2` |
| 3 | Bazzi | 100185 | 100189 | 100193 | 100197 | 100201 | `Bazzi/raccoon3` |
| 4 | Kephi | 100186 | 100190 | 100194 | 100198 | 100202 | `Kephi/raccoon4` |

The client `Config/ShopItem2.xml` `ShopItem` sheet identifies those rows as
clothing and records `Head`, `BodyU`, `BodyD`, `HandR` and `Feet` attachment
locations. The same rows identify `100218` as shared pirate head equipment and
`100219` as shared gloves. In the native face selector, `100218` dispatches by
character type 1/3/4/5 to the four families above.

The kr-812 and cn-324 `Config/FighterConfig.xml` `AiFighterConfig` sheet binds
`prologue_pirate_taro1` to character type 1 and head 100184,
`prologue_pirate_toro1` to type 5 and head 100183, and
`prologue_pirate_tau1` to type 4 and head 100186. It also binds `pirate` and
`pirate_gatekeeper` to type 1/head 100184. The `pirate_teo`, `pirate_roger` and
`pirate_mapu` columns use shared head 100218 with types 1, 5 and 4 respectively.
The client `NpcMode/Scripts/Uni.lua`, `Bazzi.lua` and `Kephi.lua` independently
bind their NPC templates to the matching character types and head equipment.
Clothing category and attachment interpretation follows the item table; some
legacy Lua field labels for gloves/shoes are interchanged.

kr-812 sub_6179E0 and kr-812 sub_936970 select `raccoon1` through `raccoon4`
from heads 100183–100186 and return the corresponding
`Models/Characters/<family>/Default/raccoon/Face.msh`. cn-324 sub_52AC70 and
cn-717 sub_66A830 implement the same mapping. cn-279 sub_642B40 selects the
expression family by character type during NPC construction; cn-279 sub_7DE960
also binds those types to the equipment IDs listed above.

cn-774 sub_5849F0 still selects `raccoon2` for head 100184 and for shared head
100218 with character type 1. cn-774 sub_B5C730 binds the type-1 pirate to head
100184, shirt 100188, pants 100192, gloves 100196 and shoes 100200, and selects
the same expression family for its later pirate equipment branch.

The playable Leo/Lavi resource families are separate from these original NPC
bindings. Later copied raccoon assets under other character folders do not make
those characters the identity of the original pirates. `NpcMode/Models/Npc/Squirrel*`
is another NPC asset family and is outside this repair. Dizni resources, including
the repaired classic head, are outside the package's target paths.

## Expression and archive lookup

cn-324 sub_8F0F10 enables an expression override and stores its directory name.
cn-324 sub_8FD9B0 substitutes that directory into the current animation path,
changes the extension to `.face`, and reads `StartTick` and `ResourcePath`.
It inserts events into a tick-keyed map, so the source CSV need not be sorted.
cn-324 sub_8F13B0 selects the event for the current animation tick, loads its
texture through cn-324 sub_8D3110 and updates the facial material. If the
timeline is absent, its empty event map reaches the no-expression-texture path.

The corresponding timeline readers include cn-279 sub_54D590,
cn-406 sub_9D5F10, cn-404 sub_AA7E00, cn-408 sub_AB0650,
cn-705 sub_A76740, cn-717 sub_BC0C20 and kr-812 sub_AF84C0.
cn-706 sub_5A84D0 independently confirms the equipment selector, and its decoded
facial assets and missing timeline set match the other targets.
cn-774 sub_1017F70 prepares the expression timeline lookup, and cn-774 sub_101A940
reads the same `.face` data through its `StartTick` and `ResourcePath` columns.

For example, kr-812 `Dao/raccoon2/pis-stand-idle.face` selects raccoon `f00.png`
at tick 1, `f01.png` at tick 10, `f00.png` at tick 73, `f01.png` at tick 90 and
`f00.png` at tick 95. Those target textures already exist unchanged in every
Chinese client covered by the package.

cn-324 sub_8637F0 derives a BFZ filename from the requested resource's parent
directory and the `.bfz` suffix. Therefore a new `Dao/raccoon2` timeline belongs
in `Models/Characters/Dao/raccoon2/raccoon2.bfz`. Putting it into an unrelated
existing BFZ would not satisfy that lookup. The Chinese target clients do not
have that archive. The resource plan explicitly requests a missing BFZ baseline;
it rejects an existing file and shares normal conflict, cache and deployment
handling. The existing recovery journal removes the new file after use.

cn-279 uses an indexed PKN virtual filesystem. Its plan adds members to the
existing `Pack/Models0001.pkn`, preserving all original stored members and metadata.
The archive composer retains the case-sensitive early PKN root and URI format.

cn-774 uses the newer PKN format under `Packs/`. A complete scan of all 245
archives, containing 140773 members, found no `Dao/raccoon2/*.face` members.
Its 19 face assets and all 30 required textures match the reviewed kr-812 bytes.
The plan appends the missing timelines to the existing
`Packs/ModelsChrPlDao_00003.pkn`, which already contains its Dao raccoon face
assets. It preserves that archive's format and every original member; no new
archive is needed for this build.

## Scope and packaging evidence

| Build | Local client date | Selector evidence | Plan |
| --- | --- | --- | --- |
| cn-279 | 2011-04-08 | cn-279 sub_642B40 | Add to existing early PKN |
| cn-324 | 2012-09-22 | cn-324 sub_52AC70 | Create missing BFZ |
| cn-406 | 2013-06-28 | cn-406 sub_50EB60 | Create missing BFZ |
| cn-404 | 2014-04-03 | cn-404 sub_5C31D0 | Create missing BFZ |
| cn-408 | 2014-05-26 | cn-408 sub_5C59D0 | Create missing BFZ |
| cn-705 | 2014-07-31 | cn-705 sub_5A83B0 | Create missing BFZ |
| cn-706 | 2014-09-23 | cn-706 sub_5A84D0 | Create missing BFZ |
| cn-717 | 2015-06-18 | cn-717 sub_66A830 | Create missing BFZ |
| cn-774 | 2019-05-30 | cn-774 sub_5849F0 | Add to existing newer PKN |

The executable identity table, `games/BubbleFighter/client-versions.toml`,
records cn-735 with PE timestamp `0x5767E697` and signature timestamp
`2016-06-20T12:52:06Z`. This establishes it as a 2016 client rather than part of
the 2011–2015 target range. It remains excluded. The separately verified cn-774
is the only target outside that initial date range.
cn-705 is recorded in client identification data
without a bundled build definition; this package does not create one.

The maker pins the 193-member timeline set, 19 facial assets and 30 texture
dependencies using SHA-256 over sorted path/length/content-digest records. It
then asks the existing archive materializer to read and verify those bytes from
the supplied client roots. The final ZIP contains 110 distinct content-addressed
timeline payloads and three plans shared across the nine build selections.
No client asset is committed to the source repository.

Launcher 0.1.62 is the minimum candidate version because the package uses
explicit archive creation. Unit verification exercises composition, conflicting
member additions, cache reuse, creation preconditions, cancellation and recovery.
Real-client archive preparation and isolated transaction checks can establish
the on-disk result. Acceptance of facial animation during a running pirate match
must still be recorded separately.
