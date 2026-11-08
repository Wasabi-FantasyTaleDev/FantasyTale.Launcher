# Client diagnostics

[Chinese](diagnostics.zh.md)

## Launcher startup and exit

The host records local startup, frontend readiness, window activation, close
requests and registered bridge method outcomes in
`%LOCALAPPDATA%/FantasyTale/Launcher/host-lifecycle.jsonl`. An explicit
`--data-root` places it beside that data directory's other files. The current
file is bounded to 64 KiB with one retained rotation, `host-lifecycle.jsonl.1`.
This does not require a game or the client Diagnostic mode plugin.

Records include UTC time, process ID, event/method names and exception type or
the fixed shutting-down outcome. They exclude bridge parameters, responses,
exception messages, account names, passwords, tokens and tickets, and are not
uploaded automatically. Recording failure never blocks a window action.
For a cross-PC startup/exit issue, retain both files from the affected PC after
reproducing it; logs from a working PC do not identify another PC's failure.

Gateway connection diagnostics retain a connection's role, local listener port
and fixed lifecycle events, including its uniqueness exchange and closure. They
do not contain remote addresses, account identifiers or protocol bodies. The
selector and Launcher each enforce a 64 KiB metadata budget; exhaustion emits a
fixed marker. Repeated unknown stderr is redacted and deduplicated so it does not
consume the connection-event budget. Disk rotation remains bounded to two 64 KiB
files, and the copy action returns at most the latest 512 validated launch records.

## Game process diagnostics

Enable **Diagnostic mode** in the client's plugin selection before launching.
The plugin is not selected by default. Each launch gets a separate directory named by its
local start time, such as `diag_20261103_044700`; the launcher retains the latest ten. A
directory that holds a file the launcher did not write is left untouched and no longer counted.
The diagnostics action opens that client's log folder.

* `fthook.log` records activation outcomes without the login account or ticket.
* `crash.log` contains bounded exception observations, including context flags and segment registers.
  The first serious fault also records dispatcher frames and up to eight local exception handlers.
  It is retained when earlier handled exceptions exhausted the observation count, subject to the file size cap.
  Addresses from `stack_candidates` are memory matches, not a verified call stack.
* `exception.dmp` captures the first supported serious exception using a worker
  and the system dump library, loaded by its absolute system-directory path even if the game already loaded a same-named library. Its default cap is 32 MiB, configurable up to
  64 MiB, and the faulting thread waits no longer than ten seconds.
  Readable pages of the main client image are also requested, using at most half
  of that same cap; the complete dump still obeys the configured limit. Guarded
  or absent pages are skipped. `image-memory-complete`, `image-memory-partial` or
  `image-memory-not-captured` identifies whether those runtime pages were retained.
* `exception.dmp.status.txt` records dump initialization and the writer's outcome,
  with a 1 KiB cap. `written error=0` confirms the final file was saved;
  `failed-size-limit`, `failed-create`, `failed-write`, `failed-flush` and
  `failed-publish` distinguish failure stages and include a Windows error number.
  A file ending at `armed` means no dump write began; one ending at `writing`
  does not establish completion. A failed final save preserves the temporary
  file and any existing final dump instead of overwriting it.
  If the system writer returns error 87, one retry omits the exception stream while preserving
  process and thread data within the same size cap. `written-without-exception error=0`
  confirms this fallback snapshot; the original exception remains in `crash.log`.
* `receive.ftnet` records main-executable synchronous `recv` and `recvfrom`
  imports. Its default cap is 16 MiB, configurable up to 64 MiB. It flushes each
  record so earlier data can survive a later process failure.

An exception snapshot can describe a first-chance exception that the game later
handles. Exiting normally or external process termination need not generate a
dump. Receive records are raw transport chunks, not decoded game packets; they
exclude outgoing traffic, asynchronous receives, dynamic imports and receives
inside other DLLs. Dumps and traces can contain account data from game memory
or traffic. Keep them local and inspect their contents before sharing.

The FTNET stream begins with ASCII `FTNET` and version byte 1. Each record has
a 24-byte little-endian header: tick u64, socket u32, wire length u32, captured
length u32 and flags u32, followed by the captured bytes (at most 64 KiB).
Controlled fixture tests verify a valid access-violation dump and a loopback
receive trace. Isolated writer tests also cover size limits, write failure,
existing temporary/final files and the status-file cap. Real-game validation
remains a separate acceptance step.

The process fixture covers every currently declared diagnostic compatibility ID:
`kr-812`, `cn-279`, `cn-324`, `cn-406`, `cn-706`, `cn-717`, `id-400`, `id-1809`
and `kr-1256`. It launches a controlled native executable with the real runtime
and diagnostic DLL, receives loopback data, then causes an access violation.
Each case checks the dump exception stream and writer status, the trace payload,
original library restoration and removal of `session.toml`. These identifiers
exercise plugin selection and session labeling; the fixture does not reproduce
each retail executable's receive imports, exception handlers or graphics lifecycle.
