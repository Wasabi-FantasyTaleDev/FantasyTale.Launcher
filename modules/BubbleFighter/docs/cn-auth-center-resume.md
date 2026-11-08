# Chinese authentication and native Center login

[Chinese](cn-auth-center-resume.zh.md)

The Chinese authentication component owns the client-specific login adapter in
`login_bridge.dll`. The shared Messenger host remains the sole authority for
launch credentials and its one-delivery rule. No game-specific passport logic
is added to that host. The component retains one bounded copy of its authorized
provider record for a narrowly validated native connection continuation.

## One adapter, per-build data

`native-passports.toml` records PE timestamps, permitted image sizes, connection
RVAs, passport offsets, native string functions and exact instruction signatures.
Its comments identify every supported client. Initialization and each passport
assignment check executable memory and signatures before calling native code.
An audited passport profile with an unexpected image or function refuses authentication.
Other timestamps retain ordinary provider behavior.

The component wraps only the two known login-provider interface identities.
It wraps them only when the loaded image has a matched passport adapter. Images
without a passport adapter return the host's original provider object unchanged,
even when a caption override is active.
Lifecycle/setter calls preserve the original object as `this`. The synchronous
success callback first copies the host-authorized marked ticket into the current
connection's native passport, then forwards the original record to the game.
It never requests a second ticket or constructs a login packet. Failure callbacks
are forwarded without a passport write, and repeated callbacks are not delivered.
Temporary passport buffers are cleared on every exit.

CN2012's Automatic redirect constructs an empty replacement connection in
cn-324 sub_705710/sub_6FEEE0 and promotes it in cn-324 sub_705980. The new
LoginStage requests the provider again in cn-324 sub_584580. The component may
answer that request once with its cached, already authorized record, only after
selection, connection establishment and pending promotion are complete, the
current connection differs from the seeded one, native login is incomplete and
the new passport is empty. cn-324 sub_584890 cannot already have sent login from
that empty passport. The attempt is consumed before calling client code; the
original cn-324 sub_5852D0 still converts credentials and sends native login.
No executable or vtable patch is added. The cache is cleared at initialization
and termination. This is one continuation per launch, not arbitrary ticket replay.

CN2011 uses the same continuation with its earlier provider identity. cn-279
sub_6FBE60 constructs the empty passport through cn-279 sub_6FA250, and cn-279
sub_6F1A70 promotes it. cn-279 sub_857F20 resets the provider-request flag;
cn-279 sub_8588F0 requests a new callback. The selected branch of the cn-279
sub_8580A0 provider callback sends native login through cn-279 sub_4203B0. Its initial unselected
branch caches account data without sending, independently of the passport;
seeding that passport does not change the selection flags or submit login early.
The original and expanded image sizes are both explicit profile data, with native
function signatures still checked at runtime. This adds no new Hook.

CN2013 cn-406 and CN2015 cn-717 copy the passport during redirection. Their
replacement LoginStage may request the provider even when its Entry skipped
the native sender. The same connection guards apply; a nonempty passport must
exactly match the authorized ticket. The adapter checks the original region,
automatic-login feature and early-abort predicates through fingerprinted native
functions. When Entry skipped sending, it delivers the cached record once.
When Entry selected its sender path, it consumes the redundant provider request
without another callback, passport assignment or host authorization request.
This establishes which native sender path was selected, not server receipt or
authentication success. An unavailable predicate preserves the host's rejection.
cn-406 sub_576DD0 and cn-717 sub_6DA700 ignore the provider return value and
retire their request after the call; their Entry completes before that tick.

CN2019 uses the same continuation after cn-774 sub_BA61D0 copies its passport
and cn-774 sub_BA6D70 promotes the connection. The target's patch response
requests a new LogoStage and then LoginStage. cn-774 sub_9FA4A0 selects the
native cached sender only when feature 1502 is present; cn-774 sub_9FA7F0
requests the provider again when feature 1004 is present. Its Entry predicate
has no region or early-abort branch. The profile selects that feature-only
predicate and the ECX argument contract of cn-774 sub_10695D0 explicitly.
The same exact-ticket, connected, selected, pending and completion guards apply.
When Entry skipped sending, cn-774 sub_9FB1A0 receives the authorized record
once and constructs the login itself. No executable, vtable or wire change is
added for this continuation.

