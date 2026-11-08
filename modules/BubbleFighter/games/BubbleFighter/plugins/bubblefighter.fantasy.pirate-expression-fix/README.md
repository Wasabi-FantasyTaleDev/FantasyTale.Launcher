# Pirate expression repair

This optional resource plugin restores the missing facial expression timelines for
the Dao-based raccoon pirate in Chinese 2011–2015 and 2019 clients. Enable the plugin before
launching. It requires Launcher 0.1.62 or later, including support for creating an
explicitly absent resource archive.

Supported client identities are `cn-279`, `cn-324`, `cn-406`, `cn-404`, `cn-408`,
`cn-705`, `cn-706`, `cn-717` and `cn-774`. `cn-705` has local executable identification
evidence but does not currently have a bundled build definition. Unlisted Chinese
clients, Korean clients and other regions are outside this package's scope.

The affected pirate uses character type `1` and head equipment `100184`; shared
pirate head `100218` also selects this expression family for character type `1`.
Its existing face mesh and expression textures are already correct. The package
adds the 193 missing `Models/Characters/Dao/raccoon2/*.face` timelines from kr-812.
Every texture they reference was verified against all supported target clients.
It contains no replacement character mesh, texture or gameplay configuration.

CN2011 and CN2019 add those members to their respective existing model packs,
preserving the original PKN format and members. The BFZ clients temporarily
receive the missing dedicated expression archive. Preparation merges
resource contributions, detects conflicts and verifies payloads before launch.
The original pack is restored, or the newly created archive is removed, after
the game exits or during interrupted-launch recovery. An existing expression
archive is rejected rather than overwritten.

The source repository contains the definition, packaging rules and maker only.
Decoded client payloads belong in an ignored output folder. Create a package with
`tools/package-pirate-expression-fix.py`, a reviewed member inventory and the
existing `CharacterResourceMaterializer` tool. The maker rechecks executable
timestamps, complete member-set digests and all archive reads before producing
the package and ZIP.

Offline resource and recovery verification establishes the candidate's file
behavior. Visual acceptance in a running pirate match remains required.

See [client evidence](../../../../docs/hook/pirate-expression-fix.md) for the
character/equipment relationships and native loader addresses.
