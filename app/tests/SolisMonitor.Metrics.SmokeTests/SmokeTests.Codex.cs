internal static partial class SmokeTests
{
static void CodexWeeklyUsageFollowsMainQuotaCycle()
{
    string root = Path.Combine(Path.GetTempPath(), $"SolisMonitor.WeeklyTokens-{Guid.NewGuid():N}");
    try
    {
        var tracker = new CodexWeeklyUsageTracker(root);
        Near(0, tracker.Update(1_000_000, "07-29 08:46"),
             "首次观察主周周期时应建立零点");
        Near(250_000, tracker.Update(1_250_000, "07-29 08:46"),
             "同一主周周期没有按账户累计 Token 做差");

        var reloaded = new CodexWeeklyUsageTracker(root);
        Near(300_000, reloaded.Update(1_300_000, "07-29 08:46"),
             "PC 重启后没有保留主周周期基线");
        Near(0, reloaded.Update(1_400_000, "08-05 08:46"),
             "主周额度重置后没有重新从零统计");
        Near(100_000, reloaded.Update(1_500_000, "08-05 08:46"),
             "新主周周期累计值错误");
    }
    finally
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}

static void CodexWeeklyUsageUsesLocalSessionEventsImmediately()
{
    string root = Path.Combine(Path.GetTempPath(), $"SolisMonitor.LocalWeeklyTokens-{Guid.NewGuid():N}");
    DateTimeOffset nextReset = new(2026, 8, 4, 11, 26, 0, TimeZoneInfo.Local.GetUtcOffset(
        new DateTime(2026, 8, 4, 11, 26, 0)));
    DateTimeOffset periodStart = nextReset.AddDays(-7);
    const string existingId = "12121212-1212-1212-1212-121212121212";
    const string newId = "34343434-3434-3434-3434-343434343434";

    try
    {
        string directory = Path.Combine(root, "sessions", "2026", "07", "28");
        Directory.CreateDirectory(directory);
        string existingPath = Path.Combine(directory, $"rollout-existing-{existingId}.jsonl");
        File.WriteAllLines(existingPath,
        [
            JsonSerializer.Serialize(new
            {
                type = "session_meta",
                payload = new { id = existingId, cwd = "F:\\Projects\\Existing", source = "vscode" }
            }),
            CreateCodexTokenCount(periodStart.AddMinutes(-1), 10, 100, 0, 10080,
                nextReset.ToUnixTimeSeconds(), totalTokens: 1_000_000),
            CreateCodexTokenCount(periodStart.AddMinutes(1), 10, 100, 1, 10080,
                nextReset.ToUnixTimeSeconds(), totalTokens: 1_250_000),
            CreateCodexTokenCount(periodStart.AddMinutes(2), 10, 100, 2, 10080,
                nextReset.ToUnixTimeSeconds(), totalTokens: 1_400_000)
        ]);

        string newPath = Path.Combine(directory, $"rollout-new-{newId}.jsonl");
        File.WriteAllLines(newPath,
        [
            JsonSerializer.Serialize(new
            {
                type = "session_meta",
                payload = new { id = newId, cwd = "F:\\Projects\\New", source = "vscode" }
            }),
            CreateCodexTokenCount(periodStart.AddMinutes(3), 10, 100, 3, 10080,
                nextReset.ToUnixTimeSeconds(), totalTokens: 80_000)
        ]);
        File.SetLastWriteTimeUtc(existingPath, periodStart.AddMinutes(2).UtcDateTime);
        File.SetLastWriteTimeUtc(newPath, periodStart.AddMinutes(3).UtcDateTime);

        var reader = new CodexLocalWeeklyUsageReader(root);
        string resetText = nextReset.ToLocalTime().ToString(
            "MM-dd HH:mm", CultureInfo.InvariantCulture);
        Near(480_000, reader.Read(resetText, periodStart.AddMinutes(4)),
            "周使用 Token 没有立即汇总重置后的本地任务事件");

        File.AppendAllLines(existingPath,
        [
            CreateCodexTokenCount(periodStart.AddMinutes(5), 10, 100, 4, 10080,
                nextReset.ToUnixTimeSeconds(), totalTokens: 1_500_000)
        ]);
        File.SetLastWriteTimeUtc(existingPath, periodStart.AddMinutes(5).UtcDateTime);
        Near(580_000, reader.Read(resetText, periodStart.AddMinutes(6)),
            "增量刷新重复统计或漏掉了新 Token");
    }
    finally
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}

static void CodexWeeklyUsageIgnoresIncompleteTokenEvents()
{
    string root = Path.Combine(
        Path.GetTempPath(),
        $"SolisMonitor.IncompleteWeeklyTokens-{Guid.NewGuid():N}");
    DateTimeOffset nextReset = new(2026, 8, 8, 11, 37, 0,
        TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 8, 8, 11, 37, 0)));
    DateTimeOffset periodStart = nextReset.AddDays(-7);

    try
    {
        string directory = Path.Combine(root, "sessions", "2026", "08", "08");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "rollout-incomplete.jsonl");
        File.WriteAllLines(path,
        [
            JsonSerializer.Serialize(new
            {
                type = "session_meta",
                payload = new { id = "incomplete", cwd = "F:\\Projects\\Incomplete", source = "vscode" }
            }),
            JsonSerializer.Serialize(new
            {
                timestamp = periodStart.AddMinutes(1),
                payload = new { type = "token_count", info = (object?)null }
            }),
            CreateCodexTokenCount(periodStart.AddMinutes(2), 10, 100, 1, 10080,
                nextReset.ToUnixTimeSeconds(), totalTokens: 100_000)
        ]);
        File.SetLastWriteTimeUtc(path, periodStart.AddMinutes(2).UtcDateTime);

        var reader = new CodexLocalWeeklyUsageReader(root);
        string resetText = nextReset.ToLocalTime().ToString(
            "MM-dd HH:mm", CultureInfo.InvariantCulture);
        Near(100_000, reader.Read(resetText, periodStart.AddMinutes(3)),
            "不完整 token_count 记录不应终止周 Token 统计");
    }
    finally
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}

