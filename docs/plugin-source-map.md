# Plugin source map

[简体中文](plugin-source-map.zh.md)

A `.vcxproj` is a build entry point. Its `ClCompile` items identify the actual C/C++ source files, which may be in another directory. Other plugins are implemented by TOML patches, managed resource providers, HTML/Vue components or resource-generation scripts and do not need a dedicated C++ library.

The current store contains 17 canonical plugin identities and seven retained former IDs. All 17 current identities have a matching source definition at their latest published version in this snapshot. Former IDs resolve through the definitions' `aliases`; they are not seven additional implementations. This checks source coverage, not original-client acceptance or reproduction of asset-dependent package bytes.

Paths below are relative to the repository root. The `modules/BubbleFighter` directory contains ordinary source files, not a submodule.

| Plugin | Implementation entry points |
| --- | --- |
| Fantasy Tale Nirinsou Link | [communication services](../src/FantasyTale.Launcher.Communication/), [host companion bridge](../src/FantasyTale.Launcher.Host/Bridge/LauncherBackend.Companion.cs), [native messenger](../games/BubbleFighter/native/nmcogame/messenger.cpp) |
| Auto Dash | [standalone native implementation](../modules/BubbleFighter/native/auto-dash/auto_dash.cpp), [build-scoped patches](../modules/BubbleFighter/games/BubbleFighter/plugins/bubblefighter.fantasy.auto-dash/plugin.toml) |
| Character Models | [managed resource composition](../src/FantasyTale.Launcher.Games.BubbleFighter/CharacterModels/), [settings and definition](../modules/BubbleFighter/games/BubbleFighter/plugins/bubblefighter.fantasy.character-models/), [package generator](../modules/BubbleFighter/tools/package-character-models.py) |
| Diagnostics | [native recorder and dump sources](../games/BubbleFighter/native/crash-recorder/) |
| Chinese font support | [patch and deployment definition](../modules/BubbleFighter/games/BubbleFighter/plugins/bubblefighter.fantasy.font-support-cn/plugin.toml), [font merge](../modules/BubbleFighter/tools/merge-client-fonts.py), [outline weight generation](../modules/BubbleFighter/tools/embolden-client-font.py), [variant generation](../modules/BubbleFighter/tools/build-client-font-variants.py) |
| Chinese login fix | [native adapter](../modules/BubbleFighter/games/BubbleFighter/native/login-bridge/native_adapter.cpp), [plugin entry](../modules/BubbleFighter/games/BubbleFighter/native/login-bridge/dllmain.cpp), [definition and patches](../modules/BubbleFighter/games/BubbleFighter/plugins/bubblefighter.fantasy.login-fix-cn/plugin.toml) |
| Korean login fix | [native implementation](../games/BubbleFighter/native/kr-auth/kr_auth.cpp), [definition](../modules/BubbleFighter/games/BubbleFighter/plugins/bubblefighter.fantasy.login-fix-kr/plugin.toml) |
| ESC mid-match exit | [native input implementation](../modules/BubbleFighter/native/mid-match-exit/mid_match_exit.cpp), [exit flow](../modules/BubbleFighter/native/mid-match-exit/exit_flow.cpp), [manifest template](../modules/BubbleFighter/native/mid-match-exit/plugin.preview.toml), [patches](../modules/BubbleFighter/native/mid-match-exit/native-patches.toml), [packager](../modules/BubbleFighter/tools/package-mid-match-exit.py) |
| Native style components | [panel and definition](../modules/BubbleFighter/games/BubbleFighter/plugins/bubblefighter.fantasy.native-style-components/), [Vue components](../tools/game-ui/src/) |
| Native UI bridge | [native UI calls and service implementation](../games/BubbleFighter/native/game-controls/), [managed client](../src/FantasyTale.Launcher.Core/Hooks/GameNativeUiClient.cs) |
| File verification bypass | [TOML patches](../modules/BubbleFighter/games/BubbleFighter/plugins/bubblefighter.fantasy.no-file-verify/plugin.toml) |
| Numeric health display | [native implementation](../native/samples/bubblefighter.fantasy.numeric-health/numeric_health.cpp), [client descriptors](../native/samples/bubblefighter.fantasy.numeric-health/client_descriptors.cpp) |
| Overlay | [overlay application](../src/FantasyTale.Launcher.Overlay/), [native overlay API](../native/runtime/include/fthook/overlay_ui.h), [definition](../modules/BubbleFighter/games/BubbleFighter/plugins/bubblefighter.fantasy.overlay/plugin.toml) |
| Pirate expression repair | [resource definition](../modules/BubbleFighter/games/BubbleFighter/plugins/bubblefighter.fantasy.pirate-expression-fix/), [resource generator](../modules/BubbleFighter/tools/package-pirate-expression-fix.py) |
| Resolution Extension | [native implementation](../modules/BubbleFighter/native/resolution-extension/resolution_extension.cpp), [resolution model](../modules/BubbleFighter/native/resolution-extension/resolution_model.cpp), [settings and descriptors](../modules/BubbleFighter/games/BubbleFighter/plugins/bubblefighter.fantasy.resolution-extension/) |
| Scene styles | [resource definition and settings](../modules/BubbleFighter/games/BubbleFighter/plugins/bubblefighter.fantasy.scene-styles/), [resource generator](../modules/BubbleFighter/tools/package-scene-styles.py) |
| Shop unlock | [TOML patches](../modules/BubbleFighter/games/BubbleFighter/plugins/bubblefighter.fantasy.shop-unlock/plugin.toml) |

Shared [resource-edit providers](../src/FantasyTale.Launcher.Games.BubbleFighter/ResourceEdits/), [archive composition](../src/FantasyTale.Launcher.Games.BubbleFighter/Archives/) and the [native patch engine](../native/runtime/src/engine.cpp) execute the data-driven definitions. Game-owned fonts, models and scene textures remain external inputs. Their absence is intentional; the generators and configuration are included.

The retired Dao/Dizni classic models and Character Remaster packages are superseded by Character Models; the former font-weight packages are superseded by Chinese font support. Those implementations, their dedicated generators/tests, the retired security compatibility package, abandoned forward-jump experiments and the old ESC patch sample have been removed. The maintained ESC implementation above remains available. Store retirement records, ID aliases and conflict declarations remain necessary for users upgrading existing installations. The `gpk_stub` helper is still an input to Chinese login fix packaging and is retained.

`modules/BubbleFighter/native/cn-auth/cn_auth.vcxproj` points to the adapter under `games/BubbleFighter/native/login-bridge` inside that module. It is not an empty plugin. The `native/sdk/sample_locator.vcxproj` file is an export template; `tools/package-native-sdk.ps1` places it beside the source from `native/samples/sample.locator/sample.c`. Build the exported SDK sample, or the source-tree project under `native/samples/sample.locator`.

Use the root native solution for the integrated launcher build. Integration copies under the module consume the shared root runtime through their corrected relative imports and write to the module's separate `artifacts/native-integration` directory; the independent module projects use their pinned SDK. See [contributing](../CONTRIBUTING.md) and the [tool inventory](../tools/README.md).
