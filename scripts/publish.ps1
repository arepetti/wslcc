#Requires -Version 7.0
<#
.SYNOPSIS
    Publish wslcc, package the portable ZIP, cut a GitHub Release, and submit to winget.

.DESCRIPTION
    One script for both the first release and every release after it. It runs a pipeline
    of phases; pass -Phase to run only some of them.

      Publish  dotnet publish wslccd + wslcc side-by-side (self-contained, win-x64).
      Package  zip the publish folder into dist/wslcc-<version>-<rid>.zip and print its SHA-256.
      Release  create (or update) the GitHub Release v<version> and upload the zip (needs gh).
      Winget   render the winget manifests from packaging/winget, validate them, and
               (if a token is available) open/refresh the microsoft/winget-pkgs PR.

    With no -Phase, all four run in order. The version defaults to <VersionPrefix> in
    Directory.Build.props, so a normal release is just:  ./scripts/publish.ps1

.PARAMETER Version
    Package version (e.g. 0.1.0). Defaults to VersionPrefix from Directory.Build.props.

.PARAMETER Runtime
    .NET runtime identifier to publish. Defaults to win-x64.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER Repo
    owner/name of the GitHub repository the release is created on. Defaults to arepetti/wslcc.

.PARAMETER Phase
    Subset of Publish, Package, Release, Winget (or All). Runs in canonical order regardless
    of the order given. Defaults to All.

.PARAMETER WingetToken
    GitHub PAT (classic, able to fork/PR microsoft/winget-pkgs) used by `wingetcreate submit`.
    Defaults to $env:WINGET_TOKEN. If absent, the Winget phase renders + validates the manifests
    and prints the submit command instead of running it.

.PARAMETER DryRun
    Print the mutating commands (dotnet publish, gh, wingetcreate, ...) without running them.

.EXAMPLE
    ./scripts/publish.ps1
    Full first-time or subsequent release for the version in Directory.Build.props.

.EXAMPLE
    ./scripts/publish.ps1 -Phase Publish,Package
    Just build the self-contained artifact and the zip (what CI does before uploading it).

.EXAMPLE
    ./scripts/publish.ps1 -Version 0.2 -Phase Winget -WingetToken $env:MY_PAT
    Re-render manifests for 0.2 and submit the winget PR.
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release',
    [string]$Repo = 'arepetti/wslcc',
    [string[]]$Phase = @('All'),
    [string]$WingetToken = $env:WINGET_TOKEN,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Write-Section([string]$Message) {
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-Native {
    param([Parameter(Mandatory)][string]$File, [string[]]$Arguments)
    Write-Host "    $File $($Arguments -join ' ')" -ForegroundColor DarkGray
    if ($DryRun) { return }
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "'$File' exited with code $LASTEXITCODE."
    }
}

function Test-Command([string]$Name) {
    return [bool](Get-Command $Name -ErrorAction SilentlyContinue)
}

# --- Resolve version ---------------------------------------------------------
if (-not $Version) {
    $propsPath = Join-Path $RepoRoot 'Directory.Build.props'
    $propsText = Get-Content -Raw -LiteralPath $propsPath
    if ($propsText -match '<VersionPrefix>\s*([^<]+?)\s*</VersionPrefix>') {
        $Version = $Matches[1].Trim()
    }
    else {
        throw "Could not read <VersionPrefix> from $propsPath; pass -Version explicitly."
    }
}

$publishDir = Join-Path $RepoRoot "publish/$Runtime"
$distDir = Join-Path $RepoRoot 'dist'
$zipName = "wslcc-$Version-$Runtime.zip"
$zipPath = Join-Path $distDir $zipName
$tag = "v$Version"
$cliExe = Join-Path $publishDir 'wslcc.exe'

