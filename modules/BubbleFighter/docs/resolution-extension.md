# Resolution Extension

[Simplified Chinese](resolution-extension.zh.md)

The optional **Resolution Extension** plugin adds display modes to the original
game menu. Version 0.2.6 includes **kr-812, cn-404, cn-406, cn-706, cn-717,
cn-735, id-400 and id-1809**. The maintainer confirmed the initial kr-812 extension
in the game. The new settings and seven additional ports have static and synthetic
validation; their real-client display acceptance remains pending.

CN2011 (cn-279) and CN2012 (cn-324) are excluded because they have no native
resolution switching. kr-1256 already has a dynamic list. The later-generation
cn-774 and tw-454 are not declared compatible with this fixed-table strategy.
A plugin descriptor does not enable an otherwise disabled client profile.

## Install and configure

Install the optional package from the store, then enable it for a compatible
client. Launcher 0.1.33 or later is required for its package-declared settings page.
A local package can also be imported through **Plugins > Import plugin**.
The plugin uses the existing public native ABI and has been validated with
launcher runtime 3.7.0.

Open the plugin's **Settings** to maintain additional resolutions:

1. Enter a width and height in pixels and choose **Add** or press Enter.
2. Remove a custom entry with its delete button. Long lists scroll within the settings dialog.
3. Choose the launcher's **Save** to keep changes or **Cancel** to discard them.
4. Launch the game again and choose a resolution in its original display settings.

Only the additional list is editable. Original and automatically discovered
choices remain intact, and duplicate dimensions appear only once. An explicit
custom entry may add an ultrawide or intermediate size that the driver does not
enumerate. A menu-only adapter admits explicitly configured non-stock sizes that
fit the active monitor and are at least as large as one original candidate.
Other candidates retain the native selected-adapter check. Adding a size does not
create a driver display mode or bypass device initialization checks.

The list supports up to 256 entries within 4096 UTF-8 bytes. The stored option
accepts positive whole-number pairs such as `1920x1080;2560x1440`, separated
by semicolons, commas or newlines. The custom page provides width/height controls.

## Display behavior

Custom settings and hardware modes are read at game startup. Restart the game
after changing the list or connected displays. The automatic list preserves the
modern client's dimension and aspect-ratio policy; explicit custom entries can
supplement it. Actual choices depend on the graphics driver and the game's own
hardware checks, except for explicitly configured menu candidates described above.

The original settings and save flow remain active. kr-812 displays its original
restart-required message and applies a selected size after restarting. This
plugin does not add live switching where the client requires a restart.

CN2013 (cn-406) additionally expands its main-stage canvas when an extended
resolution needs more room than the original 1600x900 layout. The game keeps
its own centered controls, camera and scaled UI coordinates. The native menu's
800x600, 1024x768, 1366x768 and 1600x900 keep their original layout, including
when UI scaling is enabled. Other original table candidates are also preserved.
This canvas repair is enabled only for cn-406; other builds retain their existing
scene behavior. The maintainer confirmed this canvas repair in the game.

The custom-menu repair passed static checks against all eight unpacked client
images and 1,830 native fixture checks, including the thiscall stack and active
monitor path. Real-client menu acceptance of this revision remains pending.
The separately reported fullscreen-to-windowed behavior after restarting is still
under investigation and is not claimed fixed by this menu change.

Disable the plugin and restart to restore the original behavior. A saved size
outside the original table follows the game's original 1024x768 fallback. Keep
the plugin enabled when restarting into an extended size. Game files are not
replaced, and the plugin does not write `Option.ini` itself.

Build commands, per-build TOML details and acceptance checks are in
[the technical note](hook/resolution-extension.md).
