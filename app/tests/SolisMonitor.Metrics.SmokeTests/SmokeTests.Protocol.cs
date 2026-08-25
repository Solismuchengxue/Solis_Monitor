internal static partial class SmokeTests
{
static void LegacySensorQueryStringIsParsed()
{
    IDictionary<string, string> parsed = HttpServer.ParseQueryString(
        "?action=Set&id=%2Fcpu%2F0&value=42%2E5&name=Solis+Monitor&tag=one&tag=two");

    Equal("Set", parsed["action"], "action 参数应保留");
    Equal("/cpu/0", parsed["id"], "百分号编码应解码");
    Equal("42.5", parsed["value"], "数值参数应解码");
    Equal("Solis Monitor", parsed["name"], "加号应按空格解析");
    Equal("one,two", parsed["tag"], "重复参数应保持旧版逗号合并语义");
}

static void SharedSchemaFixtureIsCompatible()
{
    string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "schema1",
                               "metrics_complete.json");
    string fixture = File.ReadAllText(path);
    DeviceMetricsEnvelope? envelope = JsonSerializer.Deserialize<DeviceMetricsEnvelope>(fixture);
    True(envelope is not null, "共享协议样例无法反序列化为桌面端协议模型");

    using JsonDocument expected = JsonDocument.Parse(fixture);
    using JsonDocument actual = JsonDocument.Parse(envelope!.Serialize());
    True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement),
         "桌面端协议模型往返序列化改变了共享样例");
}

