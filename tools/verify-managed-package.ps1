param([Parameter(Mandatory)][string]$Package, [string]$ExpectedVersion = '')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($Package)
$hostAssembly = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $root 'FantasyTale.Launcher.dll'))
$expected = $hostAssembly.Version
$versionedFiles = @('FantasyTale.Launcher')
$required = @('FantasyTale.Launcher.Sdk', 'FantasyTale.Launcher.Core',
    'FantasyTale.Launcher.Downloads', 'FantasyTale.Launcher.Communication',
    'FantasyTale.Launcher.Games.BubbleFighter', 'updater/FantasyTale.Launcher.Updater',
    'overlay/FantasyTale.Launcher.Overlay', 'overlay/FantasyTale.Launcher.Sdk',
    'updater/FantasyTale.Launcher.Core', 'updater/FantasyTale.Launcher.Downloads',
    'updater/FantasyTale.Launcher.Sdk')
foreach ($name in $required) {
    $path = Join-Path $root ($name + '.dll')
    $assembly = [Reflection.AssemblyName]::GetAssemblyName($path)
    if ($assembly.Version -ne $expected) {
        throw "Mixed release assemblies: $name is $($assembly.Version), expected $expected. Rebuild the distribution before publication."
    }
}
if ($ExpectedVersion) {
    foreach ($name in $versionedFiles + $required) {
        $path = Join-Path $root ($name + '.dll')
        $actual = [Diagnostics.FileVersionInfo]::GetVersionInfo($path).ProductVersion.Split('+')[0]
        if ($actual -cne $ExpectedVersion) {
            throw "Incorrect release label: $name is $actual, expected $ExpectedVersion from Directory.Build.props."
        }
    }
}
Write-Output "PASS managed distribution: all release assemblies match $expected"
