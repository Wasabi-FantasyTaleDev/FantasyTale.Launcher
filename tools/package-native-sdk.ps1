[CmdletBinding()]
param([string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = Split-Path -Parent $PSScriptRoot
$configurationText = [IO.File]::ReadAllText((Join-Path $repository 'native/Native.Configuration.props'))
$versionMatch = [regex]::Match($configurationText, '(?m)<FtHookVersion>([^<]+)</FtHookVersion>')
$version = if ($versionMatch.Success) { $versionMatch.Groups[1].Value.Trim() } else { $null }
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'The native runtime version is invalid.' }
$header = 'native/runtime/include/fthook/plugin_abi.h'
$abiMatch = [regex]::Match([IO.File]::ReadAllText((Join-Path $repository $header)), '(?m)^#define FTHOOK_ABI_VERSION ([0-9]+)u\r?$')
if (!$abiMatch.Success) { throw 'The native ABI version is missing.' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repository "artifacts/native-sdk/fthook-sdk-$version" }
$destination = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $destination) { throw 'The SDK output path already exists; choose a new output directory.' }

$files = [ordered]@{
    'include/fthook/plugin_abi.h' = $header
    'LICENSE' = 'LICENSE'
    'README.md' = 'native/sdk/README.md'
    'README.zh.md' = 'native/sdk/README.zh.md'
    'sample/sample.c' = 'native/samples/sample.locator/sample.c'
    'sample/sample.def' = 'native/samples/sample.locator/sample.def'
    'sample/sample_locator.vcxproj' = 'native/sdk/sample_locator.vcxproj'
}
foreach ($source in $files.Values) {
    if (!(Test-Path -LiteralPath (Join-Path $repository $source) -PathType Leaf)) {
        throw "Missing SDK input: $source"
    }
}

# Stage beside the destination so incomplete exports never appear as completed SDKs.
$staging = "$destination.staging-$([Guid]::NewGuid().ToString('N'))"
try {
    New-Item -ItemType Directory -Path $staging -Force | Out-Null
    $inventory = foreach ($entry in $files.GetEnumerator()) {
        $target = Join-Path $staging $entry.Key
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $repository $entry.Value) -Destination $target
        [ordered]@{
            path = $entry.Key
            size = (Get-Item -LiteralPath $target).Length
            sha256 = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    $receipt = [ordered]@{ schema = 1; runtime_version = $version; abi_version = [int]$abiMatch.Groups[1].Value; files = @($inventory) }
    [IO.File]::WriteAllText((Join-Path $staging 'sdk.json'), ($receipt | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $staging -Destination $destination
} finally {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
}
Write-Output "Exported native SDK $version (ABI $($receipt.abi_version)) to $destination"
