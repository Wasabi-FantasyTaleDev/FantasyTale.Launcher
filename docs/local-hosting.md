# Local server hosting

[Chinese](local-hosting.zh.md)

Local hosting runs a separately installed server package on the player's computer.
Its saved configuration is separate from remote servers and installed game clients.
The shared adapter is declared by each supported client build: cn-279, cn-324,
cn-404, cn-406, cn-706, cn-717, cn-774, id-400, id-1809, kr-812, kr-1256 and
tw-454. Disabled or hidden builds remain unavailable. Both the bundled definitions
and store profiles carry this declaration; installed-client records are separate.
Shared process management has no game/build branches.

## Using the page

Single-player reuses a compatible private profile, or creates one with default
ports, local saves and name-only login. It starts the installed server package
and launches the selected client after readiness, without a configuration wizard.
If no compatible package is installed, choose a source and confirm its installation;
the dialog shows actual transfer and installation progress before continuing.
Advanced settings remain in a collapsed section.

Open Local hosting to create or edit a profile. The package directory must contain the published
`BubbleFighter.Fantasy.Host.exe`, dependencies, plugins, profiles, packet tables and
required client-data resources. A registered installed client supplies its own
archive and loose-file root directly, without copying game files. Package-only
hosting uses `client-data/<profile>/data` and `dataraw` instead. The host must support `run --managed-stdio`.
An older package fails startup instead of being treated as ready.

Private play binds game and account listeners to `127.0.0.1`. Room mode binds those
listeners to all IPv4 interfaces and advertises the address supplied by the host.
LAN mode binds to the selected local interface. Administration always remains on
loopback. Game, relay, API and administration ports are configurable and must
differ; new profiles obtain four available local ports. Modern profiles also
configure their relay listener. The launcher does not create firewall rules or claim that a private
address is reachable across the internet. Automatic network setup is future work.

Choose how player data is stored. New configurations default to **Local save**:
the adapter enables the package's SQLite provider and uses the profile's stable
`hosting/data/<profile-id>/save.sqlite`. Accounts and progress survive graceful
server restarts without a separately configured database. A complete server package
with its SQLite plugin and native dependency is required; older packages fail
explicitly. **Package database** uses the database configured by the package and
process environment. **Temporary data** enables bounded memory storage and loses
accounts/progress when the process ends. No mode falls back to another on failure.
Existing configurations retain their former package/temporary selection until edited.

Start server waits for real process readiness. Go to game then selects the local
destination on Home. New private and room profiles use name-only login by default;
the server explicitly advertises this policy, and only a room destination with that
policy receives the launcher's plain-name mode. Enable account authentication to
use the regular account registration, login and launch-ticket flow. Existing
profiles retain their saved choice. Name-only login requires runtime 3.6.0, KR
authentication 0.1.3 when applicable, and CN authentication 1.0.2 when its file
bridge is selected. Open administration
opens the server's existing web interface; the launcher does not duplicate its player,
gameplay or plugin administration controls.

The state, primary controls and title remain visible. Logs expand inside a bounded
scroll region. Stop server confirms disconnection and refuses while a managed game
is running. Removing a configuration preserves the server package and player data.

## Ownership and lifetime

Each invocation has a unique run id. Stop/log requests include that id, so a stale
request cannot affect a restarted process. A lease in the package directory prevents
two launcher processes from starting the same installation. Windows Job Objects own
the server and its descendants; closing the launcher process closes the job.

Go to game also verifies the exact invocation through `hosting.destination` before
selecting Home's server and client build. A replaced, stopped or faulted invocation
cannot supply a destination. Changing the selected profile or leaving the page
during the request cancels its UI continuation. Status polling accepts only its
newest response, including log results, so an older ready snapshot cannot overwrite
a later failure. The hosting page selects the destination; Home's single-player
action additionally launches the selected client after refreshing its services.

Processes start with redirected stdin/stdout/stderr, without a visible console or
shell expansion. Only a complete `FT_HOST_READY 1` stdout line marks readiness;
stderr, partial or truncated lines do not. Startup has a deadline. Normal shutdown
writes `stop`, closes stdin, and allows cleanup before terminating the owned job
on timeout. A server implementing the protocol also exits on stdin EOF. Unexpected
exit, startup timeout, logging failures and forced termination remain visible.

