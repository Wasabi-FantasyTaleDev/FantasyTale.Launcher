# Testing the numeric-health candidate on another PC

[Chinese](x64-plugin-testing.zh.md)

Use the official portable Launcher package for Windows x64. Check the running
version in About after any update; a detected release is not an installed release.
Extract the entire ZIP to a writable directory, such as `D:\FantasyTaleLauncher`,
and run `FantasyTale.Launcher.exe` from that directory. Do not run it inside the
ZIP or copy only the executable. The package includes the .NET runtime; a .NET
SDK is not required. If startup reports a missing WebView2 runtime, install the
Microsoft Edge WebView2 Evergreen Runtime for x64 and open Launcher again.

Launcher, Photino, WebView2Loader, the bundled runtime and the detached updater
are x64. The injected game Hook and numeric-health DLL are x86 because the
supported game client is 32-bit. The package contains no game client, saved
account credentials or signed-in session.

## Set up the client

1. Copy the entire Korean 2015 client directory from the original computer.
   The current numeric-health candidate supports build `kr-812`; another build
   is not a valid comparison. An executable by itself is insufficient.
2. Select Bubble Fighter in Launcher. Open Clients and add a client using the
   copied directory as its installation path. Let Launcher identify the build.
3. Select Bubble Fighter Fantasy as the server and sign in using your account
   on that server. Accounts belong to the selected server.
4. Open the plugin store using the built-in Fantasy Tale source. Select
   Numeric Health `0.2.9` for the added client. Releases are newest first.
   Verify the selected version instead of assuming an older installed `0.2.7`
   was upgraded automatically.
5. On the Plugins page, explicitly enable Numeric Health for that client.
   Use yellow font, native placement, show percent on and raw health off.
   Only one version of this plugin may be selected for the client; changing
   versions transfers its enabled state and compatible options.
6. Start the game and enter a match. Inspect the HP number above the gauge,
   then reduce HP to check whether the symbol follows changing digit widths.
   Settings changes require a new game process.

## Report the result

Record whether the HP number appears, whether the percent appears, their
positions and any delay or change after a window resize. A screenshot and the
numeric-health plugin log from that run are more useful than a startup-only
screenshot. Optional timing diagnostics record bounded submission checkpoints;
they do not establish visible pixels.

The maintainer confirmed visible percent artwork and native placement after
selecting the newer plugin. The earlier comparison had used `0.2.7`; do not
attribute that failure to ARM64 alone. For a new comparison, match plugin version,
options and client files on both PCs. Only `kr-812` is currently advertised; other
legacy builds remain research-only. Canonical and original preview Launcher
update entries are retained with identical schema-one indexes.
