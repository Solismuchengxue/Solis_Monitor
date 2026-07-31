internal static partial class SmokeTests
{
static void QWeatherForecastIsParsed()
{
    var handler = new QueueHttpMessageHandler(
        GzipJsonResponse("""{"code":"200","location":[{"name":"大连","id":"101070201","country":"中国"}]}"""),
        """{"code":"200","now":{"text":"阴","icon":"104","windDir":"东南风","windScale":"4"}}""",
        """{"code":"200","daily":[{"tempMax":"31","tempMin":"24","textDay":"晴"}]}""");
    using var client = new HttpClient(handler);
    var collector = new QWeatherMetricsCollector(
        new QWeatherSettings(true, "md3h2ew6qe.re.qweatherapi.com", "test-secret", "大连", null),
        client);

    WeatherMetricsReading reading = collector.Read(DateTimeOffset.FromUnixTimeSeconds(100));

    True(reading.Available, $"天气数据应可用：{reading.ErrorCategory}");
    Equal("大连", reading.Location, "地点解析错误");
    Equal("阴", reading.Description, "当前天气描述解析错误");
    Near(24, reading.OutdoorLowC, "最低温解析错误");
    Near(31, reading.OutdoorHighC, "最高温解析错误");
    Equal("东南风", reading.WindDirection, "风向解析错误");
    Equal("4", reading.WindScale, "风力等级解析错误");
    Equal(2, reading.IconIndex, "阴天图标映射错误");
    Equal(3, handler.Requests.Count, "首次采集应执行城市解析、实况和预报请求");
    True(handler.Requests.All(request => request.ApiKey == "test-secret"),
        "API Key 未通过 X-QW-Api-Key 请求头发送");
    True(handler.Requests.All(request => !request.Uri.Contains("test-secret", StringComparison.Ordinal)),
        "API Key 不应出现在 URL 中");
    True(handler.Requests[0].Uri.Contains("/geo/v2/city/lookup?location=", StringComparison.Ordinal),
        "城市解析端点错误");
    True(handler.Requests[1].Uri.Contains("/v7/weather/now?location=101070201", StringComparison.Ordinal),
        "天气实况端点或 Location ID 错误");
    True(handler.Requests[2].Uri.Contains("/v7/weather/3d?location=101070201", StringComparison.Ordinal),
        "每日预报端点或 Location ID 错误");
}

static void QWeatherCoordinatesAreUsed()
{
    var handler = new QueueHttpMessageHandler(
        """{"code":"200","location":[{"name":"甘井子","id":"101070211","adm2":"大连","adm1":"辽宁省","country":"中国"}]}""",
        """{"code":"200","now":{"text":"阴","icon":"104","windDir":"东南风","windScale":"4"}}""",
        """{"code":"200","daily":[{"tempMax":"30","tempMin":"23","textDay":"晴"}]}""");
    using var client = new HttpClient(handler);
    var collector = new QWeatherMetricsCollector(
        new QWeatherSettings(
            true,
            "md3h2ew6qe.re.qweatherapi.com",
            "test-secret",
            string.Empty,
            null,
            121.504751,
            38.837286),
        client);

    WeatherMetricsReading reading = collector.Read(DateTimeOffset.FromUnixTimeSeconds(100));

    True(reading.Available, $"经纬度天气数据应可用：{reading.ErrorCategory}");
    Equal("辽宁·大连", reading.Location, "地区名称应只包含省和市");
    Equal("阴", reading.Description, "未使用当前天气描述");
    Near(23, reading.OutdoorLowC, "坐标最低温解析错误");
    Near(30, reading.OutdoorHighC, "坐标最高温解析错误");
    Equal("东南风", reading.WindDirection, "坐标实况风向解析错误");
    Equal("4", reading.WindScale, "坐标实况风力等级解析错误");
    Equal(2, reading.IconIndex, "坐标实况天气图标映射错误");
    Equal(3, handler.Requests.Count, "配置经纬度后应先调用 GeoAPI 自动解析地区");
    True(handler.Requests[0].Uri.Contains("/geo/v2/city/lookup?location=", StringComparison.Ordinal) &&
         handler.Requests[0].Uri.Contains("121.5", StringComparison.Ordinal) &&
         handler.Requests[0].Uri.Contains("38.84", StringComparison.Ordinal),
        "GeoAPI 未使用两位小数的经纬度");
    True(handler.Requests[1].Uri.Contains("/v7/weather/now?location=101070211", StringComparison.Ordinal),
        "实况请求未使用自动解析的 Location ID");
    True(handler.Requests[2].Uri.Contains("/v7/weather/3d?location=101070211", StringComparison.Ordinal),
        "每日预报请求未使用自动解析的 Location ID");
}

static void QWeatherIconMappingIsComplete()
{
    var method = typeof(QWeatherMetricsCollector).GetMethod(
        "MapWeatherIcon",
        System.Reflection.BindingFlags.NonPublic |
        System.Reflection.BindingFlags.Static);
    True(method is not null, "未找到天气图标映射函数");

    var expected = new Dictionary<int, int>
    {
        [100] = 0,
        [101] = 1,
        [102] = 1,
        [103] = 1,
        [104] = 2,
        [150] = 18,
        [151] = 19,
        [152] = 19,
        [153] = 19,
        [300] = 3,
        [301] = 3,
        [302] = 4,
        [303] = 5,
        [304] = 5,
        [305] = 7,
        [306] = 8,
        [307] = 9,
        [308] = 12,
        [309] = 7,
        [310] = 10,
        [311] = 11,
        [312] = 12,
        [313] = 6,
        [314] = 7,
        [315] = 8,
        [316] = 9,
        [317] = 10,
        [318] = 12,
        [350] = 20,
        [351] = 20,
        [399] = 8,
        [400] = 14,
        [401] = 15,
        [402] = 16,
        [403] = 17,
        [404] = 6,
        [405] = 6,
        [406] = 6,
        [407] = 13,
        [408] = 14,
        [409] = 15,
        [410] = 16,
        [456] = 6,
        [457] = 13,
        [499] = 15,
        [500] = 21,
        [501] = 21,
        [502] = 22,
        [503] = 23,
        [504] = 23,
        [507] = 23,
        [508] = 23,
        [509] = 21,
        [510] = 21,
        [511] = 22,
        [512] = 22,
        [513] = 22,
        [514] = 21,
        [515] = 21,
        [900] = 24,
        [901] = 25,
        [999] = 26,
    };

    foreach ((int code, int expectedIndex) in expected)
    {
        object? value = method!.Invoke(null, [code.ToString(CultureInfo.InvariantCulture)]);
        Equal(expectedIndex, (int?)value, $"天气代码 {code} 的图标映射错误");
    }
}

static HttpResponseMessage GzipJsonResponse(string json)
{
    using var compressed = new MemoryStream();
    using (var gzip = new GZipStream(compressed, CompressionMode.Compress, true))
    using (var writer = new StreamWriter(gzip, new UTF8Encoding(false)))
        writer.Write(json);

    var response = new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(compressed.ToArray())
    };
    response.Content.Headers.ContentType = new("application/json");
    response.Content.Headers.ContentEncoding.Add("gzip");
    return response;
}