static void CodexWeeklyUsageIgnoresSubagentSessions()
{
    string root = Path.Combine(
        Path.GetTempPath(),
        $"SolisMonitor.WeeklySubagents-{Guid.NewGuid():N}");
    DateTimeOffset nextReset = new(2026, 8, 8, 11, 37, 0,
        TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 8, 8, 11, 37, 0)));
    DateTimeOffset periodStart = nextReset.AddDays(-7);

    try
    {
        string directory = Path.Combine(root, "sessions", "2026", "08", "01");
        Directory.CreateDirectory(directory);
        string mainPath = Path.Combine(directory, "rollout-main.jsonl");
        File.WriteAllLines(mainPath,
        [
            JsonSerializer.Serialize(new
            {
                type = "session_meta",
                payload = new { id = "main", cwd = "F:\\Projects\\Main", source = "vscode" }
            }),
            CreateCodexTokenCount(periodStart.AddMinutes(1), 10, 100, 1, 10080,
                nextReset.ToUnixTimeSeconds(), totalTokens: 100_000)
        ]);

        string subagentPath = Path.Combine(directory, "rollout-subagent.jsonl");
        File.WriteAllLines(subagentPath,
        [
            JsonSerializer.Serialize(new
            {
                type = "session_meta",
                payload = new
                {
                    id = "subagent",
                    cwd = "F:\\Projects\\Main",
                    source = new { subagent = new { } }
                }
            }),
            CreateCodexTokenCount(periodStart.AddMinutes(2), 10, 100, 2, 10080,
                nextReset.ToUnixTimeSeconds(), totalTokens: 900_000)
        ]);
        File.SetLastWriteTimeUtc(mainPath, periodStart.AddMinutes(1).UtcDateTime);
        File.SetLastWriteTimeUtc(subagentPath, periodStart.AddMinutes(2).UtcDateTime);

        var reader = new CodexLocalWeeklyUsageReader(root);
        string resetText = nextReset.ToLocalTime().ToString(
            "MM-dd HH:mm", CultureInfo.InvariantCulture);
        Near(100_000, reader.Read(resetText, periodStart.AddMinutes(3)),
            "子代理任务不应计入账户周使用 Token");
    }
    finally
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}

static void CodexWeeklyUsageRemovesMissingSessions()
{
    string root = Path.Combine(
        Path.GetTempPath(),
        $"SolisMonitor.WeeklyRemoved-{Guid.NewGuid():N}");
    DateTimeOffset nextReset = new(2026, 8, 8, 11, 37, 0,
        TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 8, 8, 11, 37, 0)));
    DateTimeOffset periodStart = nextReset.AddDays(-7);

    try
    {
        string directory = Path.Combine(root, "sessions", "2026", "08", "01");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "rollout-moved.jsonl");
        File.WriteAllLines(path,
        [
            JsonSerializer.Serialize(new
            {
                type = "session_meta",
                payload = new { id = "moved", cwd = "F:\\Projects\\Moved", source = "vscode" }
            }),
            CreateCodexTokenCount(periodStart.AddMinutes(1), 10, 100, 1, 10080,
                nextReset.ToUnixTimeSeconds(), totalTokens: 200_000)
        ]);
        File.SetLastWriteTimeUtc(path, periodStart.AddMinutes(1).UtcDateTime);

        var reader = new CodexLocalWeeklyUsageReader(root);
        string resetText = nextReset.ToLocalTime().ToString(
            "MM-dd HH:mm", CultureInfo.InvariantCulture);
        Near(200_000, reader.Read(resetText, periodStart.AddMinutes(2)),
            "初次读取本地周使用 Token 错误");

        File.Delete(path);
        Near(0, reader.Read(resetText, periodStart.AddMinutes(3)),
            "已移走任务仍残留在周使用 Token 中");
    }
    finally
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}

