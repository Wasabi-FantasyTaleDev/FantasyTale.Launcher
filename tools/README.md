# Development and packaging tools

[简体中文](README.zh.md) · [Plugin source map](../docs/plugin-source-map.md)

The public tree retains tools that build, package, validate or reproduce supported functionality. Local-only comparison scripts are ignored and remain in the developer's private workspace.

Check explicit native source inputs with `python tools/verify-native-project-inputs.py --template-project native/sdk/sample_locator.vcxproj`. The SDK project is intentionally an export template, assembled with its sample sources by `package-native-sdk.ps1`. The input check does not replace compiler or MSBuild evaluation.

| Area | Retained purpose |
| --- | --- |
| `package-native.ps1`, `package-native-sdk.ps1` | Stage native binaries and export the supported plugin SDK from explicit source inputs. |
| `package-launcher.ps1`, `package-gateway.py`, `copy-runtime-notices.ps1`, `create-launcher-archive.py` | Produce distributions with pinned dependencies, licenses, architecture checks and recovery-compatible indexes. |
| `publish-launcher/`, `publish-store/` | Reusable release and store publication, importable plugin ZIP generation and catalog lifecycle operations. Secrets come from external configuration. |
| `verify-*.py`, `verify-plugin/`, `verify-launcher-*`, `webview-policy-test/` | Distribution validation, plugin safety checks, actual upgrade/fallback acceptance and browser boundary regression tests. |
| `inspect-client/`, `native-ui/` | Reusable client profile identification and native descriptor validation for adding or reviewing supported builds. These tools do not execute a client. |
| `prepare-hook-test/`, `web-preview/` | Isolated native test-kit preparation and frontend previews. |
| `game-ui/src/` and its build/resource preparation scripts | Rebuild the shared Vue game UI library and prepare optional user-supplied UI assets. `web/package.json` uses these sources and tests. |
| `import-level-assets.py`, `export-reviewed-source.py` | Optional local asset imports and explicit allowlist-based source exports. Neither publishes game assets automatically. |
| `modules/BubbleFighter/tools/` | Plugin package generators, font merging/emboldening, model texture fixes, archive materialization, native SDK verification and build-scoped patch checks. Their configuration and automated regression tests remain alongside them. |

The old `game-ui/extract.py` and `extract-v3.py` depended on an archive-tool project absent from this repository. The cross-client sound-bank inspector and the character/pirate verification executables depended on investigation-specific input inventories and fixed experiment expectations. They are local-only and removed from tracking, with explicit `.gitignore` rules. The functioning package generators and runtime implementations remain available.

Files named `scene-style-evidence.toml` and `character-models.toml` are required package-generation configuration despite their historical names. Removing them would prevent rebuilding the resource plugins. Font sources remain external; merge scripts, variant rules and tests are included.
