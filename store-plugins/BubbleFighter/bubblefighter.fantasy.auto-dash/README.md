# Auto Dash

[Chinese](README.zh.md)

One optional plugin for KR2015 (`kr-812`), CN2011-CN2016 builds, ID2014
ID2015 and Taiwan TW454. Press the configured forward key once to request the client's
native running transition. Remapped controls and the original movement
restrictions remain supported. KR1256 is excluded because it already has this
function. Other modern movement paths remain pending separate verification.

TW454 uses an independently verified input branch. It skips only the empty or
expired forward double-press timer and retains the client's running eligibility
checks and running-release behavior. Its manifest declares TW454 availability
and its native sidecar selects the existing declarative patch implementation.

CN2011 (`cn-279`) uses the native forward timer implementation accepted in
client testing. It is merged into this package from the former preview and does
not rewrite executable instructions. The DLL validates the client timestamp,
timer function and actor before calling the original timer on the game window
thread. Other supported builds retain their individual, verified branch patches;
the DLL performs no timer work on those builds. There is no synthetic keyboard
input, resource replacement or server change.

Enable the plugin before starting the game. **Run with one press** defaults to
on. Disable the option or the plugin and restart to restore normal double-press
behavior. The executable on disk is never changed. Both implementations are
selected by build data under this single plugin identity, not separate store
plugins. No Forward Jump behavior is included.

Branch patches require a unique executable-memory signature and matching original
bytes. Packed clients wait for unpacking through the runtime. An incompatible
image is rejected instead of receiving an unchecked write. CN2011 acceptance does
not imply that every other supported build has completed gameplay acceptance.