static void CodexWeeklyUsagePrefersAccountDelta()
{
    string root = Path.Combine(
        Path.GetTempPath(),
        $"SolisMonitor.WeeklyAccount-{Guid.NewGuid():N}");
    string settingsDirectory = Path.Combine(root, "settings");
    DateTimeOffset now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
    DateTimeOffset nextReset = now.AddDays(6);
    string resetText = nextReset.ToLocalTime().ToString(
        "MM-dd HH:mm", CultureInfo.InvariantCulture);

    try
    {
        string directory = Path.Combine(root, "sessions", "2026", "08", "02");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "rollout-main.jsonl");
        File.WriteAllLines(path,
        [
            JsonSerializer.Serialize(new
            {
                type = "session_meta",
                payload = new { id = "main", cwd = "F:\\Projects\\Main", source = "vscode" }
            }),
            CreateCodexTokenCount(now, 10, 100, 1, 10080,
                nextReset.ToUnixTimeSeconds(), totalTokens: 900_000)
        ]);
        File.SetLastWriteTimeUtc(path, now.UtcDateTime);

        var tracker = new CodexWeeklyUsageTracker(settingsDirectory);
        Near(0, tracker.Update(1_000_000, resetText),
            "账户周周期测试基线建立失败");
        Func<long?> accountReader = () => 1_200_000;
        System.Reflection.ConstructorInfo constructor =
            typeof(CodexMetricsCollector).GetConstructor(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic,
                null,
                [
                    typeof(string),
                    typeof(TimeSpan),
                    typeof(Func<long?>),
                    typeof(CodexWeeklyUsageTracker)
                ],
                null) ?? throw new InvalidOperationException("找不到 Codex 采集器测试构造函数");
        var collector = (CodexMetricsCollector)constructor.Invoke(
            [root, TimeSpan.FromMinutes(10), accountReader, tracker]);

        CodexMetricsReading reading = collector.Read(now);
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (reading.TotalTokens != 1_200_000 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
            reading = collector.Read(now);
        }

        Near(1_200_000, reading.TotalTokens, "账户累计 Token 后台读取未完成");
        Near(200_000, reading.WeeklyUsedTokens,
            "账户周期差值可用时不应被更大的本地估算覆盖");
    }
    finally
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}

static void CodexLargeIrrelevantLinesDoNotInflateManagedAllocations()
{
    string root = Path.Combine(Path.GetTempPath(), $"SolisMonitor.CodexAllocation-{Guid.NewGuid():N}");
    DateTimeOffset now = new(2026, 7, 28, 14, 0, 0, TimeSpan.Zero);
    DateTimeOffset nextReset = now.AddDays(6);
    const string id = "56565656-5656-5656-5656-565656565656";

    try
    {
        string directory = Path.Combine(root, "sessions", "2026", "07", "28");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"rollout-large-{id}.jsonl");
        string sessionMeta = JsonSerializer.Serialize(new
        {
            type = "session_meta",
            payload = new { id, cwd = "F:\\Projects\\Large", source = "vscode" }
        });
        string turnContext = JsonSerializer.Serialize(new
        {
            type = "turn_context",
            payload = new { model = "gpt-5.6-sol", effort = "medium" }
        });
        string irrelevant = new('x', 8 * 1024 * 1024);
        File.WriteAllLines(path,
        [
            sessionMeta,
            irrelevant,
            turnContext,
            CreateCodexTokenCount(
                now.AddMinutes(-1),
                50,
                100,
                25,
                10080,
                nextReset.ToUnixTimeSeconds(),
                totalTokens: 1_000_000)
        ]);
        File.SetLastWriteTimeUtc(path, now.AddMinutes(-1).UtcDateTime);

        var collector = new CodexMetricsCollector(root, TimeSpan.FromMinutes(10));
        long before = GC.GetAllocatedBytesForCurrentThread();
        CodexMetricsReading reading = collector.Read(now);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Near(50, reading.ContextUsedPercent, "大行之后的上下文指标没有读到");
        Equal("gpt-5.6-sol", reading.Model, "大行之后的模型信息没有读到");
        True(
            allocated < 4 * 1024 * 1024,
            $"扫描无关大行产生了过量托管分配；实际={allocated / 1024D / 1024D:F1} MB");
    }
    finally
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}

