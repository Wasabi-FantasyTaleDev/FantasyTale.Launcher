# Creating a missing resource archive

The Bubble Fighter `archive-members` provider accepts `create = true` on an
archive row. Omission or `false` retains the existing requirement for an input
archive. Creation is currently supported for BFZ only; an absent PKN needs an
explicit format contract before it can be supported.

This contract first ships in Launcher 0.1.62. A package that declares creation
must set `plugin.minimum_launcher_version = "0.1.62"` or a later version so an
earlier launcher cannot accept a plan it cannot prepare.

```toml
schema = 1

[[archives]]
path = "Models/Expressions/Expressions.bfz"
create = true

[[archives.members]]
path = "Models/Expressions/idle.face"
source = "payload/idle.face"
sha256 = "<lower-case SHA-256>"
original_sha256 = ""
```

Every member must be an addition. A removal or nonempty original member digest
is invalid. The target must not exist, including when a cached composition is
available. A directory, filesystem link or unsafe relative path is rejected.
Field edits require an existing member and cannot target a newly created archive.

Multiple plugins may contribute members to the same missing archive when all
declare the same absent baseline. Their claims pass through ordinary conflict
resolution, including explicit user choices for different payloads at one member
path. Mixing existing and absent baseline declarations is an error. The cache
and conflict snapshot distinguish the missing baseline from any existing file.

The prepared resource carries `ExpectedAbsent = true` separately from its
optional original digest. The shared launch service preserves that precondition
when creating the file transaction. Deployment rejects a destination that appears
before or during staging and uses a non-overwriting move for a new target at
commit. Preparation never writes directly to the client directory.

The existing transaction journal records a null original hash for the new file.
Normal exit and interrupted-launch recovery delete only the verified deployed
file. An external change retains recovery evidence and is never silently removed.
Empty parent directories created for deployment may remain after restoration.
