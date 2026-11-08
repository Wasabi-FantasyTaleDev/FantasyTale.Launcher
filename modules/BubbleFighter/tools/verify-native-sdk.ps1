param([ValidateSet('3.3.3', '3.7.2')][string]$RuntimeVersion = '3.3.3')

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = Split-Path -Parent $PSScriptRoot
$sdkRoot = Join-Path $repository "native/sdk/fthook-sdk-$RuntimeVersion"
$inventory = Get-Content -LiteralPath (Join-Path $sdkRoot 'sdk.json') -Raw | ConvertFrom-Json
if ($inventory.schema -ne 1 -or $inventory.runtime_version -ne $RuntimeVersion -or $inventory.abi_version -ne 1) {
    throw 'The pinned native SDK identity is invalid.'
}

foreach ($entry in $inventory.files) {
    $path = Join-Path $sdkRoot $entry.path
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing SDK file: $($entry.path)" }
    $file = Get-Item -LiteralPath $path
    if ($file.Length -ne $entry.size) { throw "SDK size mismatch: $($entry.path)" }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $entry.sha256) { throw "SDK hash mismatch: $($entry.path)" }
}

Write-Output "Verified FtHook SDK $($inventory.runtime_version), ABI $($inventory.abi_version), $($inventory.files.Count) files."
