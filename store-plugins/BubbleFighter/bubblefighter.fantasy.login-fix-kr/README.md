# Korean client authentication component

[Chinese](README.zh.md)

Required, latest-only native plugin for KR2026 (`kr-1256`). No other build is
supported. New installations recommend it with a fixed checked selection; the
launcher refuses to start this build without the enabled component.

The component validates the image timestamp, size and native function signatures
from `native-login.toml`, including the unpacked code in a packed process. It
requires a managed launch session and a complete server ticket. Once the runtime
is ready and the native connection and GamePatch complete, it finishes startup
on the game UI thread. Before the login stage is created, the client's own
allocating wide-string assignment places the complete marked ticket in the
connection passport. The original login stage sends it through its own login
path; the component does not submit through the login-stage callback. No keyboard
simulation or extra executable is needed.

Startup completion is a separate UI-thread step after GamePatch, before the
login stage can exist. The bounded wait allows up to 120 seconds for cold
startup. Only the verified login destination builder is replaced with the
selected server endpoint; relay destinations keep their native path.

This does not change `login.ini`, executable preference, original messenger
forwarding, server validation or unrelated gameplay features. Game-security initialization and queue-pump entry points are verified and adapted
for managed startup without the separate game-security service. Native protected
value and device-information paths remain original. An unknown image or missing
signature is rejected. Timeout and submission stages are logged without ticket
contents. Passport placement does not establish server acceptance.

Version 0.1.2 obtains its ticket and endpoint through the runtime 3.4.0
authentication grant during plugin entry. It does not parse `session.toml`
independently, so session schema additions do not break an embedded parser.
Missing grants, invalid tickets and endpoint resolution failures are reported
without logging credentials. Existing published versions retain their contents.
