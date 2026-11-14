# Self-hosted store reference

[简体中文](self-hosted-store.zh.md)

## One catalog, separate content

A store exposes one UTF-8 TOML catalog, normally `FantasyTale/source.toml`.
Schema two combines client packages, plugin packages and client profiles in this
document. Browsing does not require a separate endpoint for each package or
profile. Release file manifests and payloads remain separate: their paths and
SHA-256 digests are referenced by the catalog and read when needed.

This is a distribution contract, not a game-account or login-ticket API. Adding
a store does not authorize it to update the launcher itself. Launcher update
sources are configured separately; see [Launcher updates](launcher-updates.md).

## Follow server releases from GitHub

A schema-two catalog can subscribe to the latest published stable GitHub release.
Keep this row on your existing HTTP or AList source; future server releases need
only be published on GitHub with the same archive filename:

```toml
[[github_releases]]
id = "community.server"
game = "example-game"
builds = ["example-1"]
repository = "owner/server"
asset = "server-win-x64.zip"
executable = "Server.Host.exe"
name = { en = "Community server" }
publisher = "Community"
```

Refresh resolves the release tag, version, size and GitHub SHA-256 digest without
downloading the archive. Drafts and prereleases are excluded. Explicit installation
pins that exact tag and digest, downloads from GitHub and verifies the archive
before extraction. Paused and restarted tasks keep their original release even
when a newer version appears. The parent source still owns the package and must
remain enabled with the same endpoint.

Do not specify a wrapper directory. Installation locates `executable` within the
verified extracted file index and installs its containing root. A client archive
uses the executable and aliases from its build definition in the same way. Nested
wrapper directories may change names without requiring catalog edits. Files outside
the selected root are excluded; missing or multiple candidate roots fail before
installation writes any game files.

Successful resolutions persist with the catalog. A network failure or API rate
limit retains a matching validated release snapshot and reports a failed refresh;
without one, only that subscription is omitted and other catalog resources remain
available. Invalid release metadata is rejected and retains the previous full
catalog. The publisher may include a validated `github_asset` package snapshot
beside its subscription for first-run fallback. The publication uploader does not
upload GitHub payloads or generated manifests to AList.

When GitHub returns a quota reset time or `Retry-After` on a 403/429 response,
the running Launcher shares that deadline across its store sources and waits
until it expires before querying GitHub again. Refreshing still retrieves the
parent catalog and its ordinary resources. This cooldown does not persist across
Launcher restarts, and it cannot restore an exhausted anonymous API quota.

To verify a public AList subscription through the real download queue, provide
these process-local settings and run the opt-in test:

```powershell
$env:FT_TEST_GITHUB_LIVE_ALIST_BASE = 'https://archive.example.org/'
$env:FT_TEST_GITHUB_LIVE_CATALOG = 'FantasyTale/source.toml'
$env:FT_TEST_GITHUB_LIVE_PACKAGE = 'community.server'
$env:FT_TEST_GITHUB_LIVE_REFERENCE_ARCHIVE = 'D:/Store/server-win-x64.zip'
dotnet test tests/FantasyTale.Launcher.Core.Tests -c Release --filter FullyQualifiedName~PublicGitHubStoreTests
```

The test uses no administrator credentials, does not seed the download cache and
does not start the server. It verifies that browsing fetches no archive, explicit
installation downloads from GitHub, every installed payload matches the separately
provided archive, and the completed task restores without network traffic. The
reference archive must match the selected release's size and SHA-256. Test output
distinguishes successful release refresh from a network/quota failure that used a
validated catalog pin; successful installation alone does not prove fresh metadata.
The isolated installation is removed at the end of the test.

## Hosting and requests

Choose one transport in Manage sources:

| Kind | Base URL | Catalog request |
| --- | --- | --- |
| `http` | `https://files.example.org/` | `GET https://files.example.org/FantasyTale/source.toml` |
| `alist` | `https://archive.example.org/` | `POST /api/fs/get`, followed by GET of the resolved URL |

The catalog path is configurable. All catalog and manifest paths are relative to
the source root, not to the directory containing the referring document. HTTP
hosting can be an ordinary static server: return the actual TOML bytes with a
successful HTTP response, not an HTML preview or JSON wrapper. HEAD is not required.
Serve payloads with correct lengths; HTTP Range support enables download resume.

For AList, the resolver sends:

```json
{"path":"/FantasyTale/source.toml","password":""}
```

A successful resolution has this shape (the size must be the actual byte count):

```json
{"code":200,"message":"success","data":{"is_dir":false,"size":1234,"raw_url":"https://storage.example.org/source.toml","sign":""}}
```

If `raw_url` is absent, the adapter uses `/d/<path>?sign=<sign>`. The resolved
response must contain the original file bytes. Temporary storage URLs are resolved
as needed, not used as permanent package identities. The desktop source editor currently configures public
sources; do not require users to enter administrator credentials to browse.

## Unified catalog fields

```toml
schema = 2

[catalog]
id = "community"
name = { en = "Community store", zh-CN = "社区商店" }
```

