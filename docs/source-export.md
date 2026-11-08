# Reviewed source exports

[Chinese](source-export.zh.md)

A public source snapshot is a reviewed file list, not a copy of a developer checkout.
Keep local recovery archives, reference inputs, credentials, game assets and private
investigation notes outside the snapshot. Review the independent game module separately;
the launcher export never traverses its nested repository or copies its history.

Use `tools/export-reviewed-source.py` with a private JSON manifest. The manifest names
one full commit id and every permitted output file with its SHA-256. Paths not listed are
not exported. Review both the list and file contents before using it; the exporter is not
a secret scanner or a substitute for reviewing redistribution permissions.

```json
{
  "schema": 1,
  "revision": "<full reviewed commit id>",
  "files": [
    { "path": "LICENSE", "sha256": "<SHA-256 of the committed file>", "action": "copy" },
    { "path": "web/src/preview-levels.json", "sha256": "<SHA-256 of {} followed by LF>", "action": "empty-preview" }
  ]
}
```

```powershell
python tools/export-reviewed-source.py --repository . --manifest docs/private/reviewed-source.json --output artifacts/reviewed-source
```

The output must not already exist. The tool streams immutable Git blobs, verifies each
output hash and does not copy `.git`, ignored files, untracked files, links or submodules.
A failure can leave a partial directory for inspection; it must not be published or reused
as the output of another run. Uncommitted work is intentionally not exported. Do not put
private approval notes or the full manifest into a public snapshot by default.

Exclude game-derived level tables and images, private media and locally extracted fonts
and audio. Keep the optional `levels.toml` definitions so builds without those resources
can load. The preview catalog has one explicit transformation: `empty-preview` writes
exactly `{}` and LF, retaining the frontend import without shipping exported game data.
The numeric-health validator tests construct synthetic descriptor/header fixtures;
they do not establish rendering acceptance or permission to ship the original sprite.
Keep dependency licenses, notices and required corresponding source alongside their binaries.

Build and test the exported snapshot, not just the original checkout. Run the managed
Release build and suite, the native solution and tests, and `npm ci`, `npm test`,
`npm run type-check`, `npm run build` and `npm run build:game-ui` in `web/`.
Follow the native runtime preparation steps in the README before managed hook tests.
Only private resource integration tests may skip for absent assets; an explicit
`FT_GAME_UI_RESOURCES` root makes missing resources fail. Record every skipped integration
test and any artifact prerequisites. Source build success is not a complete portable
package or real-client acceptance result.

For a portable distribution, the updater verifier rejects linked paths, local state,
browser profiles, local configuration overrides, logs, dumps and PDBs before running its
detached helper. Normal publishing omits managed debug symbols. This filename gate does
not authenticate arbitrary file contents or grant permission to distribute media.
No export or verification command pushes, publishes or changes repository visibility.
