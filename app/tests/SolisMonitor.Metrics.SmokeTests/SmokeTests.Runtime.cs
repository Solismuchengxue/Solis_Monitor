internal static partial class SmokeTests
{
static void SolisRuntimeLifecycleIsBounded()
{
    string directory = Path.Combine(
        Path.GetTempPath(),
        $"SolisMonitor.Runtime-{Guid.NewGuid():N}");
    string codexRoot = Path.Combine(directory, "codex");
    Directory.CreateDirectory(codexRoot);

    var listener = new System.Net.Sockets.TcpListener(
        IPAddress.Loopback,
        0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();

    try
    {
        var runtime = new SolisRuntime(
            string.Empty,
            directory,
            codexRoot,
            "127.0.0.1",
            port);

        runtime.Start();
        runtime.Start();

        bool published = SpinWait.SpinUntil(
            () => runtime.CurrentMetrics.Sequence > 0,
            TimeSpan.FromSeconds(10));
        True(published, "运行时启动后没有发布指标");

        runtime.Dispose();
        ulong sequenceAfterDispose = runtime.CurrentMetrics.Sequence;
        Thread.Sleep(TimeSpan.FromMilliseconds(1200));
        Equal(
            sequenceAfterDispose,
            runtime.CurrentMetrics.Sequence,
            "运行时释放后仍在发布指标");

        runtime.Dispose();
        bool startRejected = false;
        try
        {
            runtime.Start();
        }
        catch (ObjectDisposedException)
        {
            startRejected = true;
        }

        True(startRejected, "已释放的运行时不应重新启动");
    }
    finally
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }
}
}
