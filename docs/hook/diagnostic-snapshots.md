# Diagnostic snapshot lifetime

Diagnostic mode observes first-chance exceptions, including exceptions the client
later handles. A recorded access violation or the newest observation in `crash.log`
does not by itself prove an unhandled crash or identify the process exit cause.

The bounded writer accepts one snapshot per process. Normal `WM_DESTROY` and
`WM_NCDESTROY` are not exit requests: the startup splash and secondary windows
regularly disappear while the game continues. They remain eligible for window
event logging, but do not claim the dump writer. `WM_CLOSE`, `WM_QUIT` and a true
`WM_ENDSESSION` queue a close snapshot without waiting on the UI thread. The
snapshot is best effort and may not complete if the process exits immediately.

Window event logging attempts the recorder lock once. It skips that event when
another observation owns the buffer rather than waiting on a window thread while
DbgHelp may need to suspend or inspect that same thread. Serious-exception requests
still retain the copied fault context and bounded wait needed by the existing
exception writer. The size cap, system DbgHelp loading and partial-file rejection
remain in force.

A status of `written-window-close` describes an all-thread close snapshot without
an exception stream. It must not be reported as a terminal exception dump. When
diagnosing a suspected exit, correlate the launch lifecycle and process outcome
with the dump's capture reason, time, thread contexts and actual exception stream.
Arbitrary words found on the stack are candidates, not established call frames.

`native/tests/dump_writer_tests.cpp` exercises real system dump streams, provider
failures, caps and the close-trigger policy. Its asynchronous fixture blocks the
writer until the caller returns, proving that window-triggered snapshots do not
wait for dump completion.
