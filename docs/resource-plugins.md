# Game resource plugins

Resource packages use `kind = "resources"` and delegate their transformations to
the game's optional `IGameResourceModule`. Shared Core knows no game archive
formats, character names or build-specific member layouts. Resource packages are
never passed to the native hook loader.

```toml
schema = 1
[plugin]
id = "example.resources"
version = "1.0.0"
game = "bubblefighter"
kind = "resources"
minimum_launcher_version = "0.1.36"
name = { en = "Example resources" }
[resources]
provider = "archive-members"
manifest = "resources.toml"
[[options]]
key = "enabled"
type = "bool"
default = false
label = { en = "Enable replacement" }
```

The BubbleFighter module exposes the reusable `archive-members` provider. Other
games may register their own provider identities. An unknown provider fails before
any client write. `resources.toml` selects package-relative plans:

```toml
schema = 1
[[plans]]
build = "*"
when = "enabled"
path = "plans/example.toml"
```

Omit `when` for an unconditional plan; otherwise it must name a declared boolean
option. A build selector defaults to `*`. Build selectors choose verified resource
plans and do not prevent opening settings. The plan format is:

```toml
schema = 1
[[archives]]
path = "Packs/Models_00000.pkn"
[[archives.members]]
path = "Models/Characters/Example/Default/Body.msh"
source = "payload/body"
sha256 = "<64 lower-case hexadecimal characters>"
original_sha256 = "<digest of the current decoded member>"
```

An absent/empty `original_sha256` requires a new virtual member. `remove = true`
instead of source/hash removes a member and requires its original digest. All
paths are relative, reject traversal and filesystem links, and preserve the case
needed by the client's cipher. PKN versions are detected from the current file;
the early format retains its root, unknown header fields and case-sensitive URI.
BFZ key derivation also uses the original client-relative destination path.

An archive row normally requires an existing file. From Launcher 0.1.62,
`create = true` explicitly requires an absent BFZ archive and starts with no
members. Packages using it must declare `minimum_launcher_version = "0.1.62"`
or later. Omission or `false` keeps the existing-file requirement. Creation does
not support PKN, member removals, original member digests or field-level edits.
Every contributing plan for the same archive must agree on its absent baseline;
ordinary member conflict choices still apply. Preparation and deployment reject
an existing destination, including when a cached output is reused. Recovery
removes the verified new archive; it may leave empty parent directories. See
[archive creation](hook/resource-archive-creation.md) for the full contract.

All packages contribute edits before any archive is written. Identical member
claims coalesce; conflicting contents or input digests fail. Each physical archive
is composed once. Untouched PKN encrypted blocks and BFZ local records are copied
directly and verified byte-for-byte, including their original index metadata.
Changed members are decoded and hash-verified after composition. This preserves
unrelated resources without decrypting/recompressing a whole pack.

Preparation uses at most four concurrent archive jobs and one member at a time per
job, with disk-backed PKN spooling. Progress reports measured member/verification
counts and archive completion; cancellation cleans staging before deployment.
The persistent instance cache is outside the game folder. Its identity includes
the current original archive's SHA-256, virtual edits, payload digests and composer
version. Cache reuse verifies the prepared file hash. Each original digest is
checked again while staging its pristine backup, preventing stale preparations
from overwriting a newly updated client.

## Deployment, integrity and recovery

Prepared outputs enter `RuntimeDeployment` under the same cross-process client
lease as launching and client repair. Originals are copied, hashed and flushed
before any replacement. A durable preparation marker precedes staging; a flushed
journal is atomically renamed before replacing the first file. The original
backup is never reconstructed from a patched archive.

Normal exit or startup failure restores exact original files. A later Launcher
recovers an interrupted transaction only after confirming matching game processes
have stopped. Modified destinations or damaged required backups preserve recovery
evidence and refuse further writes. Recovery is repeatable when interrupted during
restoration or backup cleanup. A pre-journal interruption can discard only its
own staging files because no destination replacement has begun.

Client verification/download repair cannot acquire the running game's lease.
After a crash it must recover the interrupted resource transaction before checking
or replacing original client content. Preferences and prepared caches survive
restoration; the next launch can reuse a valid composition.

