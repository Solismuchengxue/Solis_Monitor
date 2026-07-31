internal static partial class SmokeTests
{
static void True(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message}；期望={expected}，实际={actual}");
}

static void Near(double expected, double? actual, string message)
{
    if (actual is null || Math.Abs(expected - actual.Value) > 0.001)
        throw new InvalidOperationException($"{message}；期望={expected}，实际={actual}");
}

static HardwareSnapshot Snapshot(params RawHardwareSensor[] sensors) =>
    new(DateTimeOffset.FromUnixTimeSeconds(100), sensors, Array.Empty<string>());

static RawHardwareSensor Sensor(
    SolisHardwareKind hardwareKind,
    string hardwareId,
    string hardwareName,
    SolisSensorKind sensorKind,
    string sensorName,
    double? value) => new(hardwareKind, hardwareId, hardwareName, sensorKind, sensorName, value);

static string WriteCodexSession(
    string root,
    string id,
    DateTimeOffset timestamp,
    long inputTokens,
    long contextWindow,
    double primaryUsedPercent,
    double primaryWindowMinutes,
    double? secondaryUsedPercent = null,
    int? secondaryWindowMinutes = null,
    bool isSubagent = false,
    object? primaryResetsAt = null,
    string limitId = "codex",
    string? projectName = null,
    string model = "gpt-5.3-codex",
    string effort = "medium",
    long? totalTokens = null)
{
    string path = Path.Combine(root, "sessions", "2026", "07", "21", $"rollout-2026-07-21T00-00-00-{id}.jsonl");
    object? secondary = secondaryUsedPercent.HasValue && secondaryWindowMinutes.HasValue
        ? new { used_percent = secondaryUsedPercent.Value, window_minutes = secondaryWindowMinutes.Value }
        : null;
    string tokenCount = CreateCodexTokenCount(
        timestamp,
        inputTokens,
        contextWindow,
        primaryUsedPercent,
        primaryWindowMinutes,
        primaryResetsAt,
        limitId,
        secondaryUsedPercent,
        secondaryWindowMinutes,
        totalTokens);
    object source = isSubagent ? new { subagent = new { } } : "vscode";
    string sessionMeta = JsonSerializer.Serialize(new
    {
        type = "session_meta",
        payload = new { id, cwd = Path.Combine("F:\\Projects", projectName ?? id), source }
    });
    string turnContext = JsonSerializer.Serialize(new
    {
        type = "turn_context",
        payload = new { model, effort }
    });
    File.WriteAllLines(path, [sessionMeta, "malformed", turnContext, tokenCount]);
    return path;
}

static string CreateCodexTokenCount(
    DateTimeOffset timestamp,
    long inputTokens,
    long contextWindow,
    double primaryUsedPercent,
    double primaryWindowMinutes,
    object? primaryResetsAt,
    string limitId = "codex",
    double? secondaryUsedPercent = null,
    int? secondaryWindowMinutes = null,
    long? totalTokens = null)
{
    object? secondary = secondaryUsedPercent.HasValue && secondaryWindowMinutes.HasValue
        ? new { used_percent = secondaryUsedPercent.Value, window_minutes = secondaryWindowMinutes.Value }
        : null;
    return JsonSerializer.Serialize(new
    {
        timestamp = timestamp.ToString("O"),
        payload = new
        {
            type = "token_count",
            info = new
            {
                last_token_usage = new { input_tokens = inputTokens },
                total_token_usage = new { total_tokens = totalTokens ?? inputTokens },
                model_context_window = contextWindow
            },
            rate_limits = new
            {
                limit_id = limitId,
                primary = new
                {
                    used_percent = primaryUsedPercent,
                    window_minutes = primaryWindowMinutes,
                    resets_at = primaryResetsAt
                },
                secondary
            }
        }
    });
}

private sealed class QueueCounterSource(params NetworkCounterSnapshot[] snapshots) : INetworkCounterSource
{
    private readonly Queue<NetworkCounterSnapshot> _snapshots = new(snapshots);

