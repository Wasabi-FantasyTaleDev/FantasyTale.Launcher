<#
Builds a complete local Launcher from this checkout and starts it.

A plain `dotnet build` produces only the Launcher host. A runnable Launcher also needs what the release packager adds:
the native runtime and bundled native plugins, the web UI, the Gateway, the overlay and updater, the archive tools and
each game's media (videos), levels and native files. This script builds what the checkout can build, then copies
every file still missing from a reference release package, never replacing a file this build produced.

  powershell -ExecutionPolicy Bypass -File tools/dev-launcher.ps1                      # build all, then start
  powershell -ExecutionPolicy Bypass -File tools/dev-launcher.ps1 -NoStart             # build and stage only
  powershell -ExecutionPolicy Bypass -File tools/dev-launcher.ps1 -Reference <package> # another release package
#>
param(
    [string]$Reference = '',
    [switch]$NoStart,
    [switch]$SkipNative,
    [switch]$SkipWeb
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repository 'src/FantasyTale.Launcher.Host/bin/Release/net10.0'

function Step($text) { Write-Host "==> $text" -ForegroundColor Cyan }

# The newest release package under the main checkout's artifacts holds the Gateway, overlay, updater, media and other
# files a development build does not produce.
if (!$Reference) {
    $roots = @((Join-Path $repository 'artifacts'))
    $main = (& git -C $repository worktree list --porcelain | Select-String '^worktree ' | Select-Object -First 1).ToString().Substring(9)
    if ($main) { $roots += (Join-Path $main 'artifacts') }
    $Reference = $roots | Where-Object { Test-Path $_ } | ForEach-Object { Get-ChildItem $_ -Directory -Filter 'release-*' } |
        ForEach-Object { Join-Path $_.FullName 'build-main/package' } | Where-Object { Test-Path (Join-Path $_ 'gateway') } |
        Sort-Object { [version](($_ -replace '.*release-([0-9.]+).*', '$1')) } | Select-Object -Last 1
}
if (!$Reference -or !(Test-Path (Join-Path $Reference 'gateway'))) {
    throw 'No reference release package with a gateway folder was found; pass -Reference <artifacts/release-x.y.z/build-main/package>.'
}
Write-Host "Reference package: $Reference"

if (!$SkipNative) {
    Step 'Building native/FantasyTale.Native.sln (Release, x86)'
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild/**/Bin/MSBuild.exe' | Select-Object -First 1
    if (!$msbuild) { throw 'MSBuild was not found; install the Visual Studio C++ build tools.' }
    & $msbuild (Join-Path $repository 'native/FantasyTale.Native.sln') /m /nologo /v:minimal /p:Configuration=Release /p:Platform=x86
    if ($LASTEXITCODE -ne 0) { throw 'The native build failed.' }
    Step 'Staging the native runtime and bundled native plugins'
    & (Join-Path $repository 'tools/package-native.ps1') -Configuration Release -RuntimeOnly
    & (Join-Path $repository 'tools/package-native.ps1') -Configuration Release -BundledPluginsOnly
}

if (!$SkipWeb) {
    Step 'Building the web UI'
    Push-Location (Join-Path $repository 'web')
    try {
        if (!(Test-Path 'node_modules')) { npm ci --no-audit --no-fund; if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' } }
        npm run build; if ($LASTEXITCODE -ne 0) { throw 'The web UI build failed.' }
    } finally { Pop-Location }
}

# A Launcher started from this output (it stays in the tray when its window closes) locks the DLLs the build replaces.
$running = Get-Process FantasyTale.Launcher, FantasyTale.Launcher.Overlay -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($output, [StringComparison]::OrdinalIgnoreCase) }
foreach ($process in $running) {
    Write-Host "Stopping the previous Launcher from this output (pid $($process.Id))"
    Stop-Process -Id $process.Id -Force
    $process.WaitForExit(10000) | Out-Null
}

Step 'Building FantasyTale.Launcher.slnx (Release)'
dotnet build (Join-Path $repository 'FantasyTale.Launcher.slnx') -c Release -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'The Launcher build failed.' }

