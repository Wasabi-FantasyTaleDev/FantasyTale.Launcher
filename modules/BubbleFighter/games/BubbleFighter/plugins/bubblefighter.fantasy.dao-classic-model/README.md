# Classic Dao model

Optional replacement for character item 300001. Only existing members under
`Models/Characters/Dao/Default/` are changed. Costumes and other resources remain
byte-identical; new-only face variants are preserved. Enable before launching.
Launcher backs up original archives, restores them after game exit and recovers
stopped-client deployments. Disabling uses the original resources next launch.

Supported identities: cn-0-92-1, cn-774, kr-1256. CN2018 is `cn-0-92-1` (PE timestamp
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
KR Dao 00005 and CN2018/CN2019/TW/KR Dizni secondary face-variant packs. This
reduces the initial 14-pack plan to nine packs without dropping client content.

Acceptance checks cover original member counts, all unchanged-member hashes,
archive verification and extraction round trips. A separate Launcher harness
checks shared-library ZIP import, concurrent deployment of both character
plugins, restoration, disabled selection, stopped-client recovery, corruption
rejection and immutable store installation against copied client archives.
