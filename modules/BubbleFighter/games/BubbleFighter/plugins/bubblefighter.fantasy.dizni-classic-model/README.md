# Classic Dizni model

Optional replacement for character item 300002. Version 1.0.1 restores existing
members under `Models/Characters/Dizni/Default/`, matching `.face` expression
selectors and `.mtn` animations from China 2017. Costume meshes and unrelated
resources remain byte-identical; newer-only actions and textures are preserved.
Enable before launching.
Launcher backs up original archives, restores them after game exit and recovers
stopped-client deployments. Disabling uses the original resources next launch.

Supported identities: cn-0-92-1, cn-774, tw-454, kr-1256. CN2018 is `cn-0-92-1` (PE timestamp
0x5BDC5A1F), not cn-742; installation requires a matching Launcher build profile.
CN2019 is cn-774; Taiwan 2019 is tw-454; Korea is kr-1256.
Taiwan already includes classic Dao and does not need the Dao plugin.

Classic sources are tw-454 Dao (90 members) and cn-742 Dizni (75 members).
Names come from tw-454 and kr-1256 `Config/ShopItem2.xml`, rows 300001 and 300002:
TW `藍寶` / `痞子妹`, KR `다오` / `디지니`.

Run `python tools/package-classic-character-models.py --help` for packaging.
The maker checks executable fingerprints, rejects duplicate Default paths,
replaces only existing members, omits unchanged packs, verifies decoded output
and reuses a payload only when the complete member map and target URI match.
Client archives and payloads must never be committed. Store installation is
optional and does not change client policy. Automated archive and restoration
checks do not substitute for owner in-game testing on each supported client.

## Complete-pack reuse

CN2018 and CN2019 share the same default character models but not their complete
PKNs: costume meshes in `100000/100054/Head.msh` and
`102000/102000/Head.msh` differ. Taiwan sharding also contains members absent from
the corresponding China shard. Complete patched member maps do not match, so
these builds keep independent payloads. Unchanged archives are omitted instead:
KR Dao 00005 and unchanged Dizni shards. Unlike 1.0.0, 1.0.1 also includes any
shard containing a changed facial selector or animation.

## Facial and animation repair

The initial Default-only package retained incompatible modern facial selectors.
For example, kr-1256 `common/pis-basic-stand.face` selects `f00-02.png` while
cn-742 uses `f00.PNG` and `f01.PNG`. The matching idle animation also differs:
143,040 bytes in kr-1256 versus 43,732 bytes in cn-742. The repair restores
same-path source expressions and motions together, retaining target ownership
of every member and rejecting cross-shard duplicates. This includes 403 changed
facial selectors and 40 changed animations for kr-1256. China and Taiwan retain
their already matching motions. Later actions without a classic counterpart
remain native; the plugin does not invent classic animations for those actions.

The target-only `common/pis-stand-die2.face` already references an absent `f28.png`
in the original clients. That pre-existing reference is recorded during dependency
verification; the repair does not introduce it or fabricate a replacement image.

Acceptance checks cover original member counts, all unchanged-member hashes,
archive verification and extraction round trips. A separate Launcher harness
checks shared-library ZIP import, concurrent deployment of both character
plugins, restoration, disabled selection, stopped-client recovery, corruption
rejection and immutable store installation against copied client archives.
