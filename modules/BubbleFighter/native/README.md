# Native integrations

## Auto Dash

Build the standalone plugin from a Visual Studio developer terminal with the
v145 C++ toolset and Windows SDK:

```powershell
powershell -ExecutionPolicy Bypass -File tools/verify-native-sdk.ps1
msbuild native/auto-dash/auto_dash.vcxproj -p:Configuration=Release -p:Platform=Win32
msbuild native/tests/auto_dash_tests.vcxproj -p:Configuration=Release -p:Platform=Win32
artifacts/tests/auto_dash_tests.exe
```

The output is `artifacts/auto-dash/auto_dash.dll`. The project consumes the
checked-in FtHook 3.3.3 SDK ABI export and does not link the launcher runtime.
The SDK inventory records the exact input hashes. `toml++` v3.4.0 is a pinned,
single-header dependency under `third_party/tomlplusplus`; its MIT notice is
preserved beside the source.

The plugin keeps its store identity and options. It reads the game's selected
build from the launcher host and uses only the client-specific configuration
installed beside its manifest. This repository migration does not publish or
replace any store package.

## Resolution Extension

The independent `resolution-extension/resolution_extension.vcxproj` builds an
optional kr-812 preview against the same pinned public SDK, without a runtime
static-library dependency. Build its tests through
`tests/resolution_extension_tests.vcxproj`. The technical evidence, build/package
commands and pending real-client checks are in
[Resolution Extension](../docs/hook/resolution-extension.md).