## Character selection capability

`character-models` uses the same composer and transaction. Its settings default to
`{}`: all native/provider models remain unchanged. Selecting the native generation
is also a strict no-op. Disabling the package preserves the stored preferences.

Call `IGameModule.GetCapability<ICharacterModelCapabilities>()` by interface to
register expansion-provided characters or read the last resolved preparation
snapshot. Registration returns a disposable lease. Descriptors contain namespaced
identities, verified generation plans and provider defaults. Native descriptors
come from package data; absent characters are hidden unless a provider registers
them. Provider changes invalidate preparations. A missing optional capability
means an expansion keeps its declared default resources; it must not require this
optional model plugin merely to exist.

An alternative resource package can declare incompatible complete-file packages
in `plugin.exclusive_with`. Enabling either alternative disables optional conflicts
atomically without discarding preferences or library versions. Required client
policy components stay protected; stored configurations cannot bypass the conflict
check. This keeps whole-archive predecessors from colliding with composed edits.

`Read` reports requested/effective generation, provider, replacement action and
provider revision. It describes a resolved preparation, not proof that a game
rendered the model. Consumers must use the launch's immutable selections rather
than infer them from patched client files. Importing new characters/items and
their server protocol support are outside this package.

## Fine-grained table and UI edits

`provider = "resource-edits"` edits records inside client tables and controls
inside native `.ui` forms instead of replacing whole members. Several packages may
edit one member: their edits are merged first, the member is regenerated once from
the user's own client copy, and the archive is composed once together with any
character model or archive-member plan. See
[examples/resource-edits](examples/resource-edits) for a complete package.

The manifest has three convenience sections and one generic section. `[[items]]`
addresses the item table by `id` with `name`, `description` and `set`; its default
action is `patch`. `[[strings]]` addresses a string table by `key` (an array for
composite keys) with `text`; its default target is `strings`, `xml-strings` selects
the composite-key table, and its default action is `upsert`, which patches an
existing record or adds a missing one. `[[edits]]` names any `target` and `action`
(`add`, `replace`, `patch`, `upsert`, `remove`). Every row accepts `build` (a name
or list) and `when` (a declared boolean option). `clear = ["field"]` empties a
cell; `expect = { field = "original" }` is a precondition. A field that is not
written keeps the original value; an empty string writes an empty value.

A value may be a language table such as `{ "zh-CN" = "…", ko = "…" }`. The client
language selected for the instance is used, then the build's native language, then
`fallback_languages`; otherwise `patch` and `upsert` keep the original text and
`add` and `replace` fail. Text is decoded and encoded strictly with the member's own
BOM, encoding, declaration and line breaks; an unencodable character is an error.

Targets are data. The module's built-in `resource-targets.toml` holds the archive,
member, worksheet, header row, key columns and logical field mapping of each known
build. A package's `[[targets]]` row with the same `build` and `name` overrides only
the keys it states; `fields` and `languages.<code>` merge field by field. Fields are
found by header text or `#N`, never by a fixed column number; a missing, duplicated
or ambiguous header, duplicate record key or unknown worksheet is an error. Current
defaults cover `cn-406`, `kr-812` and `kr-1256`. Modern YAML string tables and
other builds are not supported yet; a target for another build must be declared
explicitly.

Horizontal worksheets, whose first cell is `Name:HorzTable` (`FighterConfig.xml`),
hold one record per column: the `[Field]` `[Desc]` row carries record keys and each
later row is a field named in column 1. Rows named `#Section` start sections; a
field name used in two sections is written `#Section/name`, and an unqualified
repeated name is an error. The `fighter` target addresses `FighterConfig`; an edit
row's `sheet = "FighterConfigMode"` selects a sibling worksheet. Adding a record
appends a column, removing one deletes its column and shifts later cells, column
definitions and counts left. The layout is detected from the sheet; a target's
`orientation` (`vertical` or `horizontal`) is checked against it.

UI edits name `member` and `control`, a `/` path of `Name` or `Name[ArrayIndex]`
segments. `patch` changes only the attributes in `set`; `replace` and `add` take a
native `element` whose type already occurs in that form, and `add` takes an optional
zero-based `position`. `hide` is reserved and fails with
`resource.ui.hide-unsupported` because no native visibility field is proven.
`IGameUiForms`, available through `GetCapability`, parses forms read-only for
client plugins.