| Client | Native selection and credential evidence | Passport offset |
| --- | --- | --- |
| CN2011 cn-279 | cn-279 sub_6FA250 initializes the replacement passport; cn-279 sub_8580A0 sends the selected provider record. | 2252 |
| CN2012 cn-324 | cn-324 sub_78DA40 copies the old connection passport; cn-324 sub_584890 sends native login when it is nonempty. | 2252 |
| CN2013 cn-406 | cn-406 sub_7D4490 copies the passport; cn-406 sub_577130 resumes native login. | 2392 |
| CN2014 cn-404/cn-408/cn-705/cn-706 | Each profile records its audited native string assignment function and connection global. | 2392 |
| CN2015 cn-717 | cn-717 sub_983290 copies the passport; cn-717 sub_6DAA80 resumes native login. | 2440 |
| CN2019 cn-774 | cn-774 sub_A23360 preserves the selected connection passport; cn-774 sub_9FAC30 returns without login if it is empty. | 2492 |

CN2012 allocates a temporary COW string through cn-324 sub_401C00, assigns it
through cn-324 sub_401B10 and releases it through cn-324 sub_480390. The selected
connection owns a native reference after temporary destruction. The later builds
use their own 24-byte native string assignment routines. The destination is the
passport field, not the start of the connection object.

CN2019 uses cn-774 sub_44E030 with a character pointer and count. This is a
different calling contract from the earlier native-string range assignment;
the `span` strategy selects that contract explicitly. Its exact image and
instruction signature are checked before invocation.
The original CN2019 executable has image size `0x1B7E000`; the expanded image is
`0x1B80000`. Both share the audited timestamp and remain subject to the exact
runtime assignment signature. Recognizing only the expanded size would reject
the original before the native chooser could complete startup.

The previous CN2012 chooser-continuation Hook is removed. These passport adapters
write native client state through its own functions; they do not patch executable
instructions and must not be described as protocol-only or zero intrusion.
Native code retains credential conversion, selection, serialization and routing.

## Optional CN2011 names

CN2011 cn-279 sub_405360 reads only three u32 values from CenterStatus;
adding a string would corrupt the protocol. The original local title formatter
is left unchanged. The component instead updates the already constructed
button caption through the client's own COW string functions.

Launcher supplies full directory names through capability
`bubblefighter.cn-center-names.v2`, with a TOML payload containing `schema = 1`
and `[[centers]]` rows of positive `id` and optional `name`. Earlier components
ignore this capability, so receiving newer display metadata cannot activate
the former formatter replacement. Missing or empty names preserve native titles.
The component reads only this display envelope from the launch session;
credential authorization remains in the provider.

cn-279 sub_4A78A0 publishes the completed chooser at UiSystem+8 and pumps messages
through cn-279 sub_4A9100. A Windows window timer runs on that same UI thread.
Both chooser vtable pointers must match before reading buttons +356..+360.
cn-279 sub_64C8C0 constructs these buttons in native Center vector order, so each
button is paired with that Center's identity at +2328. Names are selected by
identity, not endpoint. Both button vtable pointers and all vector extents must
match before any caption is changed. Snapshot storage uses the actual row count
within the existing transport budget, without a separate 64-row product limit.

cn-279 sub_886A70 initializes caption +336; cn-279 sub_8868F0 reads it on every draw.
The adapter constructs a temporary with cn-279 sub_401570, assigns through cn-279
sub_4012D0, then releases the temporary through cn-279 sub_401510. The button owns
the resulting native reference; its original destructor releases that reference.
Selected Center, capacity, connection, original virtual tables and executable
instructions remain unchanged.

cn-279 sub_8868F0 centers text in a 70-pixel rectangle using flags 5; cn-279
sub_540FF0 subtracts half the text width, clipping long names at the button's left
edge. Named buttons instead use a copied primary virtual table: its original
complete-object locator and 48 entries are retained, with only drawing slot 3
adapted. Buttons without supplied names retain their original table and alignment.

