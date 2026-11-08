# Native Chinese authentication component

Chinese-specific behavior lives in login_bridge.dll. The existing Messenger host
retains exclusive credential authorization and one-delivery enforcement. The
component wraps its known provider objects and copies the validated marked ticket
through native string routines before forwarding the synchronous success callback.

One adapter reads per-build fingerprints, offsets and signatures from
native-passports.toml for CN2011, CN2012, CN2013, audited CN2014 images, CN2015 and CN2019.
CN2011 and CN2012 use native COW construction, assignment and destruction; later builds use
their own string assignment. The former CN2012 chooser continuation Hook is removed.
No native login packet or routing is rebuilt. CN2019 uses an explicitly selected
native character-span assignment contract for its connection passport.

For CN2011/CN2012's empty promoted connection, a new native provider request may receive
one cached, already authorized record after selection and connection checks.
The native callback still sends login. This adds no executable or vtable patch,
cannot replay on an authenticated connection, and clears the cache when the
component initializes or terminates. CN2013 and CN2015 additionally require an
exact authorized-ticket match for nonempty passports. Their original region
and feature predicates distinguish a skipped sender from a selected native
sender path: only the skipped path receives a callback. A redundant provider
request after the sender path is consumed without another callback or host
request. This does not establish server receipt or authentication success.

CN2011 optionally consumes the launch metadata capability
bubblefighter.cn-center-names.v2. Its TOML payload has schema=1 and centers rows
with positive native id and optional full directory name. Earlier components
ignore this capability. Missing names retain original titles.

A window timer runs on the client's UI thread. The adapter validates the active
modal chooser, its button vector and the corresponding native Center identities,
then copies names into each button's caption with native COW string routines.
Literal percent signs are doubled in caption storage because the original draw
function consumes that field as a format string; rendered names remain literal.
CN2011's native drawing centers each title in a fixed 70-pixel rectangle, which
clips long titles at the button's left edge. Named buttons use an instance-local
copy of the native virtual table with only its drawing entry replaced. The adapter
calls the original drawing routine on a borrowed view for the background and font,
then uses the native text renderer with left alignment. Original executable bytes,
original virtual tables, unnamed buttons, selection, capacity and connection fields
remain unchanged. CenterStatus remains three u32 values; routing stays native.

Images without a matched passport adapter retain the original host login-provider
object. CN2011 uses the earlier known identity with the shared callback wrapper
for its empty promoted connection. Malformed optional caption metadata is
diagnosed and discarded without disabling passport adaptation.

The original CN2011 executable and its expanded image have different image sizes.
Both require matching native string functions before caption assignment. An unavailable
optional title override emits a diagnostic and preserves native titles without
blocking provider authentication. Passport validation remains mandatory where configured.

The component checks fingerprints and signatures before native calls, clears
temporary ticket buffers, uses no worker, and stops its window timer at termination.
The caption buffer remains owned and released by the client. See the bilingual
cn-auth-center-resume guide in the source distribution for evidence and tests.