static void CodexLastActiveMainThreadIsRead()
{
    string root = Path.Combine(Path.GetTempPath(), $"SolisMonitor.CodexTest-{Guid.NewGuid():N}");
    DateTimeOffset now = new(2026, 7, 21, 13, 30, 0, TimeSpan.Zero);
    const string olderId = "11111111-1111-1111-1111-111111111111";
    const string latestId = "22222222-2222-2222-2222-222222222222";
    const string subagentId = "33333333-3333-3333-3333-333333333333";

    try
    {
        Directory.CreateDirectory(Path.Combine(root, "sessions", "2026", "07", "21"));
        File.WriteAllLines(Path.Combine(root, "session_index.jsonl"),
        [
            JsonSerializer.Serialize(new { id = olderId, thread_name = "旧项目" }),
            "not-json",
            JsonSerializer.Serialize(new { id = latestId, thread_name = "Solis_Monitor" })
        ]);

        string older = WriteCodexSession(root, olderId, now.AddMinutes(-4), 10, 100, 40, 10080);
        string latest = WriteCodexSession(root, latestId, now.AddMinutes(-2), 50, 200, 96, 10080,
            secondaryUsedPercent: 75, secondaryWindowMinutes: 300,
            projectName: "Solis_Monitor", model: "gpt-5.6-sol", effort: "high", totalTokens: 123456);
        string subagent = WriteCodexSession(root, subagentId, now.AddMinutes(-1), 99, 100, 99, 10080,
            isSubagent: true, limitId: "codex_bengalfox");
        File.SetLastWriteTimeUtc(older, now.AddMinutes(-4).UtcDateTime);
        File.SetLastWriteTimeUtc(latest, now.AddMinutes(-2).UtcDateTime);
        File.SetLastWriteTimeUtc(subagent, now.AddMinutes(-1).UtcDateTime);

        var collector = new CodexMetricsCollector(root, TimeSpan.FromMinutes(10));
        CodexMetricsReading reading = collector.Read(now);

        True(reading.Online, "10 分钟内的 Codex 任务应在线");
        Equal("Solis_Monitor", reading.LastActiveTask, "没有选择最后活动的主任务");
        Equal("Solis_Monitor", reading.ProjectName, "没有从 cwd 取得项目名");
        Equal("gpt-5.6-sol", reading.Model, "没有取得最后回合模型");
        Equal("high", reading.ReasoningEffort, "没有取得最后回合推理强度");
        Near(123456, reading.TotalTokens, "没有取得当前任务累计 Token");
        Near(25, reading.ContextUsedPercent, "上下文占用百分比错误");
        Near(4, reading.WeeklyRemainingPercent, "7 天剩余额度错误");
        Near(4, reading.MainQuota?.RemainingPercent, "主周额度字段错误");
        Equal("主周额度", reading.MainQuota?.Name, "主周额度名称字段错误");
        Equal(null, reading.ErrorCategory, "有效数据不应带错误类别");

        CodexMetricsReading stale = collector.Read(now.AddMinutes(11));
        True(!stale.Online, "超过 10 分钟没有事件时应显示离线");
        Near(25, stale.ContextUsedPercent, "离线时应保留最后一次上下文值");
    }
    finally
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}

static void CodexAccountLifetimeTokensAreParsed()
{
    const string response =
        "{\"id\":2,\"result\":{\"summary\":{\"lifetimeTokens\":2958484287," +
        "\"peakDailyTokens\":988664753},\"dailyUsageBuckets\":null}}";

    Equal(2958484287L, CodexAccountUsageReader.ParseLifetimeTokensResponse(response),
        "没有解析 account/usage/read 的账户累计 Token");
    True(CodexAccountUsageReader.ParseLifetimeTokensResponse(
             "{\"id\":2,\"result\":{\"summary\":{\"lifetimeTokens\":null}}}") is null,
         "空的账户累计 Token 不应显示为零");
}

