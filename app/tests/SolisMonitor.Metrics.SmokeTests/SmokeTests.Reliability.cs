internal static partial class SmokeTests
{
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
}
