[CmdletBinding()]
param(
    [string]$CompilerPath = "D:\Inno Setup 6\ISCC.exe",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

$repositoryRoot = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot ".."))
$releaseRoot = Join-Path $repositoryRoot "build\pc-release"
$payloadDirectory = Join-Path $releaseRoot "SolisMonitor"
$manifestPath = Join-Path $releaseRoot "release-manifest.json"
$outputDirectory = Join-Path $repositoryRoot "build\installer"
$installerScript = Join-Path $repositoryRoot "installer\SolisMonitor.iss"
$setupIcon = Join-Path $repositoryRoot `
    "app\LibreHardwareMonitor\LibreHardwareMonitor\Resources\icon.ico"

if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot "Publish-PC.ps1")
    if ($LASTEXITCODE -ne 0) {
        throw "PC release publish failed with exit code $LASTEXITCODE."
    }
}

$requiredPaths = @(
    $CompilerPath,
    $payloadDirectory,
    $manifestPath,
    $installerScript,
    $setupIcon,
    (Join-Path $payloadDirectory "SolisMonitor.exe"),
    (Join-Path $payloadDirectory "LICENSE")
)
foreach ($path in $requiredPaths) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required installer input was not found: $path"
    }
}

$manifest = Get-Content -Raw -LiteralPath $manifestPath |
    ConvertFrom-Json
$displayVersion = [string]$manifest.version
if ($displayVersion -notmatch "^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)") {
    throw "Release manifest version is not compatible with the installer: $displayVersion"
}

$appVersion = "$($Matches.major).$($Matches.minor).$($Matches.patch)"
$versionInfoVersion = "$appVersion.0"

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
Get-ChildItem -LiteralPath $outputDirectory -File -Filter `
    "SolisMonitor-*-win-x64-setup.exe" |
    Remove-Item -Force

$compilerArguments = @(
    "/DAppVersion=$appVersion",
    "/DAppDisplayVersion=$displayVersion",
    "/DVersionInfoVersion=$versionInfoVersion",
    "/DSourceDir=$payloadDirectory",
    "/DOutputDir=$outputDirectory",
    "/DSetupIconFile=$setupIcon",
    $installerScript
)

& $CompilerPath @compilerArguments
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}

$installerPath = Join-Path $outputDirectory `
    "SolisMonitor-$appVersion-win-x64-setup.exe"
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Installer compiler did not create the expected file: $installerPath"
}

$installer = Get-Item -LiteralPath $installerPath
$installerHash = Get-FileHash -LiteralPath $installerPath -Algorithm SHA256
Write-Host "Solis Monitor installer created:"
Write-Host "  Path: $($installer.FullName)"
Write-Host "  Size: $($installer.Length) bytes"
Write-Host "  SHA-256: $($installerHash.Hash.ToLowerInvariant())"