static void CodexSparkRateLimitIdIsParsed()
{
    string root = Path.Combine(Path.GetTempPath(), $"SolisMonitor.CodexSparkTest-{Guid.NewGuid():N}");
    DateTimeOffset now = new(2026, 7, 21, 16, 30, 0, TimeSpan.Zero);
    const string currentId = "66666666-6666-6666-6666-666666666666";

    try
    {
        Directory.CreateDirectory(Path.Combine(root, "sessions", "2026", "07", "21"));
        File.WriteAllLines(Path.Combine(root, "session_index.jsonl"),
        [
            JsonSerializer.Serialize(new { id = currentId, thread_name = "Solis_Monitor_Spark" })
        ]);

        string sessionPath = WriteCodexSession(root, currentId, now.AddMinutes(-1), 12345, 258400, 0, 10080,
            limitId: "codex_bengalfox");
        File.SetLastWriteTimeUtc(sessionPath, now.AddMinutes(-1).UtcDateTime);

        var collector = new CodexMetricsCollector(root, TimeSpan.FromMinutes(10));
        CodexMetricsReading reading = collector.Read(now);

        Equal("Solis_Monitor_Spark", reading.LastActiveTask, "GPT-5.3-Codex-Spark 任务名称识别失败");
        Near(12345D * 100D / 258400D, reading.ContextUsedPercent,
            "上下文占用应该取 input_tokens/model_context_window");
        Near(100, reading.SparkQuota?.RemainingPercent, "Spark 周额度识别错误");
        Equal("GPT-5.3-Codex-Spark", reading.SparkQuota?.Name, "Spark 名称识别错误");
        Equal(null, reading.MainQuota, "主周额度不应在纯 Spark 流中出现");
    }
    finally
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}

static void CodexQuotaMappingAndResetFormatsAreStable()
{
    string root = Path.Combine(Path.GetTempPath(), $"SolisMonitor.CodexQuotaFormatsTest-{Guid.NewGuid():N}");
    DateTimeOffset now = new(2026, 7, 22, 6, 0, 0, TimeSpan.Zero);
    const string mainId = "77777777-7777-7777-7777-777777777777";
    const string sparkId = "88888888-8888-8888-8888-888888888888";
    long mainResetMilliseconds = now.AddDays(7).ToUnixTimeMilliseconds();
    long sparkResetMilliseconds = now.AddDays(6).ToUnixTimeMilliseconds();

    try
    {
        Directory.CreateDirectory(Path.Combine(root, "sessions", "2026", "07", "21"));
        File.WriteAllLines(Path.Combine(root, "session_index.jsonl"),
        [
            JsonSerializer.Serialize(new { id = mainId, thread_name = "未来主额度" }),
            JsonSerializer.Serialize(new { id = sparkId, thread_name = "Spark 额度" })
        ]);

        string main = WriteCodexSession(root, mainId, now.AddMinutes(-2), 10, 100, 25, 10079,
            primaryResetsAt: mainResetMilliseconds.ToString(CultureInfo.InvariantCulture),
            limitId: "future_weekly_plan");
        string spark = WriteCodexSession(root, sparkId, now.AddMinutes(-1), 20, 100, 3, 10080,
            primaryResetsAt: sparkResetMilliseconds,
            limitId: "codex_bengalfox");
        File.SetLastWriteTimeUtc(main, now.AddMinutes(-2).UtcDateTime);
        File.SetLastWriteTimeUtc(spark, now.AddMinutes(-1).UtcDateTime);

        var collector = new CodexMetricsCollector(root, TimeSpan.FromMinutes(10));
        CodexMetricsReading reading = collector.Read(now);

        Near(75, reading.MainQuota?.RemainingPercent,
            "未知且不含 codex 的未来额度应按 primary 槽位保留");
        Equal("主周额度", reading.MainQuota?.Name, "主额度应使用稳定友好名称");
        Equal(DateTimeOffset.FromUnixTimeMilliseconds(mainResetMilliseconds).ToLocalTime()
                .ToString("MM-dd HH:mm", CultureInfo.InvariantCulture),
            reading.MainQuota?.ResetAtLocal, "数字字符串毫秒重置时间解析错误");
        Near(97, reading.SparkQuota?.RemainingPercent, "Spark 显式映射错误");
        Equal("GPT-5.3-Codex-Spark", reading.SparkQuota?.Name, "Spark 应使用稳定友好名称");
        Equal(DateTimeOffset.FromUnixTimeMilliseconds(sparkResetMilliseconds).ToLocalTime()
                .ToString("MM-dd HH:mm", CultureInfo.InvariantCulture),
            reading.SparkQuota?.ResetAtLocal, "整数毫秒重置时间解析错误");
    }
    finally
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}

