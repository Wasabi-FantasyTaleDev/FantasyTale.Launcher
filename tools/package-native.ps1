param([ValidateSet('Debug','Release')][string]$Configuration = 'Release', [switch]$RuntimeOnly, [switch]$BundledPluginsOnly)
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$nativeOutput = Join-Path $repository "native/out/$Configuration"
$game = Join-Path $repository 'games/BubbleFighter'
if ($RuntimeOnly -and $BundledPluginsOnly) { throw 'Select runtime-only or bundled-plugins-only staging, not both.' }
function Copy-BundledPlugins {
    $bundledPayloads = @{
        'bubblefighter.fantasy.diagnostics' = 'crash_recorder.dll'
        'bubblefighter.fantasy.native-ui-bridge' = 'game_controls.dll'
    }
    foreach ($payload in $bundledPayloads.Values) {
        if (!(Test-Path -LiteralPath (Join-Path $nativeOutput $payload) -PathType Leaf)) {
            throw "Build native/FantasyTale.Native.sln first; missing bundled plugin payload: $payload"
        }
    }
    foreach ($entry in $bundledPayloads.GetEnumerator()) {
        Copy-Item -LiteralPath (Join-Path $nativeOutput $entry.Value) -Destination (Join-Path $game "plugins/$($entry.Key)/$($entry.Value)") -Force
    }
}
if ($BundledPluginsOnly) {
    Copy-BundledPlugins
    Write-Output "Staged bundled native plugins ($Configuration); store releases remain unchanged."
    return
}
$package = Join-Path $game 'runtime/nmcogame'
$version = ([xml](Get-Content (Join-Path $repository 'native/Native.Configuration.props') -Raw)).Project.PropertyGroup.FtHookVersion | Where-Object { $_ }
$payloads = @('nmcogame.dll', 'login_bridge.dll')
if (!$RuntimeOnly -and !(Test-Path -LiteralPath (Join-Path $nativeOutput 'gpk_stub.dll'))) { throw 'Build native/FantasyTale.Native.sln including gpk_stub first.' }
foreach ($payload in $payloads) {
    if (!(Test-Path -LiteralPath (Join-Path $nativeOutput $payload))) { throw 'Build native/FantasyTale.Native.sln first.' }
}
New-Item -ItemType Directory -Force -Path $package | Out-Null
$manifest = "schema = 1`n[runtime]`ngame = `"bubblefighter`"`nversion = `"$version`"`nsession_schema = 2`n"
foreach ($payload in $payloads) {
    $file = Join-Path $nativeOutput $payload
    Copy-Item -LiteralPath $file -Destination (Join-Path $package $payload) -Force
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    $length = (Get-Item -LiteralPath $file).Length
    $manifest += "[[files]]`npath = `"$payload`"`nsize = $length`nsha256 = `"$hash`"`n"
}
[IO.File]::WriteAllText((Join-Path $package 'runtime.toml'), $manifest, [Text.UTF8Encoding]::new($false))
if ($RuntimeOnly) {
    Write-Output "Packaged native runtime $version ($Configuration); existing plugin payloads remain unchanged."
    return
}
Copy-Item -LiteralPath (Join-Path $nativeOutput 'auto_dash.dll') -Destination (Join-Path $repository 'store-plugins/BubbleFighter/bubblefighter.fantasy.auto-dash/auto_dash.dll') -Force
Copy-BundledPlugins
Copy-Item -LiteralPath (Join-Path $nativeOutput 'kr_auth.dll') -Destination (Join-Path $repository 'store-plugins/BubbleFighter/bubblefighter.fantasy.login-fix-kr/kr_auth.dll') -Force
# The Chinese store component uses the game module build and its native passport
# table. The runtime login_bridge.dll above is only the forwarding fallback.
Write-Output "Packaged native runtime $version ($Configuration)."