static void DeviceApiResponseIsCompatible()
{
    const string token = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    var store = new MetricsSnapshotStore();
    store.UpdateHardware(new MappedHardwareMetrics(
        45, 63, 4.8, 95,
        81, 74, 2.6, 245,
        60, 7372.8, 12288, 78,
        42, 52, 40, 132,
        "CPU", "GPU", "/gpu-nvidia/0", "/nvme/0", new[] { "NVMe A" },
        MemoryUsedGb: 12,
        MemoryTotalGb: 32,
        StorageDevices:
        [
            new MappedStorageDevice("/nvme/0", "NVMe A", 65, 40),
            new MappedStorageDevice("/hdd/1", "HDD B", 72, 36)
        ]));
    store.Publish(
        new NetworkThroughputReading(true, 128, 24, "ethernet", "以太网", null),
        new CodexMetricsReading(
            true,
            "Solis_Monitor",
            25,
            25.0,
            100,
            4,
            new CodexQuotaReading("主周额度", 4, "07-21 23:59"),
            new CodexQuotaReading("GPT-5.3-Codex-Spark", 97, "07-21 23:00"),
            null,
            ProjectName: "Solis_Monitor",
            Model: "gpt-5.6-sol",
            ReasoningEffort: "high",
            TotalTokens: 123456,
            WeeklyUsedTokens: 45678),
        DateTimeOffset.FromUnixTimeSeconds(100));

    DeviceMetricsResponse missing = DeviceMetricsServer.CreateResponse(
        "GET", DeviceMetricsServer.MetricsPath, null, token, store.Current, DateTimeOffset.Now);
    Equal(HttpStatusCode.Unauthorized, missing.StatusCode, "缺少令牌时没有返回 401");
    Equal(0, missing.Payload.Length, "401 响应不应泄露正文");

    DeviceMetricsResponse wrongPath = DeviceMetricsServer.CreateResponse(
        "GET", "/metrics", $"Bearer {token}", token, store.Current, DateTimeOffset.Now);
    Equal(HttpStatusCode.NotFound, wrongPath.StatusCode, "非设备 API 路径没有返回 404");

    DateTime localDeviceTime = new(2026, 7, 21, 15, 30, 0, DateTimeKind.Unspecified);
    DateTimeOffset deviceTime = new(
        localDeviceTime,
        TimeZoneInfo.Local.GetUtcOffset(localDeviceTime));
    DeviceMetricsResponse response = DeviceMetricsServer.CreateResponse(
        "GET",
        DeviceMetricsServer.MetricsPath,
        $"Bearer {token}",
        token,
        store.Current,
        deviceTime);
    Equal(HttpStatusCode.OK, response.StatusCode, "正确令牌未返回 200");
    True(response.NoStore, "设备指标响应必须禁止缓存");
    True(response.Payload.Length <= DeviceMetricsEnvelope.MaximumPayloadBytes, "设备指标响应超过固件 4096 字节上限");

    using JsonDocument json = JsonDocument.Parse(response.Payload);
    JsonElement root = json.RootElement;
    Equal(1, root.GetProperty("schema").GetInt32(), "协议 schema 改变");
    Equal(1UL, root.GetProperty("sequence").GetUInt64(), "协议 sequence 错误");
    Equal(100L, root.GetProperty("generated_at").GetInt64(), "协议 generated_at 错误");
    JsonElement system = root.GetProperty("system");
    Equal("15:30", system.GetProperty("time").GetString(), "设备时间格式错误");
    Near(2.6, system.GetProperty("gpu_ghz").GetDouble(), "旧固件 GPU 主频字段丢失");
    Near(60, system.GetProperty("gpu_memory_usage").GetDouble(), "新增显存占用字段错误");
    Near(78, system.GetProperty("gpu_memory_temp_c").GetDouble(), "新增显存温度字段错误");
    Near(52, system.GetProperty("memory_temp_c").GetDouble(), "新增内存温度字段错误");
    Equal("CPU", system.GetProperty("cpu_name").GetString(), "CPU 名称字段错误");
    Equal("GPU", system.GetProperty("gpu_name").GetString(), "GPU 名称字段错误");
    Near(12, system.GetProperty("memory_used_gb").GetDouble(), "内存已用字段错误");
    Near(32, system.GetProperty("memory_total_gb").GetDouble(), "内存总量字段错误");
    Equal(2, system.GetProperty("storage_devices").GetArrayLength(), "物理硬盘数组错误");
    Equal("以太网", system.GetProperty("network_name").GetString(), "活跃网卡名称字段错误");
    Near(128, system.GetProperty("download_mbps").GetDouble(), "下载速度字段错误");
    JsonElement codex = root.GetProperty("codex");
    Equal("Solis_Monitor", codex.GetProperty("project").GetString(), "Codex 任务名称字段错误");
    Equal("gpt-5.6-sol", codex.GetProperty("model").GetString(), "Codex 模型字段错误");
    Equal("high", codex.GetProperty("reasoning_effort").GetString(), "Codex 推理强度字段错误");
    Near(123456, codex.GetProperty("total_tokens").GetDouble(), "Codex 累计 Token 字段错误");
    Near(45678, codex.GetProperty("weekly_used_tokens").GetDouble(), "Codex 周使用 Token 字段错误");
    Near(25, codex.GetProperty("context_used").GetDouble(), "Codex 上下文字段错误");
    Near(25, codex.GetProperty("context_used_k").GetDouble(), "Codex 上下文已用(k)字段错误");
    Near(100, codex.GetProperty("context_window_k").GetDouble(), "Codex 上下文上限(k)字段错误");
    Near(4, codex.GetProperty("weekly_remaining").GetDouble(), "Codex 周余额字段错误");
    Near(4, codex.GetProperty("main_weekly_remaining").GetDouble(), "主周额度字段错误");
    Equal("主周额度", codex.GetProperty("main_quota_name").GetString(), "主周额度名称错误");
    Near(97, codex.GetProperty("spark_weekly_remaining").GetDouble(), "Spark 周额度字段错误");
    Equal("GPT-5.3-Codex-Spark", codex.GetProperty("spark_quota_name").GetString(), "Spark 名称错误");
}

static void DeviceApiRecordsLastSuccessfulCommunication()
{
    const string token = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();

    using var server = new DeviceMetricsServer(
        () => SolisMetricsSnapshot.Empty,
        token,
        "127.0.0.1",
        port);
    True(server.Start(), "设备 API 测试监听器启动失败");
    True(server.LastSuccessfulCommunicationAt is null, "尚未请求时不应有最近通信时间");

    using var client = new HttpClient();
    using var unauthorized = client.GetAsync(
        $"http://127.0.0.1:{port}{DeviceMetricsServer.MetricsPath}")
        .GetAwaiter().GetResult();
    Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode, "无令牌请求状态错误");
    True(server.LastSuccessfulCommunicationAt is null, "鉴权失败不应计为成功通信");

    using var request = new HttpRequestMessage(
        HttpMethod.Get,
        $"http://127.0.0.1:{port}{DeviceMetricsServer.MetricsPath}");
    request.Headers.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    using HttpResponseMessage response = client.Send(request);
    Equal(HttpStatusCode.OK, response.StatusCode, "带令牌请求状态错误");
    True(server.LastSuccessfulCommunicationAt is not null, "成功返回指标后没有记录最近通信");
}
}
