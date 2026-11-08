# Connection settings

Initial server discovery honors an explicit server preference or the global
all-regions IP setting before a server policy is known. The China-only default
does not assume that an unknown server is in China. Plain HTTP may resolve the
stored domain to an IP; HTTPS retains its certificate hostname. Public discovery
may try the alternate route after a failure where supported. Once a server
declares a routing requirement, authenticated work follows it. Safe API reads retry one transient transport or 502/503/504
failure within their existing caller deadline. Authentication, ticket issuance
and admission POSTs are not replayed. A directory's own timeout is reported as a
network timeout; canceling a launch remains a separate user action.

[Chinese](network-settings.zh.md)

Connection settings contain the IP access policy and GitHub mirror acceleration.
The installation-directory shortcut is hidden while client folders remain
available from the Clients page. Update preferences remain on the Updates tab.
Store sources can override the global IP policy independently. Their IP option
is also available when adding or editing a source.

## Server API transport

The server editor's separate Use HTTPS switch remains disabled by default.
When disabled, API requests retain the configured address even if the catalog
advertises a secure URL. When enabled, the advertised HTTPS address takes
precedence, otherwise the configured HTTP address is upgraded while retaining
its path and nondefault port. Explicit HTTPS addresses remain HTTPS. Cluster
authentication supports both configured transports; IP preference is independent.

## Server API transport

The server editor retains its Use HTTPS switch, disabled by default. With the
switch off, requests keep the configured API address; an advertised secure URL
does not silently replace an HTTP address. With the switch on, requests use the
advertised HTTPS address when present, otherwise upgrade the configured HTTP
address while preserving its path and nondefault port. Explicit HTTPS addresses
remain HTTPS. Authentication and cluster directory discovery support both
configured transports. IP access preference is independent of this switch.

## GitHub mirror source

Expand GitHub mirror acceleration to choose Automatic or a specific mirror source.
The header switch enables routing independently of whether the card is expanded.
Test opens a separate scrolling results dialog; Use selects that node and enables
mirror acceleration. Failed nodes remain selectable because reachability varies
by network and can change later.

Until its independent detection marker has been saved, the launcher requests an
IP-country result once in the background, including for existing installations
whose preferences predate this check. Mainland China (`CN`) shows a suggestion
dialog after the workspace appears. Its settings card switch defaults to on.
The single Confirm button saves that switch choice, including disabled. Detection
alone never enables routing. Other countries and regions default to disabled without a dialog.
A manual choice takes priority over an outstanding lookup and survives restarts.
Completed or failed detection is recorded so later launches do not repeat it.
A failed lookup preserves the existing preference. A pending suggestion survives
a restart before display; displaying it records a separate marker, so it cannot
reappear on subsequent launches even if the application closes before confirmation.
These markers contain no IP address or country string.

The packaged `github-mirrors.toml` contains independent sources and a snapshot of
the directory's nodes. Each measurement refreshes the public directory from its
current same-origin scripts without executing them. Individual script failures
do not discard nodes found in other scripts. When the directory is unavailable,
the packaged snapshot remains usable. A saved selected node is retained if a
subsequent directory refresh no longer lists it. No nodes are removed just because
a speed test fails. Rows marked `directory_snapshot = true` are replaced by a
successful directory refresh; other configured sources remain independent.

Measurements use concurrent, bounded byte-range requests against the configured
public archive. Time includes connection establishment; the displayed rate is a
small-sample measurement, not a promised full-package throughput. Concurrency,
sample size, deadline and archive prefix are defined in the packaged TOML.

Automatic release/archive downloads measure before requesting payload bytes and
try available nodes in measured order. A manually selected source is tried first,
followed by the other sources. Direct GitHub is the final route. Each route is
attempted at most once per transfer; an interrupted response body resumes the
hash-specific partial on the next route. Invalid lengths, ranges or hashes discard
that partial before retrying. Every completed file must pass the original size and
SHA-256 checks before replacing the destination. User cancellation stops fallback.
Measurements have their own per-node deadlines and do not consume the payload's
request-header deadline. Ordinary metadata requests use the current order without
waiting for a release speed test.

Only public HTTPS GET requests to recognized GitHub origins are routed. Writes,
requests carrying authorization/cookies, and other sites keep their original
transport. Mirror selection does not alter update manifests, signatures, integrity
checks, installation policy, external replacement, health confirmation or rollback.

## Overlay developer settings

UI Overlay test is an expandable card in Developer options. Its header switch
controls whether the next game launch shows the test message. The outside-click
passthrough setting remains inside the expansion even while the test is disabled.
Changing either preference takes effect on the next game launch.