static void QWeatherSettingsAreLocal()
{
    string directory = Path.Combine(Path.GetTempPath(), $"SolisMonitor.WeatherSettings-{Guid.NewGuid():N}");
    try
    {
        var store = new QWeatherSettingsStore(directory);
        True(!store.Load().Enabled, "缺少本地天气配置时不应发起请求");
        Directory.CreateDirectory(directory);
        File.WriteAllText(store.SettingsPath,
            """{"schema":1,"enabled":true,"apiHost":"md3h2ew6qe.re.qweatherapi.com","apiKey":"local-secret","location":"大连","locationId":"","longitude":121.504751,"latitude":38.837286}""");

        QWeatherSettings settings = store.Load();
        True(settings.Enabled, "本地天气配置未启用");
        Equal("local-secret", settings.ApiKey, "本地 API Key 未加载");
        Equal("大连", settings.Location, "本地地点未加载");
        Equal(null, settings.LocationId, "空 Location ID 应触发 GeoAPI 解析");
        Near(121.504751, settings.Longitude, "经度未加载");
        Near(38.837286, settings.Latitude, "纬度未加载");

        string migratedJson = File.ReadAllText(store.SettingsPath);
        True(!migratedJson.Contains("local-secret", StringComparison.Ordinal),
            "迁移后的天气配置不应包含 API Key 明文");
        True(migratedJson.Contains("\"apiKeyProtected\"", StringComparison.Ordinal),
            "迁移后的天气配置缺少当前用户加密字段");

        QWeatherSettings migrated = store.Load();
        Equal("local-secret", migrated.ApiKey, "当前用户加密的 API Key 无法重新加载");
    }
    finally
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }
}

static void WeatherCacheExpires()
{
    var handler = new QueueHttpMessageHandler(
        """{"code":"200","now":{"text":"晴","icon":"100","windDir":"北风","windScale":"2"}}""",
        """{"code":"200","daily":[{"tempMax":"31","tempMin":"24","textDay":"晴"}]}""",
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.ServiceUnavailable);
    using var client = new HttpClient(handler);
    var collector = new QWeatherMetricsCollector(
        new QWeatherSettings(true, "md3h2ew6qe.re.qweatherapi.com", "test-secret", "大连", "101070201"),
        client);
    DateTimeOffset start = DateTimeOffset.FromUnixTimeSeconds(100);

    WeatherMetricsReading first = collector.Read(start);
    WeatherMetricsReading cached = collector.Read(start.AddHours(1));
    WeatherMetricsReading expired = collector.Read(start.AddHours(4));

    True(first.Available, "首次天气采集应成功");
    True(cached.Available, "短期网络失败应保留最近有效天气");
    Equal("HttpStatus503", cached.ErrorCategory, "缓存状态应保留最新失败诊断");
    True(!expired.Available, "超过三小时的天气缓存应失效");
    Equal("HttpStatus503", expired.ErrorCategory, "失效后应保留失败诊断");
}

