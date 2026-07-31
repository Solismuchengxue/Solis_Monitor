[CmdletBinding()]
param(
    [string]$ReleaseDirectory = "build\pc-release"
)

$ErrorActionPreference = "Stop"

$repositoryRoot = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot ".."))
$buildRoot = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot "build"))
$releaseRoot = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot $ReleaseDirectory))

$buildPrefix = $buildRoot.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar
if (-not $releaseRoot.StartsWith(
        $buildPrefix,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "ReleaseDirectory must stay inside the repository build directory."
}

if (Test-Path -LiteralPath $releaseRoot) {
    Remove-Item -LiteralPath $releaseRoot -Recurse -Force
}

$payloadDirectory = Join-Path $releaseRoot "SolisMonitor"
New-Item -ItemType Directory -Path $payloadDirectory -Force | Out-Null

$project = Join-Path $repositoryRoot `
    "app\LibreHardwareMonitor\LibreHardwareMonitor\LibreHardwareMonitor.csproj"
$publishArguments = @(
    "publish",
    $project,
    "--configuration", "Release",
    "--framework", "net10.0-windows",
    "--runtime", "win-x64",
    "--self-contained", "false",
    "--output", $payloadDirectory,
    "-m:1",
    "-p:Platform=x64",
    "-p:BuildInParallel=false",
    "-p:NuGetAudit=false"
)

& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$legalFiles = @(
    "LICENSE",
    "THIRD-PARTY-NOTICES.txt"
)
foreach ($fileName in $legalFiles) {
    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot "app\LibreHardwareMonitor\$fileName"
    ) -Destination (Join-Path $payloadDirectory $fileName)
}

Get-ChildItem -LiteralPath $payloadDirectory -Recurse -File -Filter "*.pdb" |
    Remove-Item -Force

$requiredFiles = @(
    "SolisMonitor.exe",
    "SolisMonitor.dll",
    "LibreHardwareMonitorLib.dll",
    "Aga.Controls.dll",
    "LICENSE",
    "THIRD-PARTY-NOTICES.txt",
    "NotificationHost\SolisMonitor.NotificationHost.exe"
)
foreach ($relativePath in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (
            Join-Path $payloadDirectory $relativePath))) {
        throw "Published payload is missing required file: $relativePath"
    }
}

$forbiddenNames = @(
    "SolisMonitor.config",
    "LibreHardwareMonitor.config",
    "settings.json",
    "weather.json"
)
$forbiddenFiles = Get-ChildItem -LiteralPath $payloadDirectory -Recurse -File |
    Where-Object { $forbiddenNames -contains $_.Name }
if ($forbiddenFiles) {
    $paths = ($forbiddenFiles.FullName -join [Environment]::NewLine)
    throw "Published payload contains user configuration files:$([Environment]::NewLine)$paths"
}

$payloadPrefix = $payloadDirectory.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar
$payloadFiles = @(
    Get-ChildItem -LiteralPath $payloadDirectory -Recurse -File
)
$manifestFiles = $payloadFiles |
    ForEach-Object {
        [ordered]@{
            path = $_.FullName.Substring($payloadPrefix.Length).
                Replace([IO.Path]::DirectorySeparatorChar, "/")
            size = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).
                Hash.ToLowerInvariant()
        }
    } |
    Sort-Object path

$executable = Join-Path $payloadDirectory "SolisMonitor.exe"
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo(
    $executable).ProductVersion
$payloadSize = ($payloadFiles |
    Measure-Object -Property Length -Sum).Sum
$manifest = [ordered]@{
    product = "Solis Monitor"
    version = $version
    architecture = "x64"
    framework = "net10.0-windows"
    self_contained = $false
    payload_file_count = @($manifestFiles).Count
    payload_size = $payloadSize
    files = @($manifestFiles)
}
$manifestPath = Join-Path $releaseRoot "release-manifest.json"
$manifest |
    ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM

Write-Host "PC release payload created:"
Write-Host "  Payload:  $payloadDirectory"
Write-Host "  Manifest: $manifestPath"
Write-Host "  Files:    $($manifest.payload_file_count)"
Write-Host "  Bytes:    $($manifest.payload_size)"