static void CodexWeeklyQuotaDoesNotRegressAcrossProjectSwitch()
{
    string root = Path.Combine(Path.GetTempPath(), $"SolisMonitor.CodexQuotaTest-{Guid.NewGuid():N}");
    DateTimeOffset now = new(2026, 7, 21, 14, 0, 0, TimeSpan.Zero);
    const string currentId = "44444444-4444-4444-4444-444444444444";
    const string switchedId = "55555555-5555-5555-5555-555555555555";
    const long resetAt = 1785040004;

    try
    {
        Directory.CreateDirectory(Path.Combine(root, "sessions", "2026", "07", "21"));
        File.WriteAllLines(Path.Combine(root, "session_index.jsonl"),
        [
            JsonSerializer.Serialize(new { id = currentId, thread_name = "当前项目" }),
            JsonSerializer.Serialize(new { id = switchedId, thread_name = "切换后的项目" })
        ]);

        string current = WriteCodexSession(root, currentId, now.AddMinutes(-2), 50, 200, 100, 10080,
            primaryResetsAt: resetAt);
        string switched = WriteCodexSession(root, switchedId, now.AddMinutes(-4), 20, 200, 98, 10080,
            primaryResetsAt: resetAt);
        File.SetLastWriteTimeUtc(current, now.AddMinutes(-2).UtcDateTime);
        File.SetLastWriteTimeUtc(switched, now.AddMinutes(-4).UtcDateTime);

        var collector = new CodexMetricsCollector(root, TimeSpan.FromMinutes(10));
        CodexMetricsReading initial = collector.Read(now);
        Near(0, initial.MainQuota?.RemainingPercent, "周额度耗尽时应显示 0%");
        Near(0, initial.WeeklyRemainingPercent, "周额度保持向后兼容显示主周余量");

        File.SetLastWriteTimeUtc(switched, now.AddSeconds(6).UtcDateTime);

        CodexMetricsReading afterSwitch = collector.Read(now.AddSeconds(6));
        Equal("切换后的项目", afterSwitch.LastActiveTask, "项目名称没有随最后活动任务切换");
        Near(10, afterSwitch.ContextUsedPercent, "项目切换后的上下文占用错误");
        Near(0, afterSwitch.MainQuota?.RemainingPercent, "不应被切换后项目的历史 2% 覆盖");
        Near(0, afterSwitch.WeeklyRemainingPercent, "主周额度向后兼容显示应保持 0%");

        File.AppendAllLines(switched,
        [
            CreateCodexTokenCount(now.AddSeconds(12), 70, 200, 0, 10080, resetAt)
        ]);
        File.SetLastWriteTimeUtc(switched, now.AddSeconds(12).UtcDateTime);
        CodexMetricsReading afterReset = collector.Read(now.AddSeconds(12));
        Near(100, afterReset.MainQuota?.RemainingPercent, "后台产生更新事件后应允许主周额度动态恢复");
        Near(100, afterReset.WeeklyRemainingPercent, "向后兼容主周额度显示应恢复");
    }
    finally
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}

static void CodexSanitizedFixtureIsParsed()
{
    string root = Path.Combine(Path.GetTempPath(), $"SolisMonitor.CodexFixtureTest-{Guid.NewGuid():N}");
    DateTimeOffset now = new(2026, 7, 22, 8, 2, 0, TimeSpan.Zero);
    const string id = "99999999-9999-9999-9999-999999999999";

    try
    {
        string sessionDirectory = Path.Combine(root, "sessions", "2026", "07", "22");
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(Path.Combine(root, "session_index.jsonl"),
            JsonSerializer.Serialize(new { id, thread_name = "脱敏样例" }));
        string sessionPath = Path.Combine(sessionDirectory, $"rollout-2026-07-22T08-00-00-{id}.jsonl");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "codex", "session_complete.jsonl"),
            sessionPath);
        File.SetLastWriteTimeUtc(sessionPath, now.AddMinutes(-1).UtcDateTime);

        var collector = new CodexMetricsCollector(root, TimeSpan.FromMinutes(10));
        CodexMetricsReading reading = collector.Read(now);

        True(reading.Online, "脱敏样例应产生在线读数");
        Equal("脱敏样例", reading.LastActiveTask, "样例任务名错误");
        Equal("Example_Project", reading.ProjectName, "样例项目名错误");
        Equal("gpt-example", reading.Model, "样例模型错误");
        Near(25, reading.ContextUsedPercent, "样例上下文占用错误");
        Near(900000, reading.TotalTokens, "样例累计 Token 错误");
        Near(59, reading.MainQuota?.RemainingPercent, "样例主周额度错误");
        Near(98, reading.SparkQuota?.RemainingPercent, "样例 Spark 周额度错误");
        Equal(null, reading.ErrorCategory, "完整脱敏样例不应产生诊断错误");
    }
    finally
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}

