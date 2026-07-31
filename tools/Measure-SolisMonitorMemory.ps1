<#
.SYNOPSIS
采样一个已经运行的 Solis Monitor 进程，不负责启动或结束该进程。

.EXAMPLE
$process = Get-Process SolisMonitor
.\tools\Measure-SolisMonitorMemory.ps1 -ProcessId $process.Id

.EXAMPLE
$process = Get-Process SolisMonitor
.\tools\Measure-SolisMonitorMemory.ps1 -ProcessId $process.Id `
    -DurationSeconds 3600 -IntervalSeconds 10 `
    -OutputPath .\build\diagnostics\solis-memory.csv
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(1, [int]::MaxValue)]
    [int]$ProcessId,

    [ValidateRange(0, 86400)]
    [int]$DurationSeconds = 60,

    [ValidateRange(1, 3600)]
    [int]$IntervalSeconds = 5,

    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-SolisProcessSample {
    param(
        [int]$Id,
        [datetime]$StartedAt
    )

    $process = Get-Process -Id $Id -ErrorAction Stop
    [pscustomobject]@{
        Timestamp       = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
        ElapsedSeconds  = [math]::Round(((Get-Date) - $StartedAt).TotalSeconds, 1)
        WorkingSetMB    = [math]::Round($process.WorkingSet64 / 1MB, 1)
        PrivateMemoryMB = [math]::Round($process.PrivateMemorySize64 / 1MB, 1)
        HandleCount     = $process.HandleCount
        ThreadCount     = $process.Threads.Count
    }
}

$startedAt = Get-Date
$samples = [System.Collections.Generic.List[object]]::new()

do {
    try {
        $samples.Add((Get-SolisProcessSample -Id $ProcessId -StartedAt $startedAt))
    }
    catch [Microsoft.PowerShell.Commands.ProcessCommandException] {
        throw "进程 $ProcessId 已退出或无法读取。"
    }

    $elapsed = ((Get-Date) - $startedAt).TotalSeconds
    if ($elapsed -ge $DurationSeconds) {
        break
    }

    $remaining = $DurationSeconds - $elapsed
    $sleepSeconds = [math]::Min($IntervalSeconds, $remaining)
    if ($sleepSeconds -gt 0) {
        Start-Sleep -Milliseconds ([int]($sleepSeconds * 1000))
    }
} while ($true)

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
    $outputDirectory = Split-Path -Parent $resolvedOutput
    if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
        New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    }
    $samples | Export-Csv -LiteralPath $resolvedOutput -NoTypeInformation -Encoding UTF8
}

$samples

$first = $samples[0]
$last = $samples[$samples.Count - 1]
[pscustomobject]@{
    Summary              = "Solis Monitor memory sampling"
    Samples              = $samples.Count
    DurationSeconds      = $last.ElapsedSeconds
    WorkingSetStartMB    = $first.WorkingSetMB
    WorkingSetEndMB      = $last.WorkingSetMB
    WorkingSetDeltaMB    = [math]::Round($last.WorkingSetMB - $first.WorkingSetMB, 1)
    PrivateMemoryStartMB = $first.PrivateMemoryMB
    PrivateMemoryEndMB   = $last.PrivateMemoryMB
    PrivateMemoryDeltaMB = [math]::Round($last.PrivateMemoryMB - $first.PrivateMemoryMB, 1)
    HandleCountStart     = $first.HandleCount
    HandleCountEnd       = $last.HandleCount
    ThreadCountStart     = $first.ThreadCount
    ThreadCountEnd       = $last.ThreadCount
}
