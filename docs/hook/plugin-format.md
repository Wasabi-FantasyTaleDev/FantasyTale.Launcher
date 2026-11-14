# Hook plugin format (`plugin.toml`, schema 1)

[Chinese](plugin-format.zh.md)

Launcher-side `kind = "files"` packages use the [file replacement contract](../file-plugins.md). Starting with Launcher 0.1.54 and FtHook 3.7.1, a file package may also declare build-scoped `[[patches]]`. Only builds with a matching patch include that package in the native session. File deployment remains exclusively in the launcher; the runtime validates file metadata but never opens or installs its payloads. File packages without patches remain launcher-only.

`kind = "overlay"` uses the [Overlay foundation](overlay.md), and `kind = "service"`
selects a built-in launcher provider through `[service]`; see the
[companion service contract](companion.md). Both are launcher-owned and excluded
from native session entries. Service packages use the normal library/options UI,
cannot carry patches/DLLs/file replacements, and are optional unless an explicit
trusted policy declares otherwise.

`kind = "assets"` is a data-only package that other packages share; it is launcher-owned, never a native
session entry, and is described in [shared data packages](../plugin-management.md#shared-data-packages-and-requirements).
Any package may declare `requires` in `[plugin]` as described there.

A hook plugin is a folder whose root holds `plugin.toml`. The FtHook runtime
reads it inside the game process; the launcher reads the same file to show the
plugin, check compatibility and render its options. Adding a plugin never
requires rebuilding the host DLL.

Native consumers may optionally declare `[companion.messages]` with exact schema
numbers, requiring runtime >=3.3.3; see [native companion consumers](companion.md#native-consumers-runtime-333).
This declares grants only, never a server address, credential or implicit foundation activation.

Plugin configuration HTML pages and enum-label sidecars have no fixed byte or row
ceiling. Deferred upgrade records have no fixed byte or activation-count ceiling;
their client selection and source identities are revalidated after restart.

Use the Plugins page to import local folders or ZIP packages; see
[Plugin management](../plugin-management.md) for installation and version
selection. Imported packages are optional and cannot declare `required_on` or
replace a bundled plugin identity.

```text
bubblefighter.fantasy.font-weight/
  plugin.toml
  README.md            optional
  icon.png             optional, shown by the launcher
  font-weight.dll      only for kind = "native"
```

## Complete example

```toml
schema = 1

[plugin]
id = "bubblefighter.fantasy.font-weight"
version = "1.0.0"
game = "bubblefighter"
kind = "patch"                 # "patch" or "native"
runtime = ">=3.0.0"            # FtHook versions this plugin works with
authors = ["Fantasy Tale Studio"]
license = "MIT"

[plugin.name]
en = "Uniform font weight"
"zh-CN" = "统一字重"

[plugin.description]
en = "Keeps the weight the UI asks for instead of forcing bold for one language group."
"zh-CN" = "保持界面要求的字重，不再对某一语言组强制使用粗体。"

[compatibility]
builds = ["*"]                 # build ids, "branch:<name>" or "*"
exclude = []                   # never offered on these builds

[[options]]
key = "enabled_weight"
type = "int"                   # bool | int | string | enum
default = 400
min = 100
max = 900
[options.label]
en = "Weight"
"zh-CN" = "字重"

[[patches]]
id = "language-group-gate"
locate = { pattern = "83 3D ?? ?? ?? ?? 02 75 03 89 ?? 3C", offset = 7, match = "unique" }
expect = "75 03"
write = "90 90"
```

## `[plugin]`

| Key | Required | Meaning |
| --- | --- | --- |
| `id` | yes | Globally unique, lowercase `a-z0-9.-`, prefixed by the game id. Plugins shipped by this project use `bubblefighter.fantasy.`; others use `bubblefighter.<publisher>.`. |
| `version` | yes | SemVer 2.0. Stores compare and update by it. |
| `game` | yes | Game id from `game.toml`. |
| `kind` | yes | `patch`: only declarative patches. `native`: also loads `[native].library`. |
| `runtime` | no | SemVer range of FtHook versions (`>=3.0.0`, `>=3.1.0 <4.0.0`). |
| `name`, `description` | yes / no | Localized tables. `en` is mandatory; missing languages fall back to `en`. |
| `authors`, `license`, `homepage` | no | Shown by the launcher and stores. |
| `diagnostics` | no | Defaults to `false`. An enabled compatible selection with this capability exposes the client's diagnostics action. |

## `[compatibility]`

Build selectors are build ids (`kr-1256`), branch selectors (`branch:cn`) or
`*`. A plugin is **available** on a build matched by `builds` and not matched
by `exclude`. On an unavailable build the launcher shows the plugin with a
warning and does not start it; the runtime independently refuses it.
The game/build `clientPolicy` `require` rule forces a plugin on. The native runtime
receives that decision through the session's `required` flag, not package metadata.

## Minimum launcher version

An optional `[plugin] minimum_launcher_version = "0.1.14"` declares the minimum
application SemVer needed to enable or resolve the package. The library can still
list an incompatible installed package and display its requirement. Attempting to
enable it opens the shared launcher update flow; launch resolution checks again.
Numeric SemVer and prerelease precedence apply, independently of the `runtime`
range. The native parser validates this field's syntax, but application version
enforcement belongs to the launcher, not the in-process runtime. Older strict
manifest readers cannot load packages using the new field; do not rewrite an
already published immutable package to add it.

## `[[options]]`

Typed values the launcher renders as a form and writes into `session.toml`.
`type` is `bool`, `int` (`min`, `max`), `string` (`max_length`) or `enum`
(`values = ["a", "b"]`). Every option has a `default` and a localized `label`;
`description` is optional. Patches and native plugins read option values; a
value outside its declared range is rejected before anything is applied.

## `[[patches]]`

| Key | Meaning |
| --- | --- |
| `id` | Patch id, unique within one build. Several entries may share an id when their `builds` do not overlap; they are per-build variants of one patch. |
| `builds` | Optional selector list narrowing the plugin's builds for this entry. |
| `when` | Optional name of a `bool` option; the patch applies only when it is true. |
| `client_languages` | Optional distinct game-language tags, matched without case against `session.client_language`. Omit or use an empty list for every game language. Requires FtHook 3.7.2. |
| `locate` | Where the patch goes (below). |
| `expect` | Hex bytes that must be at the site before writing (`??` wildcards allowed). |
| `write` | Hex bytes to write. Same length as `expect`. |
| `timing` | `unpacked` (default): retry until the site appears or `deadline_ms` passes (default 60000). `immediate`: try once when the plugin loads. |

### `locate`

Exactly one of:

* `pattern = "AA BB ?? CC"`: searched in the executable sections of the main
  module. `match = "unique"` (default) fails when the pattern occurs more than
  once; `match = "first"` takes the lowest address.
* `rva = 0x5F3A10`: a fixed RVA; only allowed when `builds` names exactly one
  build.
* `locator = "fthook.config-md5-verdict"`: a named locator provided by the
  runtime or by a native plugin, for sites that need code to find.

`offset` (default 0) is added to the located address.

### Apply rules

Language filtering uses the selected **game language**, never the launcher interface
language. A nonempty `client_languages` list skips a patch when the session language
is absent or does not match. It combines with `builds` and `when`; it does not
change resource deployment or the game's language selection. Declare a compatible
runtime range when using this field.

1. All patches of one plugin that apply to the current build are located and
   checked against `expect` first. If any fails, **nothing** of that plugin is
   written and the result is `signature-mismatch`.
2. A site already holding `write` counts as installed (`already-present`).
3. Writes stay within one page, are read back, flush the instruction cache and
   restore the original protection; a failed write is rolled back.
4. The runtime writes one line per plugin: `installed`, `already-present`,
   `skipped-option`, `unavailable-build`, `signature-mismatch`, `timeout` or
   `failed` or `rollback-failed`, with the reason.
5. Patch ranges from different plugins may not overlap. The runtime rejects the
   later plugin before writing it and names both plugin and patch ids in the
   failure detail. The earlier successful plugin remains installed; reversing
   their order can change which plugin is rejected, but cannot silently overwrite it.
6. Before writing, the runtime records and validates all original bytes, then
   rechecks each exact snapshot even where `expect` uses wildcards. A changed
   byte rejects the write; rollback restores only writes made by this attempt,
   leaving sites that were already patched untouched.
7. If journal or low-level byte/cache/protection restoration is incomplete, the plugin reports `rollback-failed`.
   The session stops loading later plugins and fails readiness with
   `patch-restoration`, even when the affected plugin is optional. Its native
   library remains retained without publishing its locators.
   IAT recovery only undoes an exchange made by this attempt; already-present
   replacements remain installed if protection restoration fails.

Before shipping a pattern, check it on every build it claims with
`tools/verify-plugin` (unique match, expected bytes, unchanged branch target
after rewriting a jump).

Supply at least one `--image`. Exit code `0` requires a checked patch and no
remaining runtime checks; `1` means a byte/pattern failure; `2` means incomplete
verification or invalid command usage. Unavailable builds, native-only plugins
and named locators do not prove offline acceptance. A mixed pattern/locator
plugin still requires in-game verification even if its pattern checks pass.

## `[native]` (kind = "native")

```toml
[native]
library = "shop-ui.dll"        # x86 DLL in the plugin folder
entry = "fthook_plugin_main"   # optional, this is the default
```

The DLL exports `entry` with the signature in
`native/runtime/include/fthook/plugin_abi.h`. It receives the host API
(logging, pattern search, verified patching, option lookup, build id, locator
registration) and returns a result code. Declarative `[[patches]]` of the same
plugin are applied after the entry returns successfully, so a native plugin can
register locators its own patches use.

Native loading requires FtHook 3.1 or later. See [native-abi.md](native-abi.md)
for ABI versioning, entry/cleanup ordering, bounds and process lifetime.
Sessions are limited to 64 plugins and manifests to 256 declarative patches.

`[plugin] update_source` binds a managed package source. Latest-version requirements
belong to the game/build `clientPolicy` `latest` rule; see [client policy](../client-policy.md).
The native reader rejects `required_on`, `warn_on` and `require_latest` and performs no network update or hot reload.
