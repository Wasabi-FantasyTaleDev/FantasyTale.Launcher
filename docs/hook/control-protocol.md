# Local hook control protocol

For a schema-two launch, the launcher creates
`\\.\pipe\FantasyTale.Hook.<launch_id>` before starting the client. It uses a
single first-instance pipe restricted to the current Windows user. The launcher
checks the connected client PID; the native host checks the server PID against
`session.launcher_pid`. The random launch id and process creation timestamp in
readiness prevent accidental reuse of another launch's state. This does not
sandbox code already running as the same user.

Frames are a four-byte little-endian length followed by UTF-8 TOML. Requests
are limited to 4096 bytes, replies to 65536. One request may be in flight, with
a five-second launcher deadline. Invalid frames, mismatched replies and failed
peer validation close the channel. No login credentials cross the protocol.

```toml
protocol = 1
request_id = 1
method = "ping"
```

The response repeats `protocol` and `request_id`, with `result` and a string
`body`. `ping` returns `ok` / `pong`; `status` returns `ok` and the readiness
TOML text, or `not-ready`; `stop` acknowledges `stopping` and terminates the
client from inside its process. The launcher sends stop only after the user's
confirmation or its explicit startup-failure cleanup path. It then waits for
process exit before restoring files. Pipe closure alone never terminates a game.
Disposing the launcher channel cancels a request waiting for connection or reply.
An explicit later stop can use the owned operating-system process when the channel
is unavailable; it still awaits exit and restoration before releasing the client lease.

A `status` reply with `result = "not-ready"` and an empty body is a valid
temporary state. The caller may retry; the connection and request sequence remain
usable for ping and stop. Other non-success results or a nonempty not-ready body
are invalid and close the channel.

Repeated stop requests for the same owned invocation share one launcher task,
including a client stop overlapping explicit application exit. They issue one
native stop and wait for the same process-exit/cleanup result, rather than treating
the second request as a busy pipe and racing the first with an operating-system
kill. A failed stop operation can be retried while the invocation remains owned.

This version has no arbitrary plugin command dispatch, event subscription,
remote network listener or dynamic unload operation.
