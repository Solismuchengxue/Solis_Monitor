using LibreHardwareMonitor.Solis.Codex;
using LibreHardwareMonitor.Solis.DeviceApi;
using LibreHardwareMonitor.Solis.Metrics;
using System.Text.Json;
using System.Threading;

internal static partial class SmokeTests
{
    static void CodexAccountCreditsAndResetsAreParsed()
    {
        const string response = """
            {"id":3,"result":{"rateLimits":{"credits":{"balance":"1"}},
              "rateLimitsByLimitId":{"codex":{"credits":{"hasCredits":true,"unlimited":false,"balance":"61925.8984640000"}}},
              "rateLimitResetCredits":{"availableCount":2,"credits":[{"status":"available"}]}}}
            """;
        var reading = CodexAccountUsageReader.ParseAccountLimitsResponse(response);
        Near(61925.898464, reading.CreditBalance, "应优先使用主账户额度余额");
        Equal(2, reading.AvailableFullResets, "应使用权威 availableCount，不以明细条数计算");
    }

    static void CodexAccountCreditsRejectInvalidAndPreserveZero()
    {
        var zero = CodexAccountUsageReader.ParseAccountLimitsResponse(
            """{"id":3,"result":{"rateLimits":{"credits":{"balance":"0"}},"rateLimitResetCredits":{"availableCount":0}}}""");
        Near(0, zero.CreditBalance, "真实零余额必须可用");
        Equal(0, zero.AvailableFullResets, "真实零次必须可用");
        foreach (string balance in new[] { "-1", "NaN", "Infinity", "bad" })
        {
            var invalid = CodexAccountUsageReader.ParseAccountLimitsResponse(
                "{\"id\":3,\"result\":{\"rateLimits\":{\"credits\":{\"balance\":\"" + balance + "\"}},\"rateLimitResetCredits\":{\"availableCount\":-1}}}");
            True(invalid.CreditBalance is null, "非法余额不能成为有效读数");
            True(invalid.AvailableFullResets is null, "负重置次数不能成为有效读数");
        }
        var missing = CodexAccountUsageReader.ParseAccountLimitsResponse("""{"id":3,"result":{}}""");
        True(missing.CreditBalance is null && missing.AvailableFullResets is null, "未知不可显示为 0");
        foreach (string input in new[] { "{", "null", "[]", "{\"id\":3,\"result\":null}",
            "{\"id\":3,\"result\":{\"rateLimits\":null,\"rateLimitResetCredits\":{\"availableCount\":1.5}}}" })
        {
            var invalid = CodexAccountUsageReader.ParseAccountLimitsResponse(input);
            True(invalid.CreditBalance is null && invalid.AvailableFullResets is null,
                "缺失和畸形响应必须降级为未知");
        }
    }

    static void CodexCreditsFlowToDeviceWithoutChangingMainQuota()
    {
        var store = new MetricsSnapshotStore();
        var now = DateTimeOffset.UtcNow;
        var reading = CodexMetricsReading.Empty() with
        {
            MainQuota = new("主周额度", 81, "10-20 11:37"),
            CreditBalance = 61925.898464,
            AvailableFullResets = 2
        };
        foreach (var sample in new[] { reading, reading with { CreditBalance = 0, AvailableFullResets = 0 },
                     reading with { CreditBalance = null, AvailableFullResets = null },
                     reading with { CreditBalance = null }, reading with { AvailableFullResets = null } })
        {
            store.Publish(default, sample, now);
            byte[] payload = DeviceMetricsEnvelope.FromSnapshot(store.Current, now).Serialize();
            True(payload.Length <= DeviceMetricsEnvelope.MaximumPayloadBytes, "新字段不能超出设备载荷限制");
            using var document = JsonDocument.Parse(payload);
            var codex = document.RootElement.GetProperty("codex");
            Near(81, codex.GetProperty("main_weekly_remaining").GetDouble(), "主周额度不能改变");
            Equal("10-20 11:37", codex.GetProperty("main_quota_reset_at").GetString(), "主周重置时间不能改变");
            if (sample.CreditBalance is double balance)
            {
                Near(balance, codex.GetProperty("credit_balance").GetDouble(), "余额必须正确传递，包含零");
            }
            else
            {
                True(!codex.TryGetProperty("credit_balance", out _), "未知余额必须省略字段以保持旧样例兼容");
            }
            if (sample.AvailableFullResets is int resets)
            {
                Equal(resets, codex.GetProperty("full_resets_available").GetInt32(), "次数必须正确传递，包含零");
            }
            else
            {
                True(!codex.TryGetProperty("full_resets_available", out _), "未知次数必须省略字段以保持旧样例兼容");
            }
        }
    }

    static void CodexCreditRefreshPreservesZeroAndClearsFailures()
    {
        string root = Path.Combine(Path.GetTempPath(), $"SolisMonitor.Credits-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "sessions"));
        int mode = 0;
        var now = DateTimeOffset.UtcNow;
        try
        {
            var collector = new CodexMetricsCollector(root, TimeSpan.FromMinutes(10), () =>
                Volatile.Read(ref mode) switch
                {
                    0 => new CodexAccountReading(100, 61925.898464, 2),
                    1 => new CodexAccountReading(100, 0, 0),
                    _ => throw new IOException("synthetic read failure")
                }, null);
            CodexMetricsReading WaitFor(Func<CodexMetricsReading, bool> expected)
            {
                DateTime deadline = DateTime.UtcNow.AddSeconds(5);
                CodexMetricsReading result;
                do
                {
                    result = collector.Read(now);
                    if (expected(result)) return result;
                    Thread.Sleep(10);
                } while (DateTime.UtcNow < deadline);
                throw new InvalidOperationException("账户额度后台刷新未在限时内完成");
            }
            collector.Read(now);
            WaitFor(value => value.CreditBalance.HasValue && value.AvailableFullResets == 2);
            Volatile.Write(ref mode, 1);
            collector.Read(now.AddMinutes(16));
            WaitFor(value => value.CreditBalance == 0 && value.AvailableFullResets == 0);
            Volatile.Write(ref mode, 2);
            collector.Read(now.AddMinutes(32));
            var failed = WaitFor(value => value.CreditBalance is null && value.AvailableFullResets is null);
            Near(100, failed.TotalTokens, "额度读取失败不应清除既有累计 Token");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
