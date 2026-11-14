param(
    [Parameter(Mandatory)][string]$Output,
    [bool]$PreferIp = $true,
    [string]$BaseUrl = 'http://archive-cn.600819.xyz:5244/',
    [string]$SourceName = '',
    [ValidateSet('alist', 'http')][string]$Kind = 'alist',
    [ValidateSet('win-x64', 'win-arm64')][string]$Platform = 'win-x64',
    [string]$SourcePrefix = '',
    [string]$FeedPath = '',
    [string[]]$FeedAliases = @(),
    [string]$ManualDownloadUrl = '',
    [string]$GitHubReleaseRoot = 'https://github.com/Wasabi-FantasyTaleDev/FantasyTale.Launcher/releases/latest/download/',
    [string]$Version = '',
    [long]$Revision = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds(),
    [string]$NuGetPackagesPath = '',
    [string]$NuGetHttpCachePath = '',
    [string]$GatewayArtifacts = ''
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
[xml]$versionProperties = Get-Content -LiteralPath (Join-Path $repository 'Directory.Build.props') -Raw
$repositoryVersion = [string]$versionProperties.Project.PropertyGroup.Version
if ($Version -and $Version -cne $repositoryVersion) {
    throw 'Update Version in Directory.Build.props first. Package and local build versions must use the same value.'
}
$Version = $repositoryVersion
$deprecatedBeforeVersion = [string]$versionProperties.Project.PropertyGroup.LauncherDeprecatedBeforeVersion
if (!$GatewayArtifacts) { throw 'Supply the reviewed Gateway artifact directory before packaging this release.' }
& python (Join-Path $repository 'tools/package-gateway.py') --artifacts $GatewayArtifacts
if ($LASTEXITCODE -ne 0) { throw 'Gateway provenance verification failed before building the Launcher.' }
& (Join-Path $repository 'tools/package-native.ps1') -Configuration Release -BundledPluginsOnly
$destination = [IO.Path]::GetFullPath($Output, (Get-Location).Path)
if (Test-Path -LiteralPath $destination) { throw 'Use a new output directory to avoid stale release files.' }
$source = $null
if (![Uri]::TryCreate($BaseUrl, [UriKind]::Absolute, [ref]$source) -or
    $source.Scheme -notin @('http', 'https') -or $source.UserInfo -or $source.Query -or $source.Fragment) {
    throw 'The update base URL must be HTTP(S), without credentials, query or fragment.'
}
if ($Revision -lt 1) { throw 'The publication revision must be positive.' }
if ($Version -and $Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') {
    throw 'The package version must be a semantic version so the executable and index agree.'
}
if (!$SourcePrefix) { $SourcePrefix = "FantasyTale/launcher/$Platform" }
if (!$FeedPath) { $FeedPath = "FantasyTale/launcher/$Platform/current.toml" }
if (!$FeedAliases -and $FeedPath -eq "FantasyTale/launcher/$Platform/current.toml") {
    $FeedAliases = @("FantasyTale/launcher/$Platform/preview/current.toml")
}
if ($SourcePrefix -notmatch '^[a-zA-Z0-9_./-]+$' -or
    ($SourcePrefix.Split('/') | Where-Object { $_ -in @('', '.', '..') })) {
    throw 'The source prefix must contain safe relative path segments.'
}
if ($FeedPath -notmatch '^[a-zA-Z0-9_./-]+$' -or
    ($FeedPath.Split('/') | Where-Object { $_ -in @('', '.', '..') })) {
    throw 'The feed path must contain safe relative path segments.'
}
foreach ($alias in $FeedAliases) {
    if ($alias -notmatch '^[a-zA-Z0-9_./-]+$' -or
        ($alias.Split('/') | Where-Object { $_ -in @('', '.', '..', 'objects') }) -or
        $alias -eq $FeedPath) { throw 'Legacy aliases must be distinct safe index paths.' }
}
if (@($FeedAliases | Select-Object -Unique).Count -ne $FeedAliases.Count) { throw 'Duplicate legacy feed alias.' }
if (!$ManualDownloadUrl) {
    $ManualDownloadUrl = $source.AbsoluteUri.TrimEnd('/') + "/FantasyTale/launcher/$Platform/portable"
    if ($Kind -eq 'http') { $ManualDownloadUrl += "/FantasyTale.Launcher-$Platform.zip" }
}
$manualUri = $null
if (![Uri]::TryCreate($ManualDownloadUrl, [UriKind]::Absolute, [ref]$manualUri) -or
    $manualUri.Scheme -notin @('http', 'https') -or $manualUri.UserInfo) { throw 'Use an HTTP(S) manual-download page without credentials.' }
function Quote-Toml([string]$Value) { ConvertTo-Json -InputObject $Value -Compress }
function Write-Utf8([string]$Path, [string]$Value) { [IO.File]::WriteAllText($Path, $Value, [Text.UTF8Encoding]::new($false)) }
function Require-Success([string]$Step) { if ($LASTEXITCODE -ne 0) { throw "$Step failed with exit code $LASTEXITCODE." } }

try {
New-Item -ItemType Directory -Path $destination | Out-Null
$package = Join-Path $destination 'package'
$public = Join-Path $destination 'public'
$nugetPackagesWasSet = $false
$nugetHttpCacheWasSet = $false
$previousNuGetPackages = $env:NUGET_PACKAGES
$previousNuGetHttpCache = $env:NUGET_HTTP_CACHE_PATH
    # Keep restore metadata beside the disposable build output. This avoids a full
    # system volume taking down an otherwise valid release before publication.
    if (!$NuGetPackagesPath) { $NuGetPackagesPath = Join-Path $destination '.nuget-packages' }
    if (!$NuGetHttpCachePath) { $NuGetHttpCachePath = Join-Path $destination '.nuget-http-cache' }
    New-Item -ItemType Directory -Force -Path $NuGetPackagesPath, $NuGetHttpCachePath | Out-Null
    $env:NUGET_PACKAGES = [IO.Path]::GetFullPath($NuGetPackagesPath)
    $env:NUGET_HTTP_CACHE_PATH = [IO.Path]::GetFullPath($NuGetHttpCachePath)
    $nugetPackagesWasSet = $true
    $nugetHttpCacheWasSet = $true
# Release labels and object storage folders must not retarget installed launchers.
$feed = $FeedPath
Push-Location (Join-Path $repository 'web')
try {
    & npm run type-check
    Require-Success 'Frontend type check'
    & npm run build
    Require-Success 'Frontend build'
} finally { Pop-Location }
$publishArguments = @('publish', (Join-Path $repository 'src/FantasyTale.Launcher.Host'), '-c', 'Release', '-r', $Platform, '--self-contained', 'true', '-o', $package, '-m:1', '-nr:false')
# Rebuild project references even when another configuration previously populated their shared outputs.
$publishArguments += '-t:Rebuild'
& dotnet @publishArguments
Require-Success 'Launcher publication'
& python (Join-Path $repository 'tools/package-gateway.py') --artifacts $GatewayArtifacts --package $package
Require-Success 'Gateway bundling and full verification'
& (Join-Path $repository 'tools/copy-runtime-notices.ps1') -Package $package -NuGetPackagesPath $env:NUGET_PACKAGES -Platform $Platform
& (Join-Path $repository 'tools/verify-managed-package.ps1') -Package $package -ExpectedVersion $Version
& python (Join-Path $repository 'tools/verify-updater-package.py') $package --platform $Platform --complete
Require-Success 'Complete distribution and detached updater verification'
Write-Utf8 (Join-Path $package 'launcher-download.toml') ("schema = 1`nurl = $(Quote-Toml $manualUri.AbsoluteUri)`n")

# The baseline belongs to this distribution, not to the mutable remote feed.
$githubRoot = $null
if (![Uri]::TryCreate($GitHubReleaseRoot, [UriKind]::Absolute, [ref]$githubRoot) -or
    $githubRoot.Scheme -ne 'https' -or $githubRoot.UserInfo -or $githubRoot.Query -or $githubRoot.Fragment) {
    throw 'The GitHub release root must be HTTPS without credentials, query or fragment.'
}
$displaySourceName = if ($SourceName) { $SourceName } elseif ($source.Host -eq 'archive-cn.600819.xyz') { 'Fantasy Tale Archive' } else { $source.Host }
$githubFeed = "launcher-$Platform.toml"
$policy = "schema = 2`n[[sources]]`nname = $(Quote-Toml $displaySourceName)`nkind = $(Quote-Toml $Kind)`nbase_url = $(Quote-Toml $source.AbsoluteUri)`nfeed_path = $(Quote-Toml $feed)`nprefer_ip = $($PreferIp.ToString().ToLowerInvariant())`nminimum_revision = $Revision`n`n[[sources]]`nname = 'GitHub'`nkind = 'http'`nbase_url = $(Quote-Toml $githubRoot.AbsoluteUri)`nfeed_path = $(Quote-Toml $githubFeed)`nminimum_revision = $Revision`n"
Write-Utf8 (Join-Path $package 'launcher-update.toml') $policy
$config = "input = $(Quote-Toml $package)`noutput = $(Quote-Toml $public)`nplatform = $(Quote-Toml $Platform)`nversion = $(Quote-Toml $Version)`nrevision = $Revision`nfeed_path = $(Quote-Toml $feed)`nsource_prefix = $(Quote-Toml $SourcePrefix)`n"
$configuration = Join-Path $destination 'publish.toml'
if ($deprecatedBeforeVersion) {
    $config += "deprecated_before_version = $(Quote-Toml $deprecatedBeforeVersion)`n"
}
Write-Utf8 $configuration $config
& dotnet run --project (Join-Path $repository 'tools/publish-launcher') -c Release -- $configuration
Require-Success 'File index publication'
$index = Join-Path $public $feed
# GitHub release assets have a flat namespace; retain the same payload hashes and destinations.
$githubAssets = Join-Path $destination 'github-assets'
New-Item -ItemType Directory -Path $githubAssets | Out-Null
$githubIndex = Get-Content -LiteralPath $index -Raw
$githubIndex = [regex]::Replace($githubIndex, 'source_path\s*=\s*"[^"]*/objects/([a-f0-9]{64})"', 'source_path = "$1"')
Write-Utf8 (Join-Path $githubAssets $githubFeed) $githubIndex
Get-ChildItem -LiteralPath (Join-Path $public ($SourcePrefix + '/objects')) -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $githubAssets $_.Name)
}
foreach ($alias in $FeedAliases) {
    $aliasPath = Join-Path $public $alias
    New-Item -ItemType Directory -Path (Split-Path -Parent $aliasPath) -Force | Out-Null
    Copy-Item -LiteralPath $index -Destination $aliasPath
}
$archiveRelative = "FantasyTale/launcher/$Platform/portable/FantasyTale.Launcher-$Platform.zip"
$archive = Join-Path $public $archiveRelative
& python (Join-Path $repository 'tools/create-launcher-archive.py') $package $index $archive
Require-Success 'Manual-download archive'
$aliasRecord = @{ feed_path = $feed; aliases = @($FeedAliases); sha256 = (Get-FileHash -LiteralPath $index -Algorithm SHA256).Hash.ToLowerInvariant();
    manual_download = @{ path = $archiveRelative; sha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant(); size = (Get-Item -LiteralPath $archive).Length } }
Write-Utf8 (Join-Path $public '.fantasytale-feed-aliases.json') ($aliasRecord | ConvertTo-Json -Depth 3)
Write-Output "Distribution: $package"
Write-Output "Feed: $(Join-Path $public $feed)"
Write-Output "Baseline revision: $Revision"
} finally {
    if ($nugetPackagesWasSet) { $env:NUGET_PACKAGES = $previousNuGetPackages }
    if ($nugetHttpCacheWasSet) { $env:NUGET_HTTP_CACHE_PATH = $previousNuGetHttpCache }
}
