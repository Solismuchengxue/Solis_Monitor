$ErrorActionPreference = 'Stop'
$repositoryRoot = Join-Path $PSScriptRoot '..'
$firmwareRoot = Join-Path $PSScriptRoot '..\firmware'
$firmwareBuild = Join-Path $PSScriptRoot '..\build\firmware-verify'
$firmwareSdkconfig = Join-Path $firmwareBuild 'sdkconfig'
$desktopProject = Join-Path $PSScriptRoot '..\app\LibreHardwareMonitor\LibreHardwareMonitor\LibreHardwareMonitor.csproj'
$desktopMetricsTests = Join-Path $PSScriptRoot '..\app\tests\SolisMonitor.Metrics.SmokeTests\SolisMonitor.Metrics.SmokeTests.csproj'
& dotnet restore $desktopProject --disable-parallel -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& dotnet restore $desktopMetricsTests --disable-parallel -p:NuGetAudit=false -p:Platform=x64
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& dotnet build $desktopProject --configuration Release --no-restore -p:Platform=x64 -m:1 -p:BuildInParallel=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& dotnet build $desktopMetricsTests --configuration Release --no-restore -p:Platform=x64 -m:1 -p:BuildInParallel=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& dotnet run --project $desktopMetricsTests --configuration Release --no-build --no-restore -p:Platform=x64
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$python = Join-Path $PSScriptRoot '..\.venv\Scripts\python.exe'
if (-not (Test-Path -LiteralPath $python)) { throw 'Create .venv with Python 3.12 first.' }
if ([string]::IsNullOrWhiteSpace($env:IDF_PATH) -or -not (Test-Path -LiteralPath $env:IDF_PATH)) { throw 'Run this script from an ESP-IDF 6.0.2 environment: IDF_PATH is not set to a usable ESP-IDF directory.' }
if ([string]::IsNullOrWhiteSpace($env:IDF_PYTHON_ENV_PATH) -or -not (Test-Path -LiteralPath $env:IDF_PYTHON_ENV_PATH)) { throw 'Run this script from an ESP-IDF 6.0.2 environment: IDF_PYTHON_ENV_PATH is not set to a usable ESP-IDF Python environment.' }
$idfScript = Join-Path $env:IDF_PATH 'tools\idf.py'
if (-not (Test-Path -LiteralPath $idfScript)) { throw 'Run this script from an ESP-IDF 6.0.2 environment: IDF_PATH does not contain tools\idf.py.' }
$idfPython = Join-Path $env:IDF_PYTHON_ENV_PATH 'Scripts\python.exe'
if (-not (Test-Path -LiteralPath $idfPython)) { throw 'Run this script from an ESP-IDF 6.0.2 environment: IDF_PYTHON_ENV_PATH does not contain Scripts\python.exe.' }
Push-Location $repositoryRoot
try {
    & $python -m unittest discover -s tools\tests -p "test_*.py" -v
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
}
Push-Location $firmwareRoot
try {
    New-Item -ItemType Directory -Force -Path $firmwareBuild | Out-Null
    if (Test-Path -LiteralPath $firmwareSdkconfig) {
        Remove-Item -LiteralPath $firmwareSdkconfig -Force
    }
    & $idfPython $idfScript -B $firmwareBuild -D "SDKCONFIG=$firmwareSdkconfig" reconfigure
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & ninja -C $firmwareBuild -j1
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $idfPython $idfScript -B $firmwareBuild -D "SDKCONFIG=$firmwareSdkconfig" size
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
}
exit $LASTEXITCODE
