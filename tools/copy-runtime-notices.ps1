param(
    [Parameter(Mandatory)][string]$Package,
    [Parameter(Mandatory)][string]$NuGetPackagesPath,
    [Parameter(Mandatory)][ValidateSet('win-x64', 'win-arm64')][string]$Platform
)
$ErrorActionPreference = 'Stop'

# Runtime versions come from each published executable, including detached helpers.
$entries = @(
    'FantasyTale.Launcher.runtimeconfig.json',
    'updater/FantasyTale.Launcher.Updater.runtimeconfig.json',
    'overlay/FantasyTale.Launcher.Overlay.runtimeconfig.json'
)
foreach ($entry in $entries) {
    $configuration = Join-Path $Package $entry
    $options = (Get-Content -LiteralPath $configuration -Raw | ConvertFrom-Json).runtimeOptions
    if (!$options.includedFrameworks) { throw "Expected a self-contained runtime configuration: $entry" }
    foreach ($framework in $options.includedFrameworks) {
        if ($framework.name -notin @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App') -or
            $framework.version -notmatch '^\d+\.\d+\.\d+$') {
            throw "Unsupported runtime identity in $entry"
        }
        $identity = "$($framework.name).Runtime.$Platform".ToLowerInvariant()
        $source = Join-Path (Join-Path $NuGetPackagesPath $identity) $framework.version
        $destination = Join-Path (Split-Path -Parent $configuration) "ThirdPartyLicenses/runtime/$($framework.name)-$($framework.version)"
        $noticeNames = if ($framework.name -eq 'Microsoft.NETCore.App') {
            @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')
        } else { @('LICENSE') }
        foreach ($name in $noticeNames) {
            $inputFile = Join-Path $source $name
            if (!(Test-Path -LiteralPath $inputFile -PathType Leaf) -or
                (Get-Item -LiteralPath $inputFile).Length -eq 0) {
                throw "Runtime redistribution notice missing: $identity/$($framework.version)/$name"
            }
        }
        New-Item -ItemType Directory -Force -Path $destination | Out-Null
        foreach ($name in $noticeNames) {
            Copy-Item -LiteralPath (Join-Path $source $name) -Destination $destination
        }
    }
}
