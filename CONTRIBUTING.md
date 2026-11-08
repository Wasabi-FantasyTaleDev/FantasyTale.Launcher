# Contributing

[简体中文](CONTRIBUTING.zh.md)

Use Windows with the .NET 10 SDK, Node.js, Python 3 and Visual Studio's C++ tools (v145 and the x86 build tools). The launcher targets Windows; in-process game plugins are x86 even when the launcher or Gateway runs on x64 or ARM64.

From a Visual Studio developer PowerShell, build the native runtime before managed tests that depend on it:

```powershell
msbuild native/FantasyTale.Native.sln -p:Configuration=Release -p:Platform=x86 -m
./tools/package-native.ps1
dotnet build FantasyTale.Launcher.slnx -c Release
dotnet test FantasyTale.Launcher.slnx -c Release
```

Run the native test executables produced under `native/out/Release`, including `fthook_tests.exe`, `nmcogame_tests.exe`, `native_tests.exe` and `dump_writer_tests.exe`. Warnings are treated as errors; do not suppress them to make a build pass.

Build and test the frontend separately:

```powershell
cd web
npm ci
npm test
npm run type-check
npm run build
npm run build:game-ui
```

Real-client, external-store and interactive elevation checks have additional prerequisites. Record their skips explicitly; a unit test result does not establish real-client acceptance. Do not connect local experiments to production accounts or databases.

`modules/BubbleFighter` contains the separately maintained Bubble Fighter source snapshot as ordinary source files. It has no nested Git metadata. Its standalone native SDK is pinned; some integration projects retain the original shared-source layout requirements. The root solution remains the entry point for the launcher integration. Packaging plugins that replace game resources requires legally obtained local originals; those inputs are not included here.

Font tools generate resource packages from user-supplied fonts. Neither original nor generated game fonts are included in the source snapshot. Keep third-party licenses and corresponding source with redistributed dependencies. See [NOTICE.md](NOTICE.md) and [source exports](docs/source-export.md).

Use a topic branch and a pull request. Explain the problem, resulting behavior, validation and remaining limitations. Preserve saved plugin settings, explicit installation choices and runtime recovery. Changes to packet layouts or memory patches need evidence from the relevant client's consumer and must name the client build with any function address.
