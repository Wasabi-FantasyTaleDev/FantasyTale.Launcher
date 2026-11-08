# Forward Jump

This research package is currently unavailable. The isolated CN2011 packed-client run crashed before login after the patch was installed. Offline signature matching is not runtime acceptance.

[简体中文](README.zh.md)

This optional patch is for the CN2011 (`cn-279`) client. It keeps the native
Space action and its existing status, scene, cooldown and movement checks, but
removes the legacy gate that caused W + Space to fall through to the roll
transition. The process image is patched only in memory and restored when the
launch session ends.

The plugin is intentionally limited to CN2011 until another client has a
separate verified branch. It does not synthesize keyboard input, modify the
server protocol, or change the executable on disk.
