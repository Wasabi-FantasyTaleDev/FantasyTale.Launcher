# Shipped-reader archive fallback verification

Build against an unchanged distribution, then run with its Windows architecture:

```powershell
dotnet build tools/verify-launcher-fallback/VerifyLauncherFallback.csproj -c Release `
  -p:BaselinePackage=D:/path/to/unchanged/package
& 'C:/Program Files/dotnet/x64/dotnet.exe' `
  tools/verify-launcher-fallback/bin/Release/net10.0/VerifyLauncherFallback.dll `
  D:/path/to/unchanged/package D:/path/to/fresh/acceptance-output
```

The tool compares loaded Core, Downloads and SDK hashes with the distribution.
It references those shipped assemblies directly, without current-source project
references, and copies the distribution's archive decoder when present. An x64
distribution requires an x64 runtime even when the SDK runs under ARM64.

All requests use an in-memory HTTP handler. Each transport, HTTP and AList, runs
ten scenarios: successful incremental staging, complete ZIP fallback, split ZIP
fallback, a different release, a corrupt archive transfer, changed contents,
missing files, unexpected files, duplicate paths and traversal paths. Successful
staging is independently checked against every selected file and restored with
network access disabled. Discovery must request no payloads; successful
incremental staging must request no archive metadata or volumes.

Small fixture payloads keep the output independent from distribution size. A
successful run writes `receipt.json` with loaded-reader identities and per-case
outcomes. It preserves the original distribution and starts no launcher, updater
helper, game or network service. This verifies reader compatibility with the
fallback protocol; it does not prove public archive availability, actual binary
installation, frontend health or support for untested archive formats.

For a real published fallback, supply the expected local index and its matching
`<index>.archive.toml` companion:

```powershell
& 'C:/Program Files/dotnet/x64/dotnet.exe' `
  tools/verify-launcher-fallback/bin/Release/net10.0/VerifyLauncherFallback.dll `
  --public-archive D:/path/to/unchanged/package D:/candidate/current.toml `
  D:/path/to/fresh/public-acceptance-output
```

This mode keeps the distribution's configured sources and copies it into the
fresh output. Only incremental payload failures are injected; discovery, archive
selection and archive bytes come from those real sources. It verifies the public
companion against the expected publication, every downloaded archive volume and
every expanded content object against their complete SHA-256 values. The visible
task must list archive volumes. Older readers reconstruct the ready task from
changed destinations after reopening; the offline task must contain exactly those
destinations with every file verified, and restore the same ready release.
Loaded reader and decoder identities must match the
unchanged distribution. The installation and source policy must remain unchanged.

Allow disk space for the copied distribution, archive volumes and expanded unique
objects. Use a data-volume output and explicit TEMP/TMP paths when the system
volume is constrained. The receipt distinguishes this public-network acceptance
from fixtures. It still proves staging and offline restoration only; installing
the update, external helper behavior and rendered frontend health need separate
acceptance. No launcher, updater helper or game is started by this mode.

If that public transfer is interrupted, run the same command with
`--resume-public-archive` instead of `--public-archive` and the same existing
output directory. The tool first verifies that its retained installation still
matches the unchanged baseline. The normal download engine resumes partial
archive bytes and verifies the complete result. The receipt records the saved
partial byte count and whether the existing output was reused; elapsed staging
time for a resumed run is not the time for a fresh full download.

To inspect an already staged output without downloading again, use
`--inspect-ready <unchanged-baseline-package> <existing-output>`. It prints safe
offline state and file counts and checks the loaded Core identity. This diagnostic
does not create a public-network acceptance receipt.
