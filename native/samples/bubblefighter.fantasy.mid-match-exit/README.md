# Leave an ongoing match (experimental)

[Chinese](README.zh.md)

One optional plugin identity is used for every supported build. Currently only
kr-812 has an offline-verified implementation; other builds must be added as
build-scoped variants after their own consuming paths and bytes are checked.

Import this folder using the launcher's plugin import when retail testing is
authorized. It is deliberately not a bundled default plugin. Leave **Allow all
modes** off to preserve original rules. Enable it to remove the selected mode
and boss-presence restrictions while retaining the original relay confirmation
path. Ordinary confirmation requires relay. Initialization, context, connection and duplicate-dialog guards
remain. Special-mode and session-exit routing remain original.

Five expected-byte sites passed `tools/verify-plugin` against the unpacked
kr-812 image. The managed parser accepts the package and default options.
These checks do not prove that every game mode exits successfully: server
acceptance, actual key handling, Cancel, Confirm, room membership cleanup and
subsequent play still require retail validation. Do not advertise this sample
as a validated all-build or all-mode release.

Version 0.1.0 displayed the dialog in retail testing, but confirmation had no
effect on a connection without relay. Version 0.1.1 experimentally used the
original game leave path; the server received 506 and replied, but the client
subsequently crashed in the game-stage renderer. Version 0.1.2 removes that
fallback. Ordinary exit without relay remains unfinished.

Disabling **Allow all modes** applies no patch. Turning off a plugin after its
patches have loaded requires restarting the client; it does not undo live writes.
The plugin changes process memory only and does not replace client archives.