Append `[[packages]]` and `[[profiles]]` rows to this same document. Empty arrays
may be omitted. Unknown keys are rejected. Catalogs and file indexes have no fixed
application size or row-count ceiling. Schema two supports profiles and inline
plugin definitions; use it when composing the unified catalog.

| Package field | Meaning |
| --- | --- |
| `id`, `kind`, `game` | Stable package identity, kind and game ID; use `client` or `plugin` for these examples |
| `builds` | Exactly one build for a client; explicit supported builds for plugins |
| `version` | Optional client update label; SemVer for plugins |
| `name`, `description` | Localized plain text; names include English; description is optional |
| `publisher` | Required display label, not verified identity |
| `size` | Total download bytes in the release manifest |
| `manifest`, `manifest_sha256` | Relative manifest path and SHA-256 of its exact bytes |
| `update_mode` | Client `files` for a current file snapshot, or `release` for versioned releases |
| `revision` | Release ordering value; use unique increasing positive values for ordered releases |
| `authors`, `uploader` | Optional author list and uploader display name; omit uploader when empty |
| `plugin_definition` | Schema-two inline `plugin.toml` text for plugin metadata; required for a package that declares `requires`, so its requirements are installed from the same catalog before the download |
| `minimum_launcher_version` | Optional schema-two SemVer requirement; must agree with inline plugin metadata |

Package identity is scoped to its configured source. A different source cannot
replace an installed package merely by copying its ID. Paths cannot contain
URLs, traversal, backslashes, percent escapes, queries or fragments. Use the
publisher to generate real sizes and hashes; example digests are never valid
release metadata.

## Per-client automatic checks

The add-client dialog enables **Check integrity** by default. The client library's
edit action exposes the same saved preference. Turning it off skips automatic
client update checks so local patches are not replaced by an automatic update.
Manual checks remain available. This preference never skips SHA-256 verification
of downloaded archives or files before installation.

## Adding clients

Prepare a clean distributable client folder. Copy and adapt
`tools/publish-store/publish.example.toml`, keeping output outside the inputs:

```toml
[store]
id = "community"
name = "Community store"
output = "./public"
storage_gib = 200

[[clients]]
game = "bubblefighter"
build = "cn-279"
name = "China 2011"
directory = "./clients/cn-279"
exclude = ["logs/*", "*.log", "*.dmp"]
```

```powershell
python tools/publish-store/publish_store.py --config D:/Store/publish.toml
```

This generates the catalog, hashed manifests and content-addressed files under
the output root. `--watch 30` rescans periodically; unchanged content is reused.
`--full-check` bypasses the publisher's input metadata cache. The client build
ID must correspond to a supported game module and a valid build profile: a
catalog row alone does not add a new game implementation.

### Registering archives already uploaded to AList

When complete archives were uploaded separately, use `register_archives.py`
instead of uploading them again. Prepare a schema-two candidate catalog that
preserves every published package row and adds new client archive packages.
Keep its referenced archive manifests under an input directory, and save the
current public catalog as the concurrency baseline. Configure local originals:

```toml
[[archives]]
source_path = "FantasyTale/client/cn-279/client.7z"
local_path = "D:/Distributions/client.7z"
```

```powershell
python tools/publish-store/register_archives.py --input D:/Store/public --candidate D:/Store/candidate.toml --baseline D:/Store/current.toml --references D:/Store/references.toml --base-url https://archive.example.org/ --receipt D:/Store/preflight.json --validator tools/publish-launcher/bin/Release/net10.0/FantasyTale.Launcher.Publish.dll --check-only
```

Build the validator first with `dotnet build tools/publish-launcher -c Release`.
Authentication comes only from `FT_ALIST_USERNAME` and `FT_ALIST_PASSWORD` in the
process environment. Remove `--check-only` to publish after checking the result.
The tool computes local SHA-256 and MD5 together, matches the storage provider's
MD5 and size, and checks anonymous raw download access with a one-byte request.
It requires provider MD5 metadata; it never calls this a remote SHA-256 proof.
Launcher installations still verify the entire downloaded archive against its
manifest SHA-256. Registration uploads only new immutable manifests and then
the catalog, rechecking archive metadata and the catalog baseline before that
last write. It cannot replace or remove an existing package. The credential-free
receipt records which evidence was checked. Publish a compatible Launcher before
activating profiles that use a new profile contract.

## Adding and updating profiles

A profile row contains `game`, `build`, a positive `revision`, and `definition`
holding the complete schema-one build TOML as a string. Its identity must match
the embedded `[build]` identity. Increment the revision whenever its definition
changes. Do not regress revisions or change a published revision in place.

Maintain build definitions as ordinary files rather than hand-escaping them into
the catalog. Start from a supported `games/<game>/builds/<build>.toml` and configure:

```toml
[[profiles]]
definition = "./profiles/cn-279.toml"
revision = 1
```

