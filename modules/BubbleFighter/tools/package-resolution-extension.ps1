param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = Split-Path -Parent $PSScriptRoot
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repository 'artifacts/resolution-extension/package' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw "Output already exists: $output" }
$plugin = Join-Path $repository 'games/BubbleFighter/plugins/bubblefighter.fantasy.resolution-extension'
$binary = Join-Path $repository 'artifacts/resolution-extension/resolution_extension.dll'
if (!(Test-Path -LiteralPath $binary -PathType Leaf)) { throw 'Build resolution_extension.vcxproj first.' }
New-Item -ItemType Directory -Path $output | Out-Null
$package = Join-Path $output 'bubblefighter.fantasy.resolution-extension'
New-Item -ItemType Directory -Path $package | Out-Null
Copy-Item -LiteralPath (Join-Path $plugin 'plugin.toml') -Destination $package
Copy-Item -LiteralPath (Join-Path $plugin 'plugin-ui.toml') -Destination $package
Copy-Item -LiteralPath (Join-Path $plugin 'settings.html') -Destination $package
Copy-Item -LiteralPath (Join-Path $plugin 'builds') -Destination $package -Recurse
Copy-Item -LiteralPath $binary -Destination $package
Copy-Item -LiteralPath (Join-Path $repository 'LICENSE') -Destination $package
$manifest = [IO.File]::ReadAllText((Join-Path $plugin 'plugin.toml'))
$version = [regex]::Match($manifest, '(?m)^version\s*=\s*"([0-9A-Za-z.+-]+)"\s*$').Groups[1].Value
if (!$version) { throw 'The plugin version could not be read.' }
$archive = Join-Path $output "bubblefighter.fantasy.resolution-extension-$version-preview.zip"
# Windows PowerShell's Compress-Archive writes backslashes, which are not valid package paths.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::Open($archive, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $package -Recurse -File | Sort-Object FullName) {
        $relative = $file.FullName.Substring($package.Length + 1).Replace('\', '/')
        $entry = $zip.CreateEntry($relative, [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
        $input = [IO.File]::OpenRead($file.FullName)
        try {
            $destination = $entry.Open()
            try { $input.CopyTo($destination) } finally { $destination.Dispose() }
        } finally { $input.Dispose() }
    }
} finally { $zip.Dispose() }
Get-FileHash -LiteralPath $archive -Algorithm SHA256 | Select-Object Path, Hash
Write-Output 'Prepared a local optional-plugin preview. No store upload or game installation was performed.'