Step 'Copying every file the build did not produce from the reference package'
$copied = 0
$referenceRoot = (Resolve-Path -LiteralPath $Reference).Path.TrimEnd([char]92, [char]47) + [IO.Path]::DirectorySeparatorChar
Get-ChildItem -LiteralPath $Reference -Recurse -File | ForEach-Object {
    $relative = $_.FullName.Substring($referenceRoot.Length)
    # Feeds and indexes describe the published release, not this build.
    if ($relative -match '^(launcher-update|launcher-download)\.toml$') { return }
    # The release package is a self-contained x64 build; its top-level runtime and framework binaries would replace this
    # build's own host on another architecture (an ARM64 machine then fails to load hostfxr.dll). Only top-level text
    # files come from the package; everything in its folders (gateway, overlay, updater, media, ...) is copied.
    if ($relative -notmatch '[\\/]' -and $relative -match '\.(dll|exe|json|pdb)$') { return }
    $target = Join-Path $output $relative
    if (!(Test-Path -LiteralPath $target)) {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $target
        $script:copied++
    }
}
Write-Host "Copied $copied missing file(s)."

# Large game media (background videos and music) is git-ignored: it lives only in the main checkout's games/*/media and
# is in no release package, so a worktree build has to take it from there.
$mainCheckout = (& git -C $repository worktree list --porcelain | Select-String '^worktree ' | Select-Object -First 1).ToString().Substring(9)
Get-ChildItem -Path (Join-Path $mainCheckout 'games') -Directory | ForEach-Object {
    $media = Join-Path $_.FullName 'media'
    if (!(Test-Path $media)) { return }
    $targetMedia = Join-Path $output "games/$($_.Name)/media"
    New-Item -ItemType Directory -Force -Path $targetMedia | Out-Null
    Get-ChildItem -LiteralPath $media -File | Where-Object { !(Test-Path -LiteralPath (Join-Path $targetMedia $_.Name)) } |
        ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $targetMedia; Write-Host "Media: games/$((Split-Path $media -Parent | Split-Path -Leaf))/media/$($_.Name)" }
}

# Every media file a game definition names must be present, or that game's page shows no video or music.
$missingMedia = Get-ChildItem -Path (Join-Path $output 'games') -Directory | ForEach-Object {
    $game = $_
    $definition = Join-Path $game.FullName 'game.toml'
    if (!(Test-Path $definition)) { return }
    Select-String -LiteralPath $definition -Pattern '"(media/[^"]+)"' -AllMatches | ForEach-Object { $_.Matches } | ForEach-Object {
        $file = $_.Groups[1].Value
        if (!(Test-Path -LiteralPath (Join-Path $game.FullName $file))) { "games/$($game.Name)/$file" }
    }
}
if ($missingMedia) {
    throw "Game media named in game.toml is missing (restore it into $mainCheckout\games\<game>\media): $($missingMedia -join ', ')"
}

Step 'Checking the staged Launcher'
$required = @('FantasyTale.Launcher.exe', 'wwwroot/index.html', 'gateway', 'overlay', 'updater', 'games/BubbleFighter/game.toml')
$missing = $required | Where-Object { !(Test-Path -LiteralPath (Join-Path $output $_)) }
if ($missing) { throw "Still missing after staging: $($missing -join ', ')" }
$referenceTop = Get-ChildItem -LiteralPath $Reference -Directory | ForEach-Object Name
$absent = $referenceTop | Where-Object { !(Test-Path -LiteralPath (Join-Path $output $_)) }
if ($absent) { throw "Folders of the release package absent from the build: $($absent -join ', ')" }
Write-Host "Launcher ready: $output" -ForegroundColor Green

if (!$NoStart) {
    Step 'Starting the Launcher'
    Start-Process -FilePath (Join-Path $output 'FantasyTale.Launcher.exe') -WorkingDirectory $output
}
