# Background Collection Reliability Implementation Plan

> **历史状态（2026-08-08）：已执行完成。** Task 1–6 已实现、验证并合并至 `main`；发布基线 `808648b` 已完成 D 盘部署验收，并以 `v0.9.6-beta.4` 公开为 Pre-release。下方未勾选框保留原始执行计划语法，不代表当前待办；当前状态以根目录 `TODO.md` 为准。

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent recoverable metrics and weather collection exceptions from terminating Solis Monitor while preserving the last complete snapshot, exposing recoverable diagnostics, and keeping sanitized error evidence within fixed disk and memory bounds.

**Architecture:** Add a small `BackgroundCollectionGuard` that runs one collection operation and reports recoverable failures, plus a bounded `RuntimeErrorLog` that stores only a fixed failure record. Wire the guard only into the existing metrics and weather timer callbacks, reuse metrics freshness diagnostics, add a weather failure transition, and harden the remaining Codex session metadata JSON boundary.

**Tech Stack:** C# 13 / .NET 10 WPF, existing custom smoke-test executable, PowerShell 7, Python 3.12, ESP-IDF 6.0.2.

## Global Constraints

- Work from `F:\30_Product_and_Engineering\Solis_Monitor` on `main`.
- The approved design is `docs/superpowers/specs/2026-08-08-background-collection-reliability-design.md`.
- Do not add NuGet, Python, ESP-IDF, logging, or system dependencies.
- Catch only recoverable exceptions inside metrics and weather collection callbacks; do not add a global exception handler.
- Do not change Timer periods, device protocol, firmware, UI layout, notifications, or user configuration schema.
- Preserve the last complete metrics and weather values; never publish a partial metrics cycle.
- Log only UTC time, fixed module, exception type, and HResult. Never log exception messages, stacks, source data, credentials, tokens, Wi-Fi values, or complete paths.
- Keep `runtime-errors.log` and one backup at 512 KiB each; keep only two fixed in-memory throttle slots.
- Identical module and exception type pairs may be written at most once every five minutes.
- Logging failure is best effort and must not terminate collection or suppress the diagnostic callback.
- `OutOfMemoryException` and `AccessViolationException` must remain visible to the process and must not be treated as recoverable.
- Implementation, each commit, D-drive deployment, push, and GitHub Release work remain separate approval gates during execution.

## File Map

- Create `app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Diagnostics/BackgroundCollectionGuard.cs`: failure value, fixed module enum, recoverable-exception filter, and one-operation guard.
- Create `app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Diagnostics/RuntimeErrorLog.cs`: sanitized record formatting, two-slot throttling, bounded rotation, and best-effort file I/O.
- Create `app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Reliability.cs`: behavioral tests for the guard and log.
- Modify `app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Diagnostics/SolisDiagnosticsMonitor.cs`: weather background-failure transition and message mapping.
- Modify `app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/SolisRuntime.cs`: construct the reliability components and wrap only the two timer bodies.
- Modify `app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Codex/CodexLocalWeeklyUsageReader.cs`: validate the session root and payload value kinds.
- Modify `app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Diagnostics.cs`: weather failure and recovery regression.
- Modify `app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Codex.cs`: primitive/null session metadata regression.
- Modify `app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Runner.cs`: register every new test explicitly.
- Modify `docs/DESKTOP_APP.md`: document the background recovery and bounded local log.

---

### Task 1: Isolate recoverable collection failures

**Files:**
- Create: `app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Diagnostics/BackgroundCollectionGuard.cs`
- Create: `app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Reliability.cs`
- Modify: `app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Runner.cs`

**Interfaces:**
- Produces: `BackgroundCollectionModule` with exactly `Metrics` and `Weather`.
- Produces: `BackgroundCollectionFailure(BackgroundCollectionModule Module, string ExceptionType, int HResult)`.
- Produces: `BackgroundCollectionGuard(Action<BackgroundCollectionFailure, DateTimeOffset> writeFailure)`.
- Produces: `bool Execute(BackgroundCollectionModule module, DateTimeOffset now, Action operation, Action<BackgroundCollectionFailure> onFailure)`.

- [ ] **Step 1: Write four failing guard tests**

Register these methods in `SmokeTests.Runner.cs` and implement them in the new partial test file:

~~~csharp
static void BackgroundCollectionGuardIsolatesRecoverableFailure()
{
    BackgroundCollectionFailure? written = null;
    BackgroundCollectionFailure? observed = null;
    var guard = new BackgroundCollectionGuard((failure, _) => written = failure);

    bool completed = guard.Execute(
        BackgroundCollectionModule.Metrics,
        DateTimeOffset.Parse("2026-08-08T12:00:00Z"),
        () => throw new InvalidOperationException("secret-path"),
        failure => observed = failure);

    True(!completed, "普通采集异常不应被报告为成功");
    True(written is not null, "普通采集异常没有写入脱敏记录");
    Equal(written, observed, "日志和诊断收到的故障记录不一致");
    Equal(typeof(InvalidOperationException).FullName!, written!.ExceptionType,
        "故障记录没有保存异常类型");
}

static void BackgroundCollectionGuardContinuesAfterFailure()
{
    var guard = new BackgroundCollectionGuard((_, _) => { });
    int successfulRuns = 0;

    guard.Execute(BackgroundCollectionModule.Metrics, DateTimeOffset.UtcNow,
        () => throw new InvalidOperationException(), _ => { });
    bool completed = guard.Execute(BackgroundCollectionModule.Metrics,
        DateTimeOffset.UtcNow.AddSeconds(1), () => successfulRuns++, _ => { });

    True(completed, "故障后的下一次采集没有恢复执行");
    Equal(1, successfulRuns, "恢复周期没有执行一次完整采集");
}

static void BackgroundCollectionGuardPropagatesFatalFailures()
{
    var guard = new BackgroundCollectionGuard((_, _) => { });
    foreach (Exception fatal in new Exception[]
             { new OutOfMemoryException(), new AccessViolationException() })
    {
        bool propagated = false;
        try
        {
            guard.Execute(BackgroundCollectionModule.Weather,
                DateTimeOffset.UtcNow, () => throw fatal, _ => { });
        }
        catch (Exception exception)
        {
            propagated = ReferenceEquals(fatal, exception);
        }

        True(propagated, $"严重异常被错误隔离：{fatal.GetType().Name}");
    }
}

static void BackgroundCollectionGuardSurvivesLogFailure()
{
    int diagnostics = 0;
    var guard = new BackgroundCollectionGuard((_, _) => throw new IOException());

    bool completed = guard.Execute(BackgroundCollectionModule.Weather,
        DateTimeOffset.UtcNow,
        () => throw new InvalidOperationException(),
        _ => diagnostics++);

    True(!completed, "采集异常不应返回成功");
    Equal(1, diagnostics, "日志失败阻断了诊断故障回调");
}
~~~

- [ ] **Step 2: Run the smoke suite and verify RED**

Run:

~~~powershell
dotnet run --project .\app\tests\SolisMonitor.Metrics.SmokeTests\SolisMonitor.Metrics.SmokeTests.csproj --configuration Release -p:Platform=x64 --no-restore
~~~

Expected: build fails because `BackgroundCollectionGuard`, `BackgroundCollectionFailure`, and `BackgroundCollectionModule` do not exist.

- [ ] **Step 3: Implement the minimal guard**

Create the production file with this shape:

~~~csharp
#nullable enable

using System;

namespace LibreHardwareMonitor.Solis.Diagnostics;

public enum BackgroundCollectionModule
{
    Metrics,
    Weather
}

public sealed record BackgroundCollectionFailure(
    BackgroundCollectionModule Module,
    string ExceptionType,
    int HResult);

public sealed class BackgroundCollectionGuard
{
    private readonly Action<BackgroundCollectionFailure, DateTimeOffset> _writeFailure;

    public BackgroundCollectionGuard(
        Action<BackgroundCollectionFailure, DateTimeOffset> writeFailure) =>
        _writeFailure = writeFailure ?? throw new ArgumentNullException(nameof(writeFailure));

    public bool Execute(
        BackgroundCollectionModule module,
        DateTimeOffset now,
        Action operation,
        Action<BackgroundCollectionFailure> onFailure)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(onFailure);
        try
        {
            operation();
            return true;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            var failure = new BackgroundCollectionFailure(
                module,
                exception.GetType().FullName ?? exception.GetType().Name,
                exception.HResult);
            try
            {
                _writeFailure(failure, now);
            }
            catch (Exception logException) when (IsRecoverable(logException))
            {
            }

            onFailure(failure);
            return false;
        }
    }

    private static bool IsRecoverable(Exception exception) =>
        exception is not OutOfMemoryException and not AccessViolationException;
}
~~~

- [ ] **Step 4: Run the smoke suite and verify GREEN**

Run the command from Step 2.

Expected: all existing tests plus the four guard tests pass.

- [ ] **Step 5: Review and request the Task 1 commit gate**

Run:

~~~powershell
git diff --check
git diff -- app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Diagnostics/BackgroundCollectionGuard.cs app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Reliability.cs app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Runner.cs
~~~