static void QWeatherCollectorDisposesOwnedClient()
{
    var collector = new QWeatherMetricsCollector(
        new QWeatherSettings(
            true,
            "md3h2ew6qe.re.qweatherapi.com",
            "test-secret",
            "大连",
            "101070201"));
    var field = typeof(QWeatherMetricsCollector).GetField(
        "_httpClient",
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic);
    var httpClient = (HttpClient?)field?.GetValue(collector);
    True(httpClient is not null, "无法检查天气采集器的 HTTP 客户端");

    collector.Dispose();

    bool disposed = false;
    try
    {
        _ = httpClient!.GetAsync("https://example.invalid/");
    }
    catch (ObjectDisposedException)
    {
        disposed = true;
    }

    True(disposed, "天气采集器未释放自己创建的 HTTP 客户端");
    collector.Dispose();
}

static void WeatherImmediateFailureNotificationIsDebounced()
{
    var monitor = new WeatherFailureMonitor(TimeSpan.FromMinutes(30));
    DateTimeOffset start = DateTimeOffset.FromUnixTimeSeconds(100);

    WeatherFailureNotification? first =
        monitor.Observe(WeatherMetricsReading.Empty("HttpStatus401"), start);
    WeatherFailureNotification? duplicate =
        monitor.Observe(WeatherMetricsReading.Empty("HttpStatus403"), start.AddMinutes(1));
    WeatherFailureNotification? recovered = monitor.Observe(
        new WeatherMetricsReading(true, "大连", "晴", 24, 31, null),
        start.AddMinutes(2));
    WeatherFailureNotification? nextEpisode =
        monitor.Observe(WeatherMetricsReading.Empty("ApiHostInvalid"), start.AddMinutes(3));

    True(first is not null && first.Message.Contains("API Key", StringComparison.Ordinal),
        "认证失败应立即提示检查 API Key");
    Equal(null, duplicate, "同一明确故障期间不应重复通知");
    Equal(null, recovered, "天气恢复时不应弹通知");
    True(nextEpisode is not null && nextEpisode.Message.Contains("Host", StringComparison.Ordinal),
        "恢复后的 Host 故障应开启新的通知周期");
}

static void WeatherNetworkFailureNotificationIsDelayed()
{
    var monitor = new WeatherFailureMonitor(TimeSpan.FromMinutes(30));
    DateTimeOffset start = DateTimeOffset.FromUnixTimeSeconds(200);

    Equal(null,
        monitor.Observe(WeatherMetricsReading.Empty("NetworkError"), start),
        "首次网络失败不应立即通知");
    Equal(null,
        monitor.Observe(WeatherMetricsReading.Empty("Timeout"), start.AddMinutes(29)),
        "网络失败未满三十分钟不应通知");

    WeatherFailureNotification? delayed =
        monitor.Observe(WeatherMetricsReading.Empty("NetworkError"), start.AddMinutes(30));
    True(delayed is not null && delayed.Message.Contains("30 分钟", StringComparison.Ordinal),
        "网络失败满三十分钟后应通知");
    Equal(null,
        monitor.Observe(WeatherMetricsReading.Empty("Timeout"), start.AddMinutes(31)),
        "同一网络故障期间只应通知一次");
    Equal(null,
        monitor.Observe(
            new WeatherMetricsReading(true, "大连", "晴", 24, 31, null),
            start.AddMinutes(32)),
        "网络恢复时不应弹通知");
}

static void SnapshotStorePublishesWeather()
{
    var store = new MetricsSnapshotStore();
    store.UpdateWeather(new WeatherMetricsReading(
        true, "大连", "晴", 24, 31, null, "东南风", "4", 0));
    Equal(0UL, store.Current.Sequence, "天气更新不应提前发布快照");

    store.Publish(
        new NetworkThroughputReading(false, null, null, null, null, "NotSampled"),
        CodexMetricsReading.Empty("NotSampled"),
        DateTimeOffset.FromUnixTimeSeconds(300));

    Equal("大连", store.Current.Weather.Location, "天气地点未写入统一快照");
    Equal("晴", store.Current.Weather.Description, "天气描述未写入统一快照");
    Near(24, store.Current.Weather.OutdoorLowC.Value, "最低温未写入统一快照");
    Near(31, store.Current.Weather.OutdoorHighC.Value, "最高温未写入统一快照");
    Equal("东南风", store.Current.Weather.WindDirection, "风向未写入统一快照");
    Equal("4", store.Current.Weather.WindScale, "风力等级未写入统一快照");
    Equal(0, store.Current.Weather.IconIndex, "天气图标未写入统一快照");
}
}
