# WebView document policy checks

[Chinese](README.zh.md)

Run on Windows with the WebView2 runtime installed:

```powershell
dotnet run --project tools/webview-policy-test -c Release -r win-arm64
dotnet run --project tools/webview-policy-test -c Release -r win-x64
```

The executable uses the Overlay's actual event policy and shared document matcher
inside an off-screen WebView2 window. It verifies real message source metadata,
non-string messages, hash routing, reload, local resources, blocked documents,
popups, an opaque-origin settings frame and delegate lifetime after collection.
Failures return a nonzero exit code; browser event waits have deadlines. Output
records the process architecture and installed browser version. Test pages and
the browser profile use a new temporary directory, which can remain locked briefly
after the browser is disposed. No real accounts or game resources are used.

The Core test suite separately checks malformed URI spellings and configuration.
Neither suite establishes the main Photino bridge policy: that host needs native
event metadata and a separate integration test against its actual dependency.