# Accept both `-Phase Publish Package` (array) and `-Phase Publish,Package` (single token,
# e.g. when the script is invoked with pwsh -File).
$requested = @($Phase | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$allPhases = @('Publish', 'Package', 'Release', 'Winget')
$valid = @('All') + $allPhases
$unknown = $requested | Where-Object { $_ -notin $valid }
if ($unknown) {
    throw "Unknown -Phase value(s): $($unknown -join ', '). Valid: $($valid -join ', ')."
}
$selected = if ($requested -contains 'All') { $allPhases } else { $allPhases | Where-Object { $requested -contains $_ } }

Write-Host "wslcc publish" -ForegroundColor Green
Write-Host "  version : $Version"
Write-Host "  runtime : $Runtime ($Configuration)"
Write-Host "  repo    : $Repo"
Write-Host "  phases  : $($selected -join ', ')$(if ($DryRun) { '  [dry-run]' })"

# --- Publish -----------------------------------------------------------------
if ($selected -contains 'Publish') {
    Write-Section "Publish (self-contained $Runtime)"
    if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
    New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

    $common = @(
        '-c', $Configuration,
        '-r', $Runtime,
        '--self-contained', 'true',
        '-p:PublishSingleFile=false',
        '-o', $publishDir
    )

    # Daemon first, CLI second: both land in the same folder so wslcc.exe and wslccd.exe
    # sit side by side and share one runtime (DaemonLocator finds wslccd next to wslcc).
    Invoke-Native 'dotnet' (@('publish', (Join-Path $RepoRoot 'src/Wslccd/Wslccd.csproj')) + $common)
    Invoke-Native 'dotnet' (@('publish', (Join-Path $RepoRoot 'src/Wslcc.Cli/Wslcc.Cli.csproj')) + $common)
    Write-Host "    -> $publishDir"
}

# --- Package -----------------------------------------------------------------
if ($selected -contains 'Package') {
    Write-Section 'Package (portable ZIP)'
    if (-not $DryRun -and -not (Test-Path $cliExe)) {
        throw "Publish output not found at $cliExe. Run with -Phase Publish (or All) first."
    }
    New-Item -ItemType Directory -Force -Path $distDir | Out-Null
    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
    if (-not $DryRun) {
        Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
    }
    Write-Host "    $zipPath" -ForegroundColor DarkGray
    if (Test-Path $zipPath) {
        $sha = (Get-FileHash $zipPath -Algorithm SHA256).Hash
        Write-Host "    SHA256: $sha"
    }
}

# --- Release -----------------------------------------------------------------
if ($selected -contains 'Release') {
    Write-Section "GitHub Release $tag"
    if (-not (Test-Path $zipPath) -and -not $DryRun) {
        throw "$zipPath not found. Run with -Phase Package (or All) first."
    }
    if (-not (Test-Command 'gh')) {
        throw "GitHub CLI (gh) not found. Install it (winget install GitHub.cli) and run 'gh auth login'."
    }

    $releaseExists = $false
    if (-not $DryRun) {
        & gh release view $tag --repo $Repo *> $null
        $releaseExists = ($LASTEXITCODE -eq 0)
    }

    if ($releaseExists) {
        Write-Host "    Release $tag exists; uploading asset (clobber)." -ForegroundColor DarkGray
        Invoke-Native 'gh' @('release', 'upload', $tag, $zipPath, '--repo', $Repo, '--clobber')
    }
    else {
        Invoke-Native 'gh' @('release', 'create', $tag, $zipPath, '--repo', $Repo, '--title', "wslcc $Version", '--generate-notes')
    }
}

# --- Winget ------------------------------------------------------------------
if ($selected -contains 'Winget') {
    Write-Section 'winget manifests'
    if (-not (Test-Path $zipPath)) {
        throw "$zipPath not found. Run with -Phase Package (or All) first so the installer SHA-256 can be computed."
    }

    $sha = (Get-FileHash $zipPath -Algorithm SHA256).Hash
    $templateDir = Join-Path $RepoRoot 'packaging/winget'
    $outDir = Join-Path $distDir "winget/$Version"

    if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    Get-ChildItem -Path $templateDir -Filter '*.yaml' | ForEach-Object {
        $rendered = (Get-Content -Raw -LiteralPath $_.FullName).
            Replace('__VERSION__', $Version).
            Replace('__SHA256__', $sha)
        Set-Content -LiteralPath (Join-Path $outDir $_.Name) -Value $rendered -NoNewline -Encoding utf8
    }
    Write-Host "    rendered manifests -> $outDir"

    if (Test-Command 'winget') {
        Invoke-Native 'winget' @('validate', '--manifest', $outDir)
    }
    else {
        Write-Host "    (winget not found; skipping local validate)" -ForegroundColor Yellow
    }

    if ($WingetToken) {
        if (-not (Test-Command 'wingetcreate')) {
            throw "wingetcreate not found. Install it (winget install Microsoft.WingetCreate) or use the release.yml winget job."
        }
        Invoke-Native 'wingetcreate' @('submit', '--token', $WingetToken, $outDir)
    }
    else {
        Write-Host ''
        Write-Host "    No token (WINGET_TOKEN). Manifests are ready at:" -ForegroundColor Yellow
        Write-Host "        $outDir"
        Write-Host "    Submit them with:"
        Write-Host "        wingetcreate submit --token <PAT> `"$outDir`""
    }
}

Write-Section 'Done'
