using LibreHardwareMonitor.Hardware;

internal static partial class SmokeTests
{
    static void StorageInitializationStopsRemovableMediaPolling()
    {
        Type storageGroupType = typeof(Computer).Assembly.GetType(
            "LibreHardwareMonitor.Hardware.Storage.StorageGroup",
            throwOnError: true)!;
        Type diskProviderType = typeof(Func<List<DiskInfoToolkit.StorageDevice>>);
        var constructor = storageGroupType.GetConstructor(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            [typeof(ISettings), diskProviderType, typeof(Action)],
            modifiers: null);

        True(constructor is not null, "StorageGroup 缺少可验证的启动枚举入口");

        bool monitoringStopped = false;
        constructor!.Invoke(
        [
            new NoOpSettings(),
            new Func<List<DiskInfoToolkit.StorageDevice>>(() => []),
            new Action(() => monitoringStopped = true)
        ]);

        True(monitoringStopped, "StorageGroup 初始化后仍保留可移动介质轮询");
    }

    private sealed class NoOpSettings : ISettings
    {
        public bool Contains(string name) => false;

        public string GetValue(string name, string value) => value;

        public void Remove(string name)
        { }

        public void SetValue(string name, string value)
        { }
    }
}
