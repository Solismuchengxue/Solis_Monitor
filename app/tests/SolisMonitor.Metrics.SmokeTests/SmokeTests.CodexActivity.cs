internal static partial class SmokeTests
{
    static void CodexWeeklyUsageResetsWhenTailRemoved() =>
        AssertCodexWeeklyUsageTailTruncation(0);

    static void CodexWeeklyUsageResetsWhenTailShortened() =>
        AssertCodexWeeklyUsageTailTruncation(5);

    static void AssertCodexWeeklyUsageTailTruncation(int retainedTailBytes)
    {
        string root = Path.Combine(Path.GetTempPath(), $"SolisMonitor.TruncatedTail-{Guid.NewGuid():N}");
        DateTimeOffset reset = new(2026, 8, 8, 11, 37, 0,
            TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 8, 8, 11, 37, 0)));
        DateTimeOffset now = reset.AddDays(-7).AddHours(1);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "sessions", "2026", "07", "21"));
            string path = WriteCodexSession(root, "weekly", now.AddSeconds(-1), 10, 100, 18, 10080,
                primaryResetsAt: reset.ToUnixTimeSeconds(), totalTokens: 100);
            File.SetLastWriteTimeUtc(path, now.UtcDateTime);
            long completeLength = new FileInfo(path).Length;
            var reader = new CodexLocalWeeklyUsageReader(root);
            string resetText = reset.ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
            Near(100, reader.Read(resetText, now), "初始周 Token 基线错误");
            string token = CreateCodexTokenCount(now.AddSeconds(6), 20, 100, 18, 10080,
                reset.ToUnixTimeSeconds(), totalTokens: 175);
            byte[] tail = Encoding.UTF8.GetBytes(token);
            File.AppendAllText(path, token);
            Near(175, reader.Read(resetText, now.AddSeconds(6)), "完整无换行尾记录应即时计数");
            Near(175, reader.Read(resetText, now.AddSeconds(12)), "重读无换行尾记录不得重复累计");
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write))
                stream.SetLength(completeLength + retainedTailBytes);
            Near(100, reader.Read(resetText, now.AddSeconds(18)), "截短已计数尾记录后必须清除旧贡献");
            Near(100, reader.Read(resetText, now.AddSeconds(24)), "截断后重复读取不能恢复旧贡献");
            using (var stream = new FileStream(path, FileMode.Append))
                stream.Write(tail.AsSpan(retainedTailBytes));
            File.AppendAllText(path, "\r\n");
            Near(175, reader.Read(resetText, now.AddSeconds(30)), "截断后补全记录应重新正确累计");
            Near(175, reader.Read(resetText, now.AddSeconds(36)), "补全记录不得重复累计");
        }
        finally { Directory.Delete(root, true); }
    }

    static void CodexActivityRecoversSplitTokenRecords()
    {
        DateTimeOffset now = new(2026, 10, 16, 2, 0, 0, TimeSpan.Zero);
        string token = CreateCodexTokenCount(now.AddSeconds(6), 60, 100, 18, 10080, null);
        foreach (int split in new[] { token.IndexOf("token_count", StringComparison.Ordinal) + 4, token.Length / 2 })
        {
            string root = Path.Combine(Path.GetTempPath(), $"SolisMonitor.SplitToken-{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "sessions", "2026", "07", "21"));
                WriteCodexSession(root, "minister", now.AddMinutes(-20), 20, 100, 18, 10080,
                    projectName: "Solis_Central_Committee");
                string current = WriteCodexSession(root, "monitor", now.AddMinutes(-30), 10, 100, 18, 10080,
                    projectName: "Solis_Monitor");
                var collector = new CodexMetricsCollector(root, TimeSpan.FromMinutes(10));
                Equal("Solis_Central_Committee", collector.Read(now).ProjectName, "分段前初始会话错误");
                File.AppendAllText(current, token[..split]);
                var partial = collector.Read(now.AddSeconds(6));
                True(!partial.Online, "未完成 Token 不应伪造活跃状态");
                File.AppendAllText(current, token[split..] + "\n");
                var completed = collector.Read(now.AddSeconds(12));
                Equal("Solis_Monitor", completed.ProjectName, "补全尾行后丢失了完整 Token 事件");
                True(completed.Online, "完整 Token 补全后未恢复活跃");
                Near(60, completed.ContextUsedPercent, "补全后的 Token 上下文丢失");
            }
            finally { Directory.Delete(root, true); }
        }
    }

    static void CodexActivityRecoversSplitTurnContext()
    {
        string root = Path.Combine(Path.GetTempPath(), $"SolisMonitor.SplitContext-{Guid.NewGuid():N}");
        DateTimeOffset now = new(2026, 10, 16, 2, 0, 0, TimeSpan.Zero);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "sessions", "2026", "07", "21"));
            WriteCodexSession(root, "minister", now.AddMinutes(-20), 20, 100, 18, 10080,
                projectName: "Solis_Central_Committee");
            string current = WriteCodexSession(root, "monitor", now.AddMinutes(-30), 10, 100, 18, 10080,
                projectName: "Solis_Monitor");
            var collector = new CodexMetricsCollector(root, TimeSpan.FromMinutes(10));
            collector.Read(now);
            string line = JsonSerializer.Serialize(new { timestamp=now.AddSeconds(6), type="turn_context",
                payload=new {model="新模型",effort="high"} }, new JsonSerializerOptions {
                    Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            byte[] bytes = Encoding.UTF8.GetBytes(line + "\r\n");
            int split = Array.IndexOf(bytes, (byte)0xE6) + 1;
            True(split > 0, "测试必须在真实 UTF8 多字节字符中分段");
            using (var stream = new FileStream(current, FileMode.Append)) stream.Write(bytes.AsSpan(0, split));
            collector.Read(now.AddSeconds(6));
            using (var stream = new FileStream(current, FileMode.Append)) stream.Write(bytes.AsSpan(split));
            var completed = collector.Read(now.AddSeconds(12));
            Equal("Solis_Monitor", completed.ProjectName, "补全后的 turn_context 没有参与项目选择");
            Equal("新模型", completed.Model, "跨字节补全后模型乱码或丢失");
            True(!completed.Online, "只有回合上下文不应延长旧 Token 活跃期");
            File.AppendAllLines(current, [CreateCodexTokenCount(now.AddSeconds(18), 70, 100, 18, 10080, null)]);
            True(collector.Read(now.AddSeconds(18)).Online, "后续完整 Token 未恢复活跃");
        }
        finally { Directory.Delete(root, true); }
    }

    static void CodexWeeklyUsageRecoversSplitTokenRecords()
    {
        string root = Path.Combine(Path.GetTempPath(), $"SolisMonitor.SplitWeekly-{Guid.NewGuid():N}");
        DateTimeOffset reset = new(2026, 8, 8, 11, 37, 0,
            TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 8, 8, 11, 37, 0)));
        DateTimeOffset now = reset.AddDays(-7).AddHours(1);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "sessions", "2026", "07", "21"));
            string path = WriteCodexSession(root, "weekly", now.AddSeconds(-1), 10, 100, 18, 10080,
                primaryResetsAt: reset.ToUnixTimeSeconds(), totalTokens:100);
            File.SetLastWriteTimeUtc(path, now.UtcDateTime);
            var reader = new CodexLocalWeeklyUsageReader(root);
            string resetText = reset.ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
            Near(100, reader.Read(resetText, now), "初始周 Token 基线错误");
            string token = CreateCodexTokenCount(now.AddSeconds(6), 20, 100, 18, 10080,
                reset.ToUnixTimeSeconds(), totalTokens:175);
            int split = token.Length / 2;
            File.AppendAllText(path, token[..split]);
            Near(100, reader.Read(resetText, now.AddSeconds(6)), "不完整尾行不应增加周计数");
            File.AppendAllText(path, token[split..] + "\r\n");
            Near(175, reader.Read(resetText, now.AddSeconds(12)), "补全尾行丢失周 Token 增量");
            Near(175, reader.Read(resetText, now.AddSeconds(18)), "重复刷新不应重复累计尾行");
        }
        finally { Directory.Delete(root, true); }
    }

    static void CodexActivityWinsOverFrozenFileTime()
    {
        string root = Path.Combine(Path.GetTempPath(), $"SolisMonitor.Activity-{Guid.NewGuid():N}");
        DateTimeOffset now = new(2026, 10, 16, 2, 46, 30, TimeSpan.FromHours(8));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "sessions", "2026", "07", "21"));
            string old = WriteCodexSession(root, "minister", now.AddMinutes(-25), 30, 100, 18, 10080,
                projectName: "Solis_Central_Committee");
            string current = WriteCodexSession(root, "monitor", now.AddSeconds(-4), 55, 100, 18, 10080,
                projectName: "Solis_Monitor");
            string child = WriteCodexSession(root, "child", now.AddSeconds(-1), 99, 100, 18, 10080,
                isSubagent: true, projectName: "IgnoredChild");
            File.SetLastWriteTimeUtc(current, now.AddHours(-3).UtcDateTime);
            File.SetLastWriteTimeUtc(old, now.AddMinutes(-34).UtcDateTime);
            File.SetLastWriteTimeUtc(child, now.UtcDateTime);
            var reading = new CodexMetricsCollector(root, TimeSpan.FromMinutes(10)).Read(now);
            Equal("Solis_Monitor", reading.ProjectName, "事件时间最新的主项目被滞后文件时间覆盖");
            True(reading.Online, "当前主任务有新 Token 时不应因旧项目超时而不活跃");
            Near(55, reading.ContextUsedPercent, "上下文必须来自选中的主任务");
        }
        finally { Directory.Delete(root, true); }
    }

    static void CodexActivityAppendSwitchesFromStaleProject()
    {
        string root = Path.Combine(Path.GetTempPath(), $"SolisMonitor.ActivityAppend-{Guid.NewGuid():N}");
        DateTimeOffset now = new(2026, 10, 16, 2, 0, 0, TimeSpan.Zero);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "sessions", "2026", "07", "21"));
            string old = WriteCodexSession(root, "minister", now.AddMinutes(-20), 20, 100, 18, 10080,
                projectName: "Solis_Central_Committee");
            string current = WriteCodexSession(root, "monitor", now.AddMinutes(-30), 10, 100, 18, 10080,
                projectName: "Solis_Monitor");
            File.SetLastWriteTimeUtc(old, now.AddMinutes(-20).UtcDateTime);
            File.SetLastWriteTimeUtc(current, now.AddHours(-3).UtcDateTime);
            var collector = new CodexMetricsCollector(root, TimeSpan.FromMinutes(10));
            True(!collector.Read(now).Online, "初始历史任务应不活跃");
            File.AppendAllLines(current, [CreateCodexTokenCount(now.AddSeconds(6), 60, 100, 18, 10080, null)]);
            File.SetLastWriteTimeUtc(current, now.AddHours(-3).UtcDateTime);
            var reading = collector.Read(now.AddSeconds(6));
            Equal("Solis_Monitor", reading.ProjectName, "追加真实事件但 mtime 不变时必须切换项目");
            True(reading.Online, "追加有效 Token 后必须恢复活跃");
            Near(60, reading.ContextUsedPercent, "没有读取增长文件的增量");
        }
        finally { Directory.Delete(root, true); }
    }

    static void CodexUnrelatedWritesDoNotStealActivity()
    {
        string root = Path.Combine(Path.GetTempPath(), $"SolisMonitor.ActivityNoise-{Guid.NewGuid():N}");
        DateTimeOffset now = new(2026, 10, 16, 2, 0, 0, TimeSpan.Zero);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "sessions", "2026", "07", "21"));
            string current = WriteCodexSession(root, "monitor", now.AddMinutes(-1), 60, 100, 18, 10080,
                projectName: "Solis_Monitor");
            string old = WriteCodexSession(root, "minister", now.AddMinutes(-20), 20, 100, 18, 10080,
                projectName: "Solis_Central_Committee");
            File.SetLastWriteTimeUtc(current, now.AddMinutes(-1).UtcDateTime);
            File.SetLastWriteTimeUtc(old, now.AddMinutes(-20).UtcDateTime);
            var collector = new CodexMetricsCollector(root, TimeSpan.FromMinutes(10));
            Equal("Solis_Monitor", collector.Read(now).ProjectName, "初始项目错误");
            File.AppendAllLines(old, [JsonSerializer.Serialize(new {timestamp=now.AddSeconds(6), type="response_item", payload=new {type="message"}})]);
            File.SetLastWriteTimeUtc(old, now.AddSeconds(6).UtcDateTime);
            var reading = collector.Read(now.AddSeconds(6));
            Equal("Solis_Monitor", reading.ProjectName, "非指标写入或 touch 不应抢占当前主任务");
            True(reading.Online, "无关文件写入不应把当前项目变为不活跃");
        }
        finally { Directory.Delete(root, true); }
    }

    static void CodexActivityCachesUnchangedSessions()
    {
        string root = Path.Combine(Path.GetTempPath(), $"SolisMonitor.ActivityCache-{Guid.NewGuid():N}");
        DateTimeOffset now = new(2026, 10, 16, 2, 0, 0, TimeSpan.Zero);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "sessions", "2026", "07", "21"));
            string current = WriteCodexSession(root, "monitor", now.AddSeconds(-1), 60, 100, 18, 10080,
                projectName: "Solis_Monitor", model: "monitor-model");
            string other = WriteCodexSession(root, "other", now.AddMinutes(-2), 20, 100, 18, 10080,
                projectName: "Other", model: "other-model");
            File.SetLastWriteTimeUtc(current, now.UtcDateTime);
            File.SetLastWriteTimeUtc(other, now.AddHours(-1).UtcDateTime);
            var collector = new CodexMetricsCollector(root, TimeSpan.FromMinutes(10));
            Near(60, collector.Read(now).ContextUsedPercent, "首次缓存错误");
            using (var held = new FileStream(current, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var cached = collector.Read(now.AddSeconds(6));
                Equal(null, cached.ErrorCategory, "未增长文件不应被重复打开扫描");
                File.AppendAllLines(other, [CreateCodexTokenCount(now.AddSeconds(12), 40, 100, 18, 10080, null)]);
                File.SetLastWriteTimeUtc(other, now.AddHours(-1).UtcDateTime);
                Equal("Other", collector.Read(now.AddSeconds(12)).ProjectName, "其他会话新增事件未被发现");
            }
            File.AppendAllLines(current, [CreateCodexTokenCount(now.AddSeconds(18), 70, 100, 18, 10080, null)]);
            var restored = collector.Read(now.AddSeconds(18));
            Equal("Solis_Monitor", restored.ProjectName, "无法切回先前会话");
            Equal("monitor-model", restored.Model, "切回时丢失该会话缓存模型");
            Near(70, restored.ContextUsedPercent, "切回时上下文未增量更新");
            True(!collector.Read(now.AddMinutes(11)).Online, "没有新 Token 时仍须按十分钟规则失活");
        }
        finally { Directory.Delete(root, true); }
    }

    static void CodexActivityRecoversFromTruncationAndRemoval()
    {
        string root = Path.Combine(Path.GetTempPath(), $"SolisMonitor.ActivityReset-{Guid.NewGuid():N}");
        DateTimeOffset now = new(2026, 10, 16, 2, 0, 0, TimeSpan.Zero);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "sessions", "2026", "07", "21"));
            string previous = WriteCodexSession(root, "previous", now.AddMinutes(-1), 20, 100, 18, 10080,
                projectName: "Previous");
            string current = WriteCodexSession(root, "monitor", now, 60, 100, 18, 10080,
                projectName: "Solis_Monitor");
            File.AppendAllLines(current, [new string('x', 8192)]);
            File.SetLastWriteTimeUtc(previous, now.AddMinutes(-1).UtcDateTime);
            File.SetLastWriteTimeUtc(current, now.UtcDateTime);
            var collector = new CodexMetricsCollector(root, TimeSpan.FromMinutes(10));
            Equal("Solis_Monitor", collector.Read(now).ProjectName, "初始选择错误");
            File.WriteAllLines(current, [JsonSerializer.Serialize(new {type="session_meta", payload=new {id="replacement",cwd="F:\\Projects\\Replacement",source="vscode"}}),
                CreateCodexTokenCount(now.AddSeconds(6), 80, 100, 18, 10080, null)]);
            var replaced = collector.Read(now.AddSeconds(6));
            Equal("Replacement", replaced.ProjectName, "截断替换后必须刷新元数据而非沿用旧 cwd");
            Near(80, replaced.ContextUsedPercent, "截断替换后没有重读 Token");
            File.Delete(current);
            Equal("Previous", collector.Read(now.AddSeconds(12)).ProjectName, "已删除会话的缓存不应继续被选中");
        }
        finally { Directory.Delete(root, true); }
    }
}