After explicit commit approval, commit only these files with message `增加后台采集异常保护器`.

---

### Task 2: Add a sanitized, bounded runtime error log

**Files:**
- Create: `app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Diagnostics/RuntimeErrorLog.cs`
- Modify: `app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Reliability.cs`
- Modify: `app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Runner.cs`

**Interfaces:**
- Consumes: `BackgroundCollectionFailure` and `BackgroundCollectionModule` from Task 1.
- Produces: `RuntimeErrorLog(string settingsDirectory, int maximumFileBytes = 524288, TimeSpan? minimumInterval = null)`.
- Produces: `void TryWrite(BackgroundCollectionFailure failure, DateTimeOffset now)`.
- Produces: `string LogPath` for diagnostics and deterministic tests.

- [ ] **Step 1: Write failing log tests**

Add runner entries and tests for duplicate rate limiting, bounded redacted rotation, and unavailable storage. Use temporary directories and a small injected maximum size.

~~~csharp
static void RuntimeErrorLogRateLimitsDuplicateFailures()
{
    string root = Path.Combine(Path.GetTempPath(), $"SolisMonitor.Log-{Guid.NewGuid():N}");
    try
    {
        var log = new RuntimeErrorLog(root, maximumFileBytes: 512,
            minimumInterval: TimeSpan.FromMinutes(5));
        var failure = new BackgroundCollectionFailure(
            BackgroundCollectionModule.Metrics,
            typeof(InvalidOperationException).FullName!,
            unchecked((int)0x80131509));
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-08T12:00:00Z");

        log.TryWrite(failure, now);
        log.TryWrite(failure, now.AddMinutes(4));
        log.TryWrite(failure, now.AddMinutes(5));

        Equal(2, File.ReadAllLines(log.LogPath).Length,
            "五分钟内重复故障没有被限流");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
~~~

The bounded/redacted test must write records at six-minute intervals until rotation occurs, then assert:

~~~csharp
True(File.Exists(log.LogPath), "当前运行时日志不存在");
True(File.Exists(log.LogPath + ".1"), "运行时日志没有轮转唯一备份");
True(new FileInfo(log.LogPath).Length <= 512, "当前日志超过测试硬上限");
True(new FileInfo(log.LogPath + ".1").Length <= 512, "备份日志超过测试硬上限");
string combined = File.ReadAllText(log.LogPath) + File.ReadAllText(log.LogPath + ".1");
True(!combined.Contains("secret-path", StringComparison.Ordinal), "日志泄露异常消息");
True(!combined.Contains(" at ", StringComparison.Ordinal), "日志泄露异常堆栈");
~~~

The unavailable-storage test must create a regular file where the settings directory is expected and verify `TryWrite` does not throw.

- [ ] **Step 2: Run the smoke suite and verify RED**

Run the Task 1 smoke command.

Expected: build fails because `RuntimeErrorLog` does not exist.

- [ ] **Step 3: Implement fixed-state throttling and bounded rotation**

Use exactly two throttle slots:

~~~csharp
private const int ModuleCount = 2;
private const int MaximumExceptionTypeLength = 160;
private readonly object _sync = new();
private readonly ThrottleState[] _states = new ThrottleState[ModuleCount];
~~~

Format one ASCII-safe line using `metrics` or `weather`, a CR/LF-stripped exception type truncated to 160 characters, and `unchecked((uint)failure.HResult):X8`. Rotate before append when the current length plus encoded line length exceeds `_maximumFileBytes`. Delete the old `.1` before moving the current file. Enclose directory creation, inspection, rotation, and append in a recoverable-exception filter.

Do not keep a stream open. Do not add a queue, timer, dictionary, dependency, exception message, or stack trace.

- [ ] **Step 4: Run the smoke suite and verify GREEN**

Expected: all tests pass and each test removes its temporary directory in `finally`.

- [ ] **Step 5: Review and request the Task 2 commit gate**

Run `git diff --check` and inspect only Task 2 files. After explicit approval, commit with message `增加有界脱敏运行时错误日志`.

---

### Task 3: Wire guard and diagnostics into the two timers

**Files:**
- Modify: `app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/SolisRuntime.cs:21-97,286-348`
- Modify: `app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Diagnostics/SolisDiagnosticsMonitor.cs:58-183,245-263`
- Modify: `app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Diagnostics.cs`
- Modify: `app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Reliability.cs`
- Modify: `app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Runner.cs`

**Interfaces:**
- Consumes: `BackgroundCollectionGuard` and `RuntimeErrorLog` from Tasks 1-2.
- Produces: `void ObserveWeatherCollectionFailure(DateTimeOffset now)`.
- Preserves: all existing public `SolisRuntime` constructors and timer periods.

- [ ] **Step 1: Write the weather failure/recovery test**

~~~csharp
static void DiagnosticsRecoverFromWeatherCollectionFailure()
{
    var monitor = new SolisDiagnosticsMonitor();
    DateTimeOffset healthyAt = DateTimeOffset.Parse("2026-08-08T12:00:00Z");
    monitor.ObserveWeather(
        new WeatherMetricsReading(true, "大连", "晴", 25, 30, null), healthyAt);

    monitor.ObserveWeatherCollectionFailure(healthyAt.AddMinutes(1));
    SolisDiagnosticsSnapshot failed = monitor.Current;
    Equal(DiagnosticCheckState.Fault, failed.Weather.State,
        "天气后台异常没有进入诊断故障状态");
    Equal("BackgroundCollectionError", failed.Weather.ErrorCategory,
        "天气后台异常类别不稳定");
    Equal(healthyAt, failed.Weather.LastNormalAt,
        "天气后台异常丢失最近正常时间");

    monitor.ObserveWeather(
        new WeatherMetricsReading(true, "大连", "晴", 26, 31, null),
        healthyAt.AddMinutes(2));
    Equal(DiagnosticCheckState.Normal, monitor.Current.Weather.State,
        "天气采集恢复后诊断没有自动恢复");
}
~~~

Also add a reliability test that publishes an initial `MetricsSnapshotStore` snapshot, runs a guarded operation that throws before a second publish, and asserts the snapshot sequence and values remain unchanged.

- [ ] **Step 2: Run the smoke suite and verify RED**

Expected: compile failure because `ObserveWeatherCollectionFailure` does not exist.

- [ ] **Step 3: Add the diagnostics transition**

Add:

~~~csharp
public void ObserveWeatherCollectionFailure(DateTimeOffset now)
{
    lock (_sync)
    {
        _weather = Fault(
            _weather,
            "天气采集异常",
            "BackgroundCollectionError");
        _updatedAt = now;
    }
}
~~~

Map `BackgroundCollectionError` to `天气采集异常` in the weather diagnostic message function.

- [ ] **Step 4: Construct one guard per runtime**

Add a readonly guard field and initialize it after `_deviceTokenStore`:

~~~csharp
var runtimeErrorLog = new RuntimeErrorLog(_deviceTokenStore.SettingsDirectory);
_backgroundCollectionGuard = new BackgroundCollectionGuard(runtimeErrorLog.TryWrite);
~~~

Do not create a global singleton or hold an open log file.

- [ ] **Step 5: Wrap only the metrics body**

Keep the reentrancy check and outer `finally`. Use:

~~~csharp
DateTimeOffset now = DateTimeOffset.UtcNow;
_backgroundCollectionGuard.Execute(
    BackgroundCollectionModule.Metrics,
    now,
    () =>
    {
        NetworkThroughputReading networkReading =
            _networkThroughputCollector.Read(Stopwatch.GetTimestamp());
        CodexMetricsReading codexReading = _codexMetricsCollector.Read(now);
        if (Volatile.Read(ref _closing) != 0)
            return;

        _diagnosticsMonitor.ObserveCodex(codexReading, now);
        _metricsSnapshotStore.Publish(networkReading, codexReading, now);
    },
    _ => { });
~~~

The empty failure callback is intentional: the existing five-second freshness check owns the metrics diagnostic and prevents one-cycle UI flicker.

- [ ] **Step 6: Wrap only the weather body**

Move the current weather operation unchanged into `Execute`. Use:

~~~csharp
_ => _diagnosticsMonitor.ObserveWeatherCollectionFailure(now)
~~~

Do not call `WeatherFailureObserved` for a guard failure.

- [ ] **Step 7: Run the smoke suite and verify GREEN**

Expected: all guard, log, diagnostics, runtime, and existing tests pass.

- [ ] **Step 8: Review and request the Task 3 commit gate**

Confirm Timer periods remain one second and one minute. Run `git diff --check`. After explicit approval, commit only Task 3 files with message `隔离后台指标与天气采集异常`.

---

### Task 4: Harden Codex session metadata JSON kinds

**Files:**
- Modify: `app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Codex/CodexLocalWeeklyUsageReader.cs:173-195`
- Modify: `app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Codex.cs`
- Modify: `app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Runner.cs`

**Interfaces:**
- Preserves: `CodexLocalWeeklyUsageReader.Read` and all token calculations.
- Changes: malformed first-line metadata becomes a non-subagent result instead of throwing.

- [ ] **Step 1: Write a failing malformed-metadata regression**

Create four temporary session files whose first line is respectively `[]`, `"text"`, `null`, and `{"type":"session_meta","payload":null}`. Append one valid in-period token event to each file. Read the directory and assert the valid totals are included without an exception.

Register:

~~~csharp
("Codex 会话元数据类型异常不会终止采集",
    CodexWeeklyUsageIgnoresInvalidSessionMetadataKinds),
~~~

- [ ] **Step 2: Run the smoke suite and verify RED**

Expected: `InvalidOperationException` from `JsonElement.TryGetProperty` in `IsSubagentSession`.

- [ ] **Step 3: Add the minimal value-kind guards**

~~~csharp
return root.ValueKind == JsonValueKind.Object &&
       root.TryGetProperty("payload", out JsonElement payload) &&
       payload.ValueKind == JsonValueKind.Object &&
       payload.TryGetProperty("source", out JsonElement source) &&
       source.ValueKind == JsonValueKind.Object &&
       source.TryGetProperty("subagent", out _);
~~~

Do not broaden the catch clause or change token arithmetic.

- [ ] **Step 4: Run the smoke suite and verify GREEN**

Expected: the new metadata regression and all existing Codex tests pass.

- [ ] **Step 5: Review and request the Task 4 commit gate**

After a three-file scope check and `git diff --check`, request commit approval and use message `补齐 Codex 会话元数据类型边界`.

---

### Task 5: Document the operational behavior

**Files:**
- Modify: `docs/DESKTOP_APP.md`

**Interfaces:**
- Documents: recovery, last-value retention, diagnostic behavior, log path, redaction, 512 KiB plus one-backup bound, and no notification.

- [ ] **Step 1: Add a reliability subsection**

Document the exact approved behavior and link to the approved design with a relative Markdown link. Do not claim deployment or production validation.

- [ ] **Step 2: Verify documentation consistency**

Run:

~~~powershell
rg -n "runtime-errors\.log|512 KiB|五分钟|BackgroundCollectionError|最后" docs/DESKTOP_APP.md docs/superpowers/specs/2026-08-08-background-collection-reliability-design.md
git diff --check
~~~

Expected: each operational rule is present and the diff check exits 0.

- [ ] **Step 3: Request the Task 5 commit gate**

After explicit approval, commit only `docs/DESKTOP_APP.md` with message `记录后台采集故障恢复边界`.

---

### Task 6: Run full verification and prepare a deployment gate

**Files:**
- No source edits expected.
- Generated ignored outputs may be replaced only under the existing repository `build` directory.

**Interfaces:**
- Consumes: all Tasks 1-5.
- Produces: verification evidence and a manifest-verified PC payload; it does not authorize D-drive deployment.

- [ ] **Step 1: Verify final source scope**

~~~powershell
git status --short --branch
git diff --check
git log --oneline --decorate -8
~~~

Expected: only approved implementation files are modified or approved commits are ahead of `origin/main`.

- [ ] **Step 2: Run the project verification script**

~~~powershell
& 'D:\ESP-IDF\v6.0.2\esp-idf\export.ps1'
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& '.\tools\verify.ps1'
~~~

Expected: exit 0; every desktop smoke test passes, both .NET builds have zero errors, Python remains 20/20, and firmware size/build checks pass.

- [ ] **Step 3: Re-run the NuGet vulnerability audit**

~~~powershell
dotnet list .\app\LibreHardwareMonitor\LibreHardwareMonitor\LibreHardwareMonitor.csproj package --vulnerable --include-transitive
~~~

Expected: no vulnerable package is reported from configured sources.

- [ ] **Step 4: Publish and verify the repository-local payload**

~~~powershell
& '.\tools\Publish-PC.ps1'
& '.\tools\Build-Installer.ps1' -SkipPublish
~~~

Verify every size and SHA-256 entry in `build/pc-release/release-manifest.json`. Record installer size and SHA-256. Do not copy to `D:\Solis Monitor` in this step.

- [ ] **Step 5: Request runtime/deployment authorization**

Present the exact local commit, changed files, test totals, payload version, file count, installer hash, and request permission to:

1. Stop the currently running deployed SolisMonitor process.
2. Copy only manifest-listed files to `D:\Solis Monitor` without mirror deletion.
3. Preserve `%LocalAppData%\SolisMonitor` configuration.
4. Start the installed EXE and observe it for at least 30 seconds.
5. Verify process survival, TCP 18472, zero new Windows crash events, and all installed file hashes.

Do not deploy until explicitly approved. Push and GitHub Release remain separate later gates.