Before reporting the final state and releasing package/save leases, the launcher
checks the job's active-process count, terminates surviving owned descendants and
waits up to ten seconds for the count to reach zero. A surviving descendant marks
the stop as forced even when the main server exited normally. This also applies to
unexpected main-process exit. If tree verification fails, the invocation reports
`process-failed` and retains its package/save leases until launcher exit rather
than allowing another invocation to reuse potentially occupied files. This check
does not guarantee that unrelated external file readers have released those files.

The launcher does not impose fixed counts on servers, profiles or diagnostic
directories. Adapter-generated configuration files, command arguments and environment
overrides have no launcher-specific count or total-byte ceilings. File paths must
remain inside the invocation directory; duplicate filenames, Windows device names,
log-file collisions, null values and NUL characters in process input are rejected.
Environment names are unique without case sensitivity. Operating-system process
and filesystem limits still apply, and startup failures release their leases.
Generated YAML lives in the launcher's hosting directory; original package profiles
are untouched.

Diagnostics remain bounded: each output line is capped at 4096 characters, the UI
keeps 128 lines, and process output rotates at 2 MiB with one backup. The adapter
separately bounds server packet-log files and disables full captures by default.

Active local servers prevent launcher restart updates. Their temporary destinations
disappear when stopped and do not become permanent remote-server definitions.
Account sessions are invalidated before a restarted local service can be selected.

## Stable save directory

Each invocation also leases `hosting/data/<profile-id>` beneath the launcher data
root and supplies it to the module as `LocalServerContext.DataDirectory`. It is
separate from package files and rotating diagnostics. Renaming a profile, restarting
or selecting an updated package retains this path. Removing a hosting profile
preserves its saved data. The directory records its game and build; reusing that
identity for a different game/build, damaged metadata, unknown unowned files and
overlapping package/log/save roots fail before a process starts.

The save lease is independent of the package lease, so another process cannot run
against the same save by selecting a second package copy. Saved data has no fixed
directory-count ceiling. This directory contract is independent of the persistence
provider. Local mode activates SQLite
within it; temporary mode still loses process-local accounts on stop, while package
mode uses the package's database. Changing the storage choice does not migrate data
between providers. Stop the server before backing up its save directory.

## Verification and remaining integration

Isolated child-process tests cover readiness, stderr rejection, graceful exit,
startup failure/timeouts, forced termination, descendant cleanup, package leases,
log bounds and stale run ids. Bridge tests cover temporary server publication,
editing/removal fences and the updater interlock. Light/dark headless UI checks
exercise the page, settings, room address, animated expanders and confirmation.

An opt-in integration test also copies a complete published CN2013 package into an
isolated directory and runs the real server through the backend bridge. It verifies
six-character password registration/login, wrong-password rejection, launch tickets,
local administration, graceful stop, released ports, restart, session invalidation,
temporary-account loss, SQLite-account preservation and wrong-password rejection
after restart, save retention after rename/package replacement, isolation between
profiles, retained data after profile removal, and the separate remote/host-player
catalog addresses.
Two additional local-storage cases first stream the entire package through a loopback
HTTP source or an AList API fixture, install it through `store.installServer`, compare
every installed file's SHA-256 with the input distribution, and run the installed copy
through the same account/restart checks. Payloads are streamed from disk; package bytes
are not retained in a large in-memory test dictionary. The AList case exercises the real
source adapter against a protocol fixture, not the official remote deployment.
It never starts the launcher window or a game, and it excludes local overrides and
environment files from the copied package. Run it in PowerShell with:

```powershell
$env:FT_TEST_SERVER_PACKAGE = 'D:\\isolated-packages\\server'
dotnet test tests/FantasyTale.Launcher.Core.Tests -c Release --filter FullyQualifiedName~LocalServerPackageTests
```

The separate `ActualClientRetainsPlayerIdentityThroughOwnedServerRestart` case
launches an actual CN2013 client through the current Launcher backend. Enable it
only with an idle test client directory and a complete server package:

```powershell
$env:FT_TEST_SERVER_PACKAGE = 'D:\\isolated-packages\\server'
$env:FT_TEST_CN406_CLIENT = 'D:\\test-clients\\CN2013'
$env:TEMP = 'D:\\test-temp'
$env:TMP = $env:TEMP
dotnet test tests/FantasyTale.Launcher.Core.Tests -c Release --filter FullyQualifiedName~ActualClientRetainsPlayerIdentity
```

It enables hosting only in copied definitions, copies the server package and native
runtime, registers a fresh account, waits for authentication-plugin transfers and
checks the server's actual MainMenu player alongside the launcher's ready Hook.
After a normal owned-server restart it logs in and launches again, requiring the
same persisted player identity. Both stops verify the original executable and temporary messenger-library
SHA-256 values, retained permanent authentication-plugin payload hashes,
session-file removal and released server ports. The module's normal endpoint preparation can update the test client's
`login.ini`; the test does not restore persistent game settings. A locally prepared
client proves that configuration only, rather than an untouched retail installation.
Required plugin checks use the configured store and are not bypassed for the test.
Without both input variables on Windows, this case is explicitly skipped.

For a controlled ticket-expiry comparison with an older server package, set
`FT_TEST_LAUNCH_TICKET_SECONDS` to a positive lifetime in seconds supported by that
server. This changes only the copied CN2013 profile, leaves authentication enabled
and records the override with launch/acceptance timestamps. Omit it to exercise the
package's own policy. Failure cleanup explicitly stops owned clients and servers
before the backend's passive disposal.

Set `FT_TEST_CLIENT_HOLD_SECONDS` to a positive number to hold each accepted client
for independent visual inspection. Failed runs retain their isolated directory and
report its path; successful runs remove it and print a credential-free acceptance
record in test output. A passing run covers login, Hook readiness and account
identity across restart, while room/round gameplay and inventory mutations still
need their own acceptance.

The October 23 fresh-account run has not passed: a copied-profile 600-second
ticket was accepted, then the server's expiry/equipment repair ran before initial
inventory seeding and failed on missing required item `202001`. Its client
connection closed before MainMenu. This remains a server integration blocker;
earlier login observations below do not establish fresh-account initialization.
Server commit `436da0cc119547c00c54d5cc503857ab3c1a6a7f` already contains the
empty-inventory fix; the frozen `6e09728e` package used by this failed run predates
it. An updated package still needs actual-client acceptance.

Without the environment variable the external-package theory is explicitly skipped;
with it, four cases cover direct temporary/local storage plus HTTP/AList provisioning.
Each case also registers two accounts through the actual server API, selects and
refreshes the first, signs out only the second, and verifies that restarting the
owned server clears process-local launcher sessions. Persistent account data and
password verification are checked separately after restart.
CN2013 real-client local login and server restart have also passed: both launches
reached the same persisted player's MainMenu with a ready hook, and both stops
restored original DLL hashes and removed the session file. The maintainer confirmed
the first successful login visually. Room/gameplay mutations and their restoration
after restart still require separate acceptance.
Directory registration, backend heartbeats, expiry and discovery/join are implemented
in [Multiplayer directories](multiplayer.md), including the verified official endpoint.
Real remote-player acceptance remains open. Server packages can now be installed from the
Store and selected in the hosting setup dialog; manual directory selection remains available.
Source-bound releases install separately, sharing the hosting package lock. Full d66cc86a
package download, installation and startup passed through both HTTP and the AList
protocol fixture, followed by the account, ticket, administration and storage checks above.
Official server-package publication and anonymous AList provisioning through the
backend have also passed for this distribution. Desktop provisioning interaction
and real-client durable gameplay acceptance remain open. Rooms currently use explicitly
provided direct addresses, without an automatic network tunnel.


Single-player remains one Home selector entry. A ready private invocation for the
selected game/build is reused directly instead of reopening setup or adding a
second server choice. With account authentication off, the owned profile's
name-only policy applies to discovery and launch; no account restoration,
registration or sign-in is requested. Start remains available while signed out.
If no nickname has been supplied, Start asks for one and remembers it for later
launches. Startup failures provide View details with the exact invocation's
bounded output and log path, rather than requiring users to locate logs.
Setup can be dismissed during startup without stopping the owned server; dismissal
cancels the pending automatic connection. Log views refresh in place and follow
the newest output, without resetting when the server status snapshot changes.
