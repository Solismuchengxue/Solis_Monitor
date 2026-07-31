#nullable enable

using System;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Solis.Metrics;
using LibreHardwareMonitor.Solis.Security;

namespace LibreHardwareMonitor.Solis.DeviceApi;

public sealed class DeviceMetricsServer : IDisposable
{
    public const int DefaultPort = 18472;
    public const string MetricsPath = "/api/v1/metrics";

    private readonly Func<string> _deviceTokenProvider;
    private readonly DeviceAuthorizationTracker _authorizationTracker = new();
    private readonly HttpListener _listener = new() { IgnoreWriteExceptions = true };
    private readonly Func<SolisMetricsSnapshot> _snapshotProvider;
    private long _lastSuccessfulCommunicationUnixMilliseconds = -1;
    private CancellationTokenSource? _cancellation;
    private Task? _listenerTask;

    public DeviceMetricsServer(
        Func<SolisMetricsSnapshot> snapshotProvider,
        string deviceToken,
        string listenerHost = "+",
        int listenerPort = DefaultPort)
        : this(
            snapshotProvider,
            () => deviceToken,
            listenerHost,
            listenerPort)
    {
    }

    public DeviceMetricsServer(
        Func<SolisMetricsSnapshot> snapshotProvider,
        Func<string> deviceTokenProvider,
        string listenerHost = "+",
        int listenerPort = DefaultPort)
    {
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
        _deviceTokenProvider = deviceTokenProvider ??
                               throw new ArgumentNullException(nameof(deviceTokenProvider));
        if (!DeviceToken.IsValid(_deviceTokenProvider()))
            throw new ArgumentException(
                "Device token provider must return exactly 64 hexadecimal characters.",
                nameof(deviceTokenProvider));
        if (string.IsNullOrWhiteSpace(listenerHost))
            throw new ArgumentException("Listener host is required.", nameof(listenerHost));
        if (listenerPort < 1 || listenerPort > 65535)
            throw new ArgumentOutOfRangeException(nameof(listenerPort));

        Prefix = $"http://{listenerHost}:{listenerPort}/";
    }

    public string Prefix { get; }

    public bool IsRunning => _listener.IsListening;

    public DeviceAuthorizationState AuthorizationState => _authorizationTracker.Current;

    public DateTimeOffset? LastSuccessfulCommunicationAt
    {
        get
        {
            long value = Interlocked.Read(
                ref _lastSuccessfulCommunicationUnixMilliseconds);
            return value < 0
                ? null
                : DateTimeOffset.FromUnixTimeMilliseconds(value);
        }
    }

    public event Action<DeviceAuthorizationState>? AuthorizationObserved;

    public string? LastErrorCategory { get; private set; }

    public static DeviceMetricsResponse CreateResponse(
        string method,
        string path,
        string? authorization,
        string expectedToken,
        SolisMetricsSnapshot snapshot,
        DateTimeOffset currentTime)
    {
        if (!string.Equals(path, MetricsPath, StringComparison.Ordinal))
            return DeviceMetricsResponse.Empty(HttpStatusCode.NotFound);

        if (!string.Equals(method, "GET", StringComparison.Ordinal))
            return DeviceMetricsResponse.Empty(HttpStatusCode.MethodNotAllowed);

        if (!DeviceToken.IsAuthorized(authorization, expectedToken))
            return DeviceMetricsResponse.Empty(HttpStatusCode.Unauthorized);

        byte[] payload = DeviceMetricsEnvelope.FromSnapshot(snapshot, currentTime).Serialize();
        return payload.Length <= DeviceMetricsEnvelope.MaximumPayloadBytes
            ? new DeviceMetricsResponse(HttpStatusCode.OK, payload, "application/json; charset=utf-8", true)
            : DeviceMetricsResponse.Empty(HttpStatusCode.ServiceUnavailable);
    }

    public bool Start()
    {
        if (_listener.IsListening)
            return true;

        try
        {
            _listener.Prefixes.Clear();
            _listener.Prefixes.Add(Prefix);
            _listener.Start();
            _cancellation = new CancellationTokenSource();
            _listenerTask = Task.Run(() => ProcessRequestsAsync(_cancellation.Token));
            LastErrorCategory = null;
            return true;
        }
        catch (Exception exception) when (
            exception is HttpListenerException ||
            exception is InvalidOperationException)
        {
            LastErrorCategory = exception.GetType().Name;
            return false;
        }
    }

    public void Stop()
    {
        _cancellation?.Cancel();
        if (_listener.IsListening)
        {
            try
            {
                _listener.Stop();
            }
            catch (HttpListenerException)
            {
            }
        }

        try
        {
            _listenerTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _listenerTask = null;
        _cancellation?.Dispose();
        _cancellation = null;
    }

    public void Dispose()
    {
        Stop();
        _listener.Close();
    }

    private async Task ProcessRequestsAsync(CancellationToken cancellationToken)
    {
        while (_listener.IsListening && !cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested || !_listener.IsListening)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception exception)
            {
                LastErrorCategory = exception.GetType().Name;
                Debug.WriteLine($"Solis device API listener error: {exception.Message}");
                continue;
            }

            await HandleContextAsync(context).ConfigureAwait(false);
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context)
    {
        try
        {
            HttpListenerRequest request = context.Request;
            DeviceMetricsResponse result = CreateResponse(
                request.HttpMethod,
                request.Url?.AbsolutePath ?? string.Empty,
                request.Headers["Authorization"],
                _deviceTokenProvider(),
                _snapshotProvider(),
                DateTimeOffset.Now);
            if (result.StatusCode is HttpStatusCode.OK or HttpStatusCode.Unauthorized &&
                request.RemoteEndPoint?.Address is IPAddress remoteAddress) {
                _authorizationTracker.Observe(
                    remoteAddress,
                    result.StatusCode == HttpStatusCode.OK,
                    DateTimeOffset.Now);
                AuthorizationObserved?.Invoke(_authorizationTracker.Current);
            }

            context.Response.StatusCode = (int)result.StatusCode;
            if (result.StatusCode == HttpStatusCode.ServiceUnavailable)
                LastErrorCategory = "MetricsPayloadTooLarge";

            if (result.Payload.Length == 0)
                return;

            if (result.NoStore)
                context.Response.Headers[HttpResponseHeader.CacheControl] = "no-store";
            context.Response.ContentType = result.ContentType;
            context.Response.ContentEncoding = Encoding.UTF8;
            context.Response.ContentLength64 = result.Payload.Length;
            if (result.StatusCode == HttpStatusCode.OK)
            {
                Interlocked.Exchange(
                    ref _lastSuccessfulCommunicationUnixMilliseconds,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            }

            await context.Response.OutputStream.WriteAsync(
                result.Payload,
                0,
                result.Payload.Length).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is HttpListenerException ||
            exception is ObjectDisposedException)
        {
            LastErrorCategory = exception.GetType().Name;
        }
        finally
        {
            try
            {
                context.Response.Close();
            }
            catch
            {
            }
        }
    }
}

public sealed record DeviceMetricsResponse(
    HttpStatusCode StatusCode,
    byte[] Payload,
    string? ContentType,
    bool NoStore)
{
    public static DeviceMetricsResponse Empty(HttpStatusCode statusCode) =>
        new(statusCode, Array.Empty<byte>(), null, false);
}