In the build definition, `[build]` supplies identity, executable, display names,
branch and protocol version. Optional `region` is a two-letter uppercase code;
`protocol_version` is a required nonnegative integer identifying the protocol
(for example 400). `[launch]` supplies the module's login mode,
arguments, login policy and elevation requirement. `[plugins]` uses `required`,
`recommended` and `default` arrays of plugin IDs. Required plugins are not the
same as recommendations. Keep the executable fingerprint and module-specific
launch behavior appropriate to the actual build; do not guess login modes.

Use region and wire version for client IDs, such as `id-400`. Distinct releases
of that wire dialect use `id-400-2`, `id-400-3` and so on, all declaring
`protocol_version = 400`. Optional `[build] release_version` and `patch_version`
describe the release; they are not substitutes for the wire identity. Installed
version metadata can refine those labels at launch without changing the profile ID.

`[build] build_date` is optional and uses a quoted invariant timestamp such as
`build_date = "2011-01-01 00:00:00"`. It replaces `year`. Use an investigated
release, signing or compilation date; omit it when unknown. The parser does not
apply the user's locale, calendar or time zone. Existing bundled signing dates
retain their previously displayed Korea time (UTC+09:00); the original UTC
signature evidence stays in the fingerprint catalog. The date is descriptive
and never selects a protocol or makes a client launchable.

Client language configuration has two parts under `[launch]`:

```toml
[launch]
login = "nmco"
native_language = "id"
additional_languages = { "zh-CN" = "example.translation.zh-cn" }
```

`native_language` identifies the unmodified distribution. `additional_languages`
maps extra language codes to file-plugin identities. Omit the map or use `{}`
when no translation is offered. The example package identity is a placeholder;
publish a real compatible package before advertising it. Each additional
language needs its own plugin identity, distinct from the native language.
The plugin must use restorable file replacements so changing languages does
not permanently overwrite the native installation. Existing file-plugin hash
verification, recovery and restore-on-exit rules apply. Only the selected
language's replacement files participate in a launch, even if several language
packs are installed and enabled. Other selected plugins retain their behavior.

Home shows one combined language list, including supported translations that
are not installed. Selecting an unavailable translation opens the store and
selects its matching package. A translation must be installed and enabled for
that client before launch. An explicit server language list restricts profile
choices; it cannot supply a missing translation implementation. Unfinished
translations are not advertised. The retired `planned_languages` and build-level
`client_languages` fields are rejected rather than treated as working support.

Compose packages and profiles into one final file:

```powershell
python tools/publish-store/compose_catalog.py --catalog D:/Store/public/FantasyTale/source.toml --config D:/Store/profiles.toml --output D:/Store/unified-source.toml
dotnet run --project tools/publish-launcher -c Release -- --validate-catalog D:/Store/unified-source.toml
```

The output must be a new path. After validation, publish it at the configured
catalog path, preserving all referenced objects. Composition prepares the current
schema-two catalog locally; upload it only after validation.

## Adding plugins

Build a clean plugin distribution with root `plugin.toml` and its runtime files.
The definition declares `[plugin]` identity, game, SemVer version, localized
names and `authors`, plus `[compatibility]` supported builds and the plugin's
implementation/options. A single plugin identity may support many builds; do
not create one package per installed client. See [Plugin format](hook/plugin-format.md)
for authoring details and [Store sources](store-sources.md) for manifest formats.

`tools/publish-store/publish_plugins.py` generates file manifests and merges new
plugin releases with an existing remote catalog. Its command **publishes**:

```powershell
# Supply FT_ALIST_USERNAME and FT_ALIST_PASSWORD only in the process environment.
python tools/publish-store/publish_plugins.py --package D:/Store/plugin-package --output D:/Store/plugin-output --base-url https://archive.example.org/
```

Use a new SemVer for changed bytes. Do not overwrite a published plugin version.
The tool retains unrelated rows; preserve existing profiles during subsequent
catalog composition. For inline schema-two metadata, set `plugin_definition` to
the exact plugin definition text; ID, game and version must match the package.
If specifying the minimum launcher version in both places, they must agree.
Downloading metadata never executes plugin code.

## Publication and verification

Publish content first, then immutable manifests, then replace the single catalog
last. Compute file hashes locally and publish them in the manifests; consumers
compare downloaded content against those hashes. Do not serve a new catalog
while its referenced files are still uploading. Keep old objects available for
downloads that already pinned an earlier manifest. Avoid concurrent writers to
the same catalog, and retain unrelated package/profile rows when composing it.

For HTTP, copy the generated tree beneath the web root and atomically replace
the catalog last. For AList, use `tools/publish-store/upload_alist.py`; its flags,
verification modes and concurrency controls are documented in
[Store sources](store-sources.md). AList upload requires a writable storage
driver; successful administrator login alone does not prove upload support.

Check from an anonymous client: fetch the catalog, validate it, fetch a referenced
manifest, compare its exact-byte SHA-256, and install a package into a separate
test directory. Confirm profile identity, required/recommended plugin lists and
multi-build plugin compatibility. Hashes establish integrity against the selected
catalog, not publisher authenticity. Do not publish account data, credentials,
session tickets, local overrides, diagnostics or private research in packages.
