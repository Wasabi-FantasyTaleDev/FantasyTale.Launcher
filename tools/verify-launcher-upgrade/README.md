# Shipped-reader incremental staging acceptance

Build against an unchanged distribution and run with its Windows architecture:

```powershell
$env:TEMP = 'D:/acceptance/temp'
$env:TMP = $env:TEMP
dotnet build tools/verify-launcher-upgrade/VerifyLauncherUpgrade.csproj -c Release `
  -p:BaselinePackage=D:/baseline/package -o D:/acceptance/verifier
& 'C:/Program Files/dotnet/x64/dotnet.exe' `
  D:/acceptance/verifier/VerifyLauncherUpgrade.dll `
  D:/baseline/package D:/candidate/current.toml D:/acceptance/fresh-output 300 0 32
```

The required arguments are the baseline distribution, exact expected index and
fresh output directory. Optional arguments are the overall staging deadline in
seconds (30 through 1800), explicit network retry count (0 through 3), and expected
visible changed-file count. Omit the last value when it is not established.
The example's 32 is a fixture-specific assertion, not a package/file limit.
Create the temporary directory on the data volume before running.

The loaded Core assembly must match the baseline. The verifier copies that
distribution without its generated transaction directories, uses its configured
source policy, stages through its shipped reader, verifies exact SHA-256 content
and reopens the transfer with networking disabled. It starts no Launcher, game
or updater process and installs nothing. For a loopback fixture, configure only
an isolated copy's policy and record that change; this is not public-source
acceptance.

Current readers need not copy unchanged files into the staging cache. Every
selected destination must have either matching staged content or matching
installed content. These independent full-hash audits run with four workers and
do not replace the product's ordinary metadata/hash cache. The receipt separates
staged destinations, reused installed destinations and the visible task count.
Full index size is not the number of downloaded files. The verifier rejects any
payload object request whose content was already unchanged in the baseline.

To continue an interrupted or already ready isolated output:

```powershell
& 'C:/Program Files/dotnet/x64/dotnet.exe' `
  D:/acceptance/verifier/VerifyLauncherUpgrade.dll --resume-staging `
  D:/baseline/package D:/candidate/current.toml D:/acceptance/existing-output 300 0 32
```

Resume first verifies that all original distribution files in the copied
installation still match the baseline, including its source policy. It refuses
an installation that has been upgraded or edited. It does not recopy the
distribution or discard existing partial/verified objects. Request counters in
the receipt cover this invocation only; a resumed run is not a fresh-download
benchmark. Preserve earlier transport logs separately when evaluating total
request counts. HTTP diagnostics contain only exception/category/socket enums,
without exception messages or temporary storage URLs.

Use an HTTP server with reliable concurrent response handling. The standalone
fixture should support ordinary GET and correct Range/Content-Range for resume.
Staging, unchanged-file reuse and offline restoration do not establish external
replacement, automatic restart, rollback or rendered frontend health. Those
remain separate desktop/helper acceptance gates.