    public NetworkCounterReadResult ReadSelected() => new(_snapshots.Dequeue(), null);
}

private sealed class QueueHttpMessageHandler(params object[] responses) : HttpMessageHandler
{
    private readonly Queue<object> _responses = new(responses);

    public List<(string Uri, string? ApiKey)> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.Headers.TryGetValues("X-QW-Api-Key", out IEnumerable<string>? values);
        Requests.Add((request.RequestUri?.ToString() ?? string.Empty, values?.SingleOrDefault()));
        object response = _responses.Dequeue();
        if (response is HttpResponseMessage message)
            return Task.FromResult(message);
        if (response is HttpStatusCode statusCode)
            return Task.FromResult(new HttpResponseMessage(statusCode));

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent((string)response)
        });
    }
}

private sealed class BlockingDiscoveryHttpMessageHandler : HttpMessageHandler
{
    private int _activeRequestCount;

    public ManualResetEventSlim Started { get; } = new(false);

    public int ActiveRequestCount => Volatile.Read(ref _activeRequestCount);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _activeRequestCount);
        Started.Set();
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException(
                "阻塞请求不应在未取消时完成");
        }
        finally
        {
            Interlocked.Decrement(ref _activeRequestCount);
        }
    }
}

private sealed class CountingDiscoveryHttpMessageHandler : HttpMessageHandler
{
    private int _requestCount;
    private string? _firstRequestUri;

    public ManualResetEventSlim Started { get; } = new(false);

    public int RequestCount => Volatile.Read(ref _requestCount);

    public string? FirstRequestUri => Volatile.Read(ref _firstRequestUri);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requestCount);
        Interlocked.CompareExchange(
            ref _firstRequestUri,
            request.RequestUri?.ToString(),
            null);
        Started.Set();
        return Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}

private sealed class RecoveringDiscoveryHttpMessageHandler : HttpMessageHandler
{
    private int _requestCount;

    public bool Available { get; set; }

    public int RequestCount => Volatile.Read(ref _requestCount);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requestCount);
        if (!Available)
        {
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }

        const string json =
            """
            {
              "product": "Solis Monitor",
              "hostname": "Solis_Monitor_A1B2",
              "firmware": "0.1.5",
              "ip": "192.168.0.42",
              "rssi": -42,
              "paired": true
            }
            """;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json")
        });
    }
}

private sealed class FirmwareHttpMessageHandler(
    string initialStatus,
    string? restartedStatus,
    bool interruptUpload) : HttpMessageHandler
{
    public string? AuthorizationToken { get; private set; }

    public int RequestCount { get; private set; }

    public int UploadedLength { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestCount++;
        AuthorizationToken = request.Headers.Authorization?.Parameter;
        if (request.Method == HttpMethod.Get)
        {
            string status = RequestCount == 1
                ? initialStatus
                : restartedStatus ?? initialStatus;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(status)
            };
        }

        if (interruptUpload)
            throw new HttpRequestException("simulated interruption");

        byte[] uploaded = await request.Content!.ReadAsByteArrayAsync(
            cancellationToken);
        UploadedLength = uploaded.Length;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"ok":true,"version":"1.2.3"}""")
        };
    }
}

private sealed class DeviceControlHttpMessageHandler(
    string settingsJson) : HttpMessageHandler
{
    public string? AuthorizationToken { get; private set; }

    public int RequestCount { get; private set; }

    public bool AllRequestsCloseConnection { get; private set; } = true;

    public string? SavedBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestCount++;
        AllRequestsCloseConnection &=
            request.Headers.ConnectionClose == true;
        AuthorizationToken = request.Headers.Authorization?.Parameter;
        if (request.Method == HttpMethod.Get)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(settingsJson)
            };
        }

        if (request.RequestUri?.AbsolutePath == "/api/control")
            SavedBody = await request.Content!.ReadAsStringAsync(
                cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"ok":true}""")
        };
    }
}
}