### CSV sheets

`[[tables]] path = "translations.csv"` (with optional `build`, `when` and a default
`target`) reads many edits of any targets from one sheet, as in
[examples/resource-edits/translations.csv](examples/resource-edits/translations.csv):

```text
target,key,field,member,action,value,zh-CN,ko,en,comment
item,1010002,name,,,,大炮,대포,Cannon,item name
item,1010002,price,,,500,,,,numbers and ids go in value
xml-strings,Ui|MainMenu|BTN_EXIT,text,,,,退出,,,composite key
fighter,1,name,,,,大头,,Bazzi,horizontal record
ui,pnlMain/btnExit,Text,Ui/MainMenuStage2.ui,,,退出游戏,,Quit,
```

`target`, `key` and `field` are required, `member` names the form of a `.ui` row.
Optional columns are `action` (default `upsert` for tables and `patch` for `.ui`),
`build` (one or more, separated by commas or spaces), `sheet`, `value` (text for
every language, for numbers and identifiers) and `comment`. Every other header is a
client language code. Composite keys join their parts with `|`. Rows for one record
and action merge into one edit, so a name and a description may sit on separate
lines; the same field twice, or two actions for one record, is an error. An empty
language cell means that language is not provided and follows the language rules
above; a row with no text at all is a template row and is skipped. Rows whose target
starts with `#` are comments.

Sheets may be UTF-8, UTF-16 or UTF-32 in either byte order, with or without a byte order
mark; Excel's "CSV UTF-8" and "Unicode Text" both work. Without a mark, UTF-16 and UTF-32
are recognised from the zero bytes of the ASCII header; `encoding = "utf-16le"` (or
`utf-8`, `utf-16be`, `utf-32le`, `utf-32be`) on the `[[tables]]` row declares it, and a
mark must agree. Tab or comma delimiters are detected from the header, and quoted cells may contain delimiters,
quotes written twice and line breaks. A sheet in a legacy code page such as GBK is
rejected instead of being read as garbled text. Reserved column names are lower
case; `Key` or `TARGET` is reported rather than read as a language. Errors name the
sheet and line. Sheet edits merge and conflict exactly like manifest edits.

### Regular expressions

`[[patterns]]` rows change many records or controls at once:

```toml
[[patterns]]                # wrap every item name
target = "item"
match = '.*'                # anchored regex over the whole key; composite keys join parts with |
fields = ["name"]
find = '^(.+)$'             # unanchored regex, replaced everywhere in the current text
replace = { "zh-CN" = "[$1]", en = "<$1>" }

[[patterns]]                # one value for a set of records
target = "item"
match = '10100\d\d'
set = { price = 500 }

[[patterns]]                # every OK button of every form
target = "ui"
member_match = 'Ui/.*\.ui'
match = '.*btnOk.*'
fields = ["Text"]
find = '^OK$'
replace = { "zh-CN" = "确定" }
```

`match` selects with a regex and `key` (`control` for `.ui`) selects one record exactly; a
row needs exactly one of them. `.ui` rows name `member` or `member_match`. A row either
substitutes with `find`, `replace` and `fields`, or writes with `set` and `clear`;
`action = "remove"` removes every matched record or control. `replace` follows the
language rules and may use `$1` and `${name}`; write `$$` for a literal dollar sign.
In a CSV sheet a row that fills `match` or `find` is a pattern: `find` and the
value or language cells are the substitution, `field` may list several fields with `|`,
and `member_match` selects forms.

Patterns compile with .NET's linear-time engine (`RegexOptions.NonBacktracking`), so no
pattern can stall a launch; lookaround, backreferences and atomic groups are rejected.
Matching is by UTF-16 code unit: a replacement that would split a character outside the
Basic Multilingual Plane is an error. Before merging, each plugin's patterns run in
declaration order against the user's original member, later patterns seeing earlier
results, and become ordinary patches and removals of the records that actually change.
They merge and conflict like any other edit; a plugin's own literal edit of the same
field or record wins over its patterns. Results are cached per archive and plugin by the
original archive digest and pattern content.