The drawing adapter copies the 340-byte read view used by cn-279 sub_8868F0,
leaves the live object untouched, and calls the original routine with an empty
view caption to draw its background and select its font. It then invokes cn-279
sub_5375B0 at the original coordinates with flags 4 for left alignment and native
vertical centering. It holds the caption's COW reference through drawing and
exception cleanup. A thread-local guard blocks nested caption writes. The copied
table and original call addresses remain pinned; after termination, surviving
adapted buttons call the original drawing routine directly. Reinitialization
rejects changed immutable draw parameters. No executable code or original table
is patched. This is a narrowly scoped button drawing Hook, not a zero-Hook feature.

Initialization and native string calls validate the image fingerprint and exact
function signatures. Invalid optional display metadata preserves native titles
without weakening mandatory passport validation. Termination cancels the window
timer; the callback module stays pinned to make already queued callbacks safe.
No worker or additional message Hook is installed.

cn-279 sub_8868F0 passes the caption as the format string to cn-279 sub_5375B0,
which calls the native wide printf formatter without additional caption arguments.
Literal percent signs are therefore doubled in stored caption text; the rendered
name remains unchanged. The expanded storage bound covers 128 percent characters.

## Verification

Build with Visual Studio v145, Release|Win32:

```powershell
msbuild native/tests/cn_auth_tests.vcxproj /p:Configuration=Release /p:Platform=Win32
artifacts/tests/cn_auth_tests.exe
python tools/package-cn-auth.py --gpk <verified-gpk-payload> --output artifacts/cn-auth/cn-auth-1.0.14.zip
```

The actual DLL is loaded against a synthetic provider. All 989 checks pass,
covering provider ABI and credential continuation, native COW lifetime, real
Windows timer dispatch, 65 rows, shared empty captions, missing and malformed
metadata, ignored v1 metadata, literal printf rendering and the 128-percent
boundary. Drawing checks pin the copied locator and all 48 entries, live-object
immutability, timer and draw reentry, exception cleanup, both native margins,
termination pass-through, reinitialization and invalid view/table shapes.
Executable bytes, original tables, selection and connection state stay unchanged.
Read-only checks independently match functions and table identities against the
original executable.

An earlier candidate using the same UI-storage path displayed six supplied Chinese
captions in an isolated original CN2011 client while
the original formatter CALL remained unchanged. Observation ended after 70.39
seconds with exit code 0 when the local fixture's 55-second selection deadline
expired. This validates the candidate display path, not exact frozen-package client acceptance,
production server login or an
uninterrupted 100-second observation. Native login acceptance is tracked separately.

The final 1.0.14 DLL also ran in the isolated original CN2011 client for 100.05
seconds with all six full names, unchanged original code, matching copied RTTI,
and exactly slot 3 adapted on each supplied-name button. The user confirmed the
left-aligned appearance. The local display fixture does not validate production
server entry.

## CN2014 startup repair

Component 1.0.16 requires Launcher 0.1.54 and FtHook 3.7.1. Its single
cn-706-only patch replaces the call at cn-706 0x868BA3 in sub_868920 with a
relative jump to cn-706 0x868C16. The original startup path calls sub_865110
(the retired updater), sub_865440 (initialization), and sub_865670 (startup);
the updater failure displays Update Error before game authentication. The
continuation has the same stack depth, and native shutdown sub_AEE4BC and
sub_AEE6EA check module and entry presence. A unique surrounding signature and
exact expected call bytes are required before writing. No identity-report
compatibility patch is included. Other builds have no active patch and retain
the existing file deployment and provider behavior.

Offline verification matches the original unpacked image. The packed cn-706
client, with this component as its only native-session package, reached a local
TCP listener and remained running for 150 seconds without Update Error or a
plugin-loading error; the maintainer confirmed both dialogs were absent. This
fixture provides no game replies and does not establish production login,
server selection, transfers or round-start acceptance.