static void CodexMalformedInternalFormatIsDiagnosed()
{
    string invalidMetaRoot = Path.Combine(Path.GetTempPath(), $"SolisMonitor.CodexInvalidMeta-{Guid.NewGuid():N}");
    string invalidJsonRoot = Path.Combine(Path.GetTempPath(), $"SolisMonitor.CodexInvalidJson-{Guid.NewGuid():N}");
    string missingFieldsRoot = Path.Combine(Path.GetTempPath(), $"SolisMonitor.CodexMissingFields-{Guid.NewGuid():N}");
    string invalidLimitsRoot = Path.Combine(Path.GetTempPath(), $"SolisMonitor.CodexInvalidLimits-{Guid.NewGuid():N}");
    DateTimeOffset now = new(2026, 7, 22, 9, 0, 0, TimeSpan.Zero);

    try
    {
        string invalidMetaDirectory = Path.Combine(invalidMetaRoot, "sessions", "2026", "07", "22");
        Directory.CreateDirectory(invalidMetaDirectory);
        File.WriteAllText(Path.Combine(invalidMetaDirectory, "invalid-meta.jsonl"),
            "{\"type\":\"session_meta\",\"payload\":{}}");
        CodexMetricsReading invalidMeta = new CodexMetricsCollector(
            invalidMetaRoot, TimeSpan.FromMinutes(10)).Read(now);
        Equal("SessionMetadataInvalid", invalidMeta.ErrorCategory,
            "缺少会话 ID 时应给出元数据诊断");

        string invalidJsonDirectory = Path.Combine(invalidJsonRoot, "sessions", "2026", "07", "22");
        Directory.CreateDirectory(invalidJsonDirectory);
        File.WriteAllLines(Path.Combine(invalidJsonDirectory, "invalid-json.jsonl"),
        [
            JsonSerializer.Serialize(new
            {
                type = "session_meta",
                payload = new { id = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", cwd = "F:\\Projects\\InvalidJson", source = "vscode" }
            }),
            "{\"timestamp\":\"2026-07-22T08:59:00Z\",\"payload\":{\"type\":\"token_count\""
        ]);
        CodexMetricsReading invalidJson = new CodexMetricsCollector(
            invalidJsonRoot, TimeSpan.FromMinutes(10)).Read(now);
        Equal("TokenCountInvalidJson", invalidJson.ErrorCategory,
            "截断的 token_count 应给出 JSON 诊断");

        string missingFieldsDirectory = Path.Combine(missingFieldsRoot, "sessions", "2026", "07", "22");
        Directory.CreateDirectory(missingFieldsDirectory);
        File.WriteAllLines(Path.Combine(missingFieldsDirectory, "missing-fields.jsonl"),
        [
            JsonSerializer.Serialize(new
            {
                type = "session_meta",
                payload = new { id = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", cwd = "F:\\Projects\\MissingFields", source = "vscode" }
            }),
            JsonSerializer.Serialize(new
            {
                timestamp = now.AddMinutes(-1).ToString("O"),
                payload = new { type = "token_count", info = new { } }
            })
        ]);
        CodexMetricsReading missingFields = new CodexMetricsCollector(
            missingFieldsRoot, TimeSpan.FromMinutes(10)).Read(now);
        Equal("TokenCountFieldsMissing", missingFields.ErrorCategory,
            "缺少必要计数字段时应给出字段诊断");

        string invalidLimitsDirectory = Path.Combine(invalidLimitsRoot, "sessions", "2026", "07", "22");
        Directory.CreateDirectory(invalidLimitsDirectory);
        File.WriteAllLines(Path.Combine(invalidLimitsDirectory, "invalid-limits.jsonl"),
        [
            JsonSerializer.Serialize(new
            {
                type = "session_meta",
                payload = new { id = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee", cwd = "F:\\Projects\\InvalidLimits", source = "vscode" }
            }),
            JsonSerializer.Serialize(new
            {
                timestamp = now.AddMinutes(-1).ToString("O"),
                payload = new
                {
                    type = "token_count",
                    info = new
                    {
                        last_token_usage = new { input_tokens = 25 },
                        total_token_usage = new { total_tokens = 50 },
                        model_context_window = 100
                    },
                    rate_limits = "unexpected"
                }
            })
        ]);
        CodexMetricsReading invalidLimits = new CodexMetricsCollector(
            invalidLimitsRoot, TimeSpan.FromMinutes(10)).Read(now);
        True(invalidLimits.Online, "额度结构损坏不应丢失仍有效的上下文");
        Near(25, invalidLimits.ContextUsedPercent, "额度结构损坏时上下文应继续解析");
        Equal("RateLimitsInvalid", invalidLimits.ErrorCategory,
            "额度结构类型变化应给出明确诊断");
    }
    finally
    {
        foreach (string root in new[]
                 { invalidMetaRoot, invalidJsonRoot, missingFieldsRoot, invalidLimitsRoot })
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}

static void CodexNonWeeklyWindowsAreIgnored()
{
    string root = Path.Combine(Path.GetTempPath(), $"SolisMonitor.CodexWindowTest-{Guid.NewGuid():N}");
    DateTimeOffset now = new(2026, 7, 22, 10, 0, 0, TimeSpan.Zero);
    const string id = "cccccccc-cccc-cccc-cccc-cccccccccccc";

    try
    {
        Directory.CreateDirectory(Path.Combine(root, "sessions", "2026", "07", "21"));
        string path = WriteCodexSession(root, id, now.AddMinutes(-1), 20, 100, 75, 10078,
            secondaryUsedPercent: 50, secondaryWindowMinutes: 300);
        File.SetLastWriteTimeUtc(path, now.AddMinutes(-1).UtcDateTime);

        CodexMetricsReading reading = new CodexMetricsCollector(
            root, TimeSpan.FromMinutes(10)).Read(now);
        True(reading.Online, "非周额度事件仍应提供上下文读数");
        Equal(null, reading.MainQuota, "10078 分钟窗口不应识别为七天主额度");
        Equal(null, reading.SparkQuota, "300 分钟窗口不应识别为七天 Spark 额度");
        Equal(null, reading.ErrorCategory, "合法的非周窗口不应产生格式错误");
    }
    finally
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}

static void CodexIncrementalAppendAndTruncationWorks()
{
    string root = Path.Combine(Path.GetTempPath(), $"SolisMonitor.CodexIncrementalTest-{Guid.NewGuid():N}");
    DateTimeOffset now = new(2026, 7, 22, 11, 0, 0, TimeSpan.Zero);
    const string id = "dddddddd-dddd-dddd-dddd-dddddddddddd";

    try
    {
        Directory.CreateDirectory(Path.Combine(root, "sessions", "2026", "07", "21"));
        string path = WriteCodexSession(root, id, now.AddMinutes(-2), 10, 100, 50, 10080);
        File.SetLastWriteTimeUtc(path, now.AddMinutes(-2).UtcDateTime);
        var collector = new CodexMetricsCollector(root, TimeSpan.FromMinutes(10));
        Near(10, collector.Read(now).ContextUsedPercent, "首次扫描上下文错误");

        File.AppendAllLines(path,
        [
            CreateCodexTokenCount(now.AddSeconds(5), 30, 100, 45, 10080, null)
        ]);
        File.SetLastWriteTimeUtc(path, now.AddSeconds(6).UtcDateTime);
        Near(30, collector.Read(now.AddSeconds(6)).ContextUsedPercent,
            "增量追加后没有读取新事件");

        string sessionMeta = JsonSerializer.Serialize(new
        {
            type = "session_meta",
            payload = new { id, cwd = "F:\\Projects\\Truncated", source = "vscode" }
        });
        File.WriteAllLines(path,
        [
            sessionMeta,
            CreateCodexTokenCount(now.AddSeconds(11), 60, 100, 40, 10080, null)
        ]);
        File.SetLastWriteTimeUtc(path, now.AddSeconds(12).UtcDateTime);
        CodexMetricsReading afterTruncate = collector.Read(now.AddSeconds(12));
        Near(60, afterTruncate.ContextUsedPercent, "文件截断后没有从头恢复读取");
        Equal(null, afterTruncate.ErrorCategory, "截断恢复后不应保留错误诊断");
    }
    finally
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}

static void SnapshotStorePublishesCodex()
{
    var store = new MetricsSnapshotStore();
    store.Publish(
        new NetworkThroughputReading(false, null, null, null, null, "NotSampled"),
        new CodexMetricsReading(
            true,
            "Solis_Monitor",
            25,
            12.5,
            128,
            4,
            new CodexQuotaReading("主周额度", 4, "07-21 23:59"),
            new CodexQuotaReading("GPT-5.3-Codex-Spark", 97, "07-21 23:00"),
            null),
        DateTimeOffset.FromUnixTimeSeconds(200));

    SolisMetricsSnapshot snapshot = store.Current;
    Equal(1UL, snapshot.Sequence, "Codex 快照序号未递增");
    Equal(200L, snapshot.GeneratedAtUnixSeconds, "Codex 快照时间错误");
    True(snapshot.Codex.Online, "Codex 在线状态未写入快照");
    Equal("Solis_Monitor", snapshot.Codex.LastActiveTask, "Codex 项目名未写入快照");
    Near(25, snapshot.Codex.ContextUsedPercent.Value, "Codex 上下文未写入快照");
    Near(12.5, snapshot.Codex.ContextUsedK.Value, "Codex 上下文已用(k)未写入快照");
    Near(128, snapshot.Codex.ContextWindowK.Value, "Codex 上下文上限(k)未写入快照");
    Near(4, snapshot.Codex.WeeklyRemainingPercent.Value, "Codex 周余额未写入快照");
    Near(4, snapshot.Codex.MainWeeklyRemainingPercent.Value, "主周额度未写入快照");
    Equal("主周额度", snapshot.Codex.MainQuotaName, "主周名称未写入快照");
    Equal("GPT-5.3-Codex-Spark", snapshot.Codex.SparkQuotaName, "Spark名称未写入快照");
}
}