Some client tables repeat a key, and which record the client reads is not known. A
shared key addresses every record with it: a literal edit changes each the same way and
a pattern changes each record's own text. Same-named sibling controls are addressed as
`Name@0`, `Name@1` in document order, and unnamed controls as `@0`, `@1`;
`IGameUiForms` reports these paths.

### Conflicts and choices

Field names, logical aliases and `#N` references are resolved to the original member's
physical columns or horizontal field rows before merging. Implicit worksheet selection
and its explicit name share one identity. Targets addressing the same worksheet must
agree on header and key layout. Conflict `Field` values use canonical `#N` positions.
Every `expect` condition checks the untouched original, before any accepted edit runs;
two identical translations do not invalidate each other's conditions. A package that
contradicts its own writes is rejected as an invalid package.

Non-overlapping edits and identical edits merge regardless of package order.
Otherwise preparation raises `GameResourceConflictException` before any file is
staged. Its `Report` is serializable: `Snapshot`, `Build`, `Language` and
`Conflicts`, each with a stable `Id`, a `Code` (`resource.conflict.field-value`,
`duplicate-add`, `remove-edit`, `add-edit`, `row-vs-field`, `replace-replace`,
`parent-child`, `member-replace`), `Archive`, `Member`, `Target`, `Key`, `Field`
and the `Parties` with plugin id, version, localized name, action and a short
requested value. Retry with `LaunchContext.ResourceConflictChoices` (or the
`resourceConflictChoices` launch argument), each naming the snapshot, conflict id
and the plugin to keep. Only the losing operations of that group are skipped. A
choice applies only while the snapshot is unchanged: a different build, client
language, plugin version, option, manifest or original archive asks again.

Whole-member conflicts also cover two whole-member providers without any fine edits.
Keeping a whole-member provider removes all losing replacements and their provenance.
Keeping fine edits may expose a narrower field conflict; retain the previous choices
when answering the next report. Invalid current-snapshot conflict ids or winners are
rejected. The host bridge returns `code = "resource_conflict"` and `conflictReport`,
and accepts the `resourceConflictChoices` array without a fixed conflict-count ceiling.

The Launcher displays a conflict dialog with one information card per participating
plugin. The conflicting resource, requested changes and selection controls appear
outside those cards. Choose the plugin to keep for every conflict before continuing;
canceling leaves the game unstarted. These decisions apply only to the current launch.

Regenerated members are cached beside composed archives. The cache identity
includes the original archive digest, the ordered edit content and the builder
version, so a changed language, plugin or client rebuilds the member.
Physical address bindings have their own input-bound, integrity-checked cache, allowing
unchanged launches to reuse members without reopening their XML or text tables.

### Managed resource API

`BubbleFighterModule.GetCapability<IBubbleFighterResourceEdits>()` exposes target lookup,
native control inspection and a fluent `CreateManifest` builder for items, ordinary and
composite-key strings, and UI controls. `ResourceTargets` publishes the known build,
target, archive and member constants; the embedded TOML remains the source of defaults.
`ResourceTargetOverride` inherits omitted values and supports the same per-language
field maps as TOML. For example:

```csharp
var api = module.GetCapability<IBubbleFighterResourceEdits>()!;
var manifest = api.CreateManifest("en")
    .PatchItem("1010001", new Dictionary<string, string>
    {
        ["zh-CN"] = "Example translation", ["en"] = "Example item"
    })
    .PatchString("MSG_EXAMPLE", "Example message")
    .ToToml();
```

Add writes the key and supplied fields; it does not clone a template row. Replace
retains the key and clears undeclared non-key values. UI add and replace apply the
declared attributes to the inserted element as well as its XML fragment; identity
attributes cannot be changed. A newly added subtree participates in descendant
conflicts. XML encoding declarations must match the actual Unicode bytes, and
tab-separated keys cannot inject tabs or line breaks into the table.

A single plugin cannot replace a UI subtree while also editing its descendants,
or remove a subtree while writing descendant values. Such a plan is rejected
before staging: canonical application order must not silently erase a declared
edit. Put the complete desired subtree in the replacement fragment instead.
