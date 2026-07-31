internal static partial class SmokeTests
{
static void ManualSelectionWins()
{
    NetworkAdapterOption[] adapters =
    [
        new("ethernet", "以太网", "Ethernet", 10, true),
        new("wifi", "Wi-Fi", "Wireless", 20, true)
    ];

    NetworkAdapterSelection result = NetworkAdapterSelector.Select(adapters, "wifi", 10);
    Equal("wifi", result.Adapter?.Id, "手动选择未覆盖自动出口");
}

static void BestInterfaceSelectionWins()
{
    NetworkAdapterOption[] adapters =
    [
        new("ethernet", "以太网", "Ethernet", 10, true),
        new("wifi", "Wi-Fi", "Wireless", 20, true)
    ];

    NetworkAdapterSelection result = NetworkAdapterSelector.Select(adapters, null, 20);
    Equal("wifi", result.Adapter?.Id, "未选择 GetBestInterface 返回的出口");
}

static void GatewayFallbackWorks()
{
    NetworkAdapterOption[] adapters =
    [
        new("isolated", "内部网络", "Virtual", 10, false),
        new("ethernet", "以太网", "Ethernet", 20, true)
    ];

    NetworkAdapterSelection result = NetworkAdapterSelector.Select(adapters, null, null);
    Equal("ethernet", result.Adapter?.Id, "系统出口查询失败后未回退到默认网关网卡");
}

static void ThroughputIsCalculated()
{
    var source = new QueueCounterSource(
        new("ethernet", "以太网", 1_000, 2_000, 2_500_000_000),
        new("ethernet", "以太网", 125_001_000, 62_502_000, 2_500_000_000));
    var collector = new NetworkThroughputCollector(source);

    NetworkThroughputReading first = collector.Read(Stopwatch.Frequency);
    Equal("FirstSample", first.ErrorCategory, "第一次采样应只建立基线");

    NetworkThroughputReading second = collector.Read(2 * Stopwatch.Frequency);
    True(second.Available, "第二次采样应可用");
    Near(1000D, second.DownloadMbps, "下载速度错误");
    Near(500D, second.UploadMbps, "上传速度错误");
}

static void InterfaceChangeRebaselines()
{
    var source = new QueueCounterSource(
        new("ethernet", "以太网", 1_000, 2_000, 1_000_000_000),
        new("wifi", "Wi-Fi", 3_000, 4_000, 1_000_000_000));
    var collector = new NetworkThroughputCollector(source);

    collector.Read(Stopwatch.Frequency);
    NetworkThroughputReading changed = collector.Read(2 * Stopwatch.Frequency);
    Equal("InterfaceChanged", changed.ErrorCategory, "切换网卡后应重建基线");
    True(!changed.Available, "切换网卡的当次采样不应产生错误速度");
}

static void SnapshotStorePublishesOneCompleteCycle()
{
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

    Equal(0UL, store.Current.Sequence, "硬件采集不应提前发布快照");
    True(!store.Current.Cpu.UsagePercent.Available, "发布前不应泄露待发布硬件值");

    store.Publish(
        new NetworkThroughputReading(true, 12.5, 3.25, "wifi", "Wi-Fi", null),
        new CodexMetricsReading(true, "Solis_Monitor", 25, 12.5, 128, 4, null, null, null),
        DateTimeOffset.FromUnixTimeSeconds(100));

    SolisMetricsSnapshot first = store.Current;
    Equal(1UL, first.Sequence, "完整周期应只递增一次序号");
    Equal(100L, first.GeneratedAtUnixSeconds, "完整周期时间错误");
    Near(45, first.Cpu.UsagePercent.Value, "完整周期没有包含最新硬件值");
    Near(12.5, first.Network.DownloadMbps.Value, "完整周期没有包含网络值");
    Equal("Solis_Monitor", first.Codex.LastActiveTask, "完整周期没有包含 Codex 值");

    store.UpdateHardware(new MappedHardwareMetrics(
        55, 64, 4.9, 96,
        82, 75, 2.7, 246,
        61, 7495.68, 12288, 79,
        43, 53, 41, 133,
        "CPU", "GPU", "/gpu-nvidia/0", "/nvme/0", new[] { "NVMe A" }));

    Equal(1UL, store.Current.Sequence, "采集更新不应产生第二次发布");
    Near(45, store.Current.Cpu.UsagePercent.Value, "发布前当前快照不应混入新硬件值");

    store.Publish(
        new NetworkThroughputReading(true, 13.5, 4.25, "wifi", "Wi-Fi", null),
        new CodexMetricsReading(true, "Solis_Monitor_2", 30, 15, 128, 3, null, null, null),
        DateTimeOffset.FromUnixTimeSeconds(101));

    SolisMetricsSnapshot second = store.Current;
    Equal(2UL, second.Sequence, "第二个完整周期应只再递增一次序号");
    Equal(101L, second.GeneratedAtUnixSeconds, "第二个完整周期时间错误");
    Near(55, second.Cpu.UsagePercent.Value, "第二个周期没有原子带入新硬件值");
    Near(13.5, second.Network.DownloadMbps.Value, "第二个周期没有带入新网络值");
    Equal("Solis_Monitor_2", second.Codex.LastActiveTask, "第二个周期没有带入新 Codex 值");
}

static void SnapshotStoreConcurrentUpdatesKeepSingleSequence()
{
    const int publishCount = 200;
    var store = new MetricsSnapshotStore();

    Task hardwareTask = Task.Run(() =>
    {
        for (int index = 0; index < publishCount; index++)
        {
            store.UpdateHardware(new MappedHardwareMetrics(
                index % 100, 63, 4.8, 95,
                81, 74, 2.6, 245,
                60, 7372.8, 12288, 78,
                42, 52, 40, 132,
                "CPU", "GPU", "/gpu-nvidia/0", "/nvme/0", new[] { "NVMe A" }));
        }
    });

    Task publisherTask = Task.Run(() =>
    {
        for (int index = 0; index < publishCount; index++)
        {
            store.Publish(
                new NetworkThroughputReading(true, index, index / 2D, "wifi", "Wi-Fi", null),
                new CodexMetricsReading(true, $"Task-{index}", index % 100, index, 128, 100 - index % 100,
                    null, null, null),
                DateTimeOffset.FromUnixTimeSeconds(1000 + index));
        }
    });

    Task.WaitAll(hardwareTask, publisherTask);

    Equal((ulong)publishCount, store.Current.Sequence, "并发硬件采集产生了额外发布或丢失序号");
    Equal(1000L + publishCount - 1, store.Current.GeneratedAtUnixSeconds,
        "并发发布没有保留最后一个周期时间");
    Equal($"Task-{publishCount - 1}", store.Current.Codex.LastActiveTask,
        "并发发布没有保留最后一个完整周期的 Codex 值");
}

static void CpuMetricsAreMapped()
{
    HardwareSnapshot snapshot = Snapshot(
        Sensor(SolisHardwareKind.Cpu, "/intelcpu/0", "CPU", SolisSensorKind.Load, "CPU Total", 42),
        Sensor(SolisHardwareKind.Cpu, "/intelcpu/0", "CPU", SolisSensorKind.Temperature, "CPU Package", 63),
        Sensor(SolisHardwareKind.Cpu, "/intelcpu/0", "CPU", SolisSensorKind.Clock, "CPU Core #1", 4800),
        Sensor(SolisHardwareKind.Cpu, "/intelcpu/0", "CPU", SolisSensorKind.Clock, "CPU Core #2", 4600),
        Sensor(SolisHardwareKind.Cpu, "/intelcpu/0", "CPU", SolisSensorKind.Power, "CPU Package", 95));

    MappedHardwareMetrics metrics = HardwareMetricMapper.Map(snapshot);
    Near(42, metrics.CpuUsagePercent, "CPU 使用率错误");
    Near(63, metrics.CpuTemperatureC, "CPU 温度错误");
    Near(4.7, metrics.CpuClockGhz, "CPU 平均主频错误");
    Near(95, metrics.CpuPowerW, "CPU 功耗错误");
    Equal("CPU", metrics.CpuName, "CPU 名称错误");
}

static void IntelHybridCpuClockIsMapped()
{
    HardwareSnapshot snapshot = Snapshot(
        Sensor(SolisHardwareKind.Cpu, "/intelcpu/0", "Intel CPU", SolisSensorKind.Clock, "Bus Speed", 100),
        Sensor(SolisHardwareKind.Cpu, "/intelcpu/0", "Intel CPU", SolisSensorKind.Clock, "P-Core #1", 5200),
        Sensor(SolisHardwareKind.Cpu, "/intelcpu/0", "Intel CPU", SolisSensorKind.Clock, "P-Core #2", 5000),
        Sensor(SolisHardwareKind.Cpu, "/intelcpu/0", "Intel CPU", SolisSensorKind.Clock, "E-Core #1", 4000),
        Sensor(SolisHardwareKind.Cpu, "/intelcpu/0", "Intel CPU", SolisSensorKind.Clock, "E-Core #2", 3800));

    MappedHardwareMetrics metrics = HardwareMetricMapper.Map(snapshot);
    Near(4.5, metrics.CpuClockGhz, "P-Core/E-Core 平均主频映射错误");
}

static void DiscreteGpuAndVramMetricsAreMapped()
{
    HardwareSnapshot snapshot = Snapshot(
        Sensor(SolisHardwareKind.GpuIntel, "/gpu-intel/0", "Intel Graphics", SolisSensorKind.Load, "GPU Core", 12),
        Sensor(SolisHardwareKind.GpuNvidia, "/gpu-nvidia/0", "NVIDIA RTX", SolisSensorKind.Load, "GPU Core", 70),
        Sensor(SolisHardwareKind.GpuNvidia, "/gpu-nvidia/0", "NVIDIA RTX", SolisSensorKind.Temperature, "GPU Core", 74),
        Sensor(SolisHardwareKind.GpuNvidia, "/gpu-nvidia/0", "NVIDIA RTX", SolisSensorKind.Clock, "GPU Core", 2600),
        Sensor(SolisHardwareKind.GpuNvidia, "/gpu-nvidia/0", "NVIDIA RTX", SolisSensorKind.Power, "GPU Package", 245),
        Sensor(SolisHardwareKind.GpuNvidia, "/gpu-nvidia/0", "NVIDIA RTX", SolisSensorKind.Load, "GPU Memory", 60),
        Sensor(SolisHardwareKind.GpuNvidia, "/gpu-nvidia/0", "NVIDIA RTX", SolisSensorKind.SmallData, "GPU Memory Used", 7372.8),
        Sensor(SolisHardwareKind.GpuNvidia, "/gpu-nvidia/0", "NVIDIA RTX", SolisSensorKind.SmallData, "GPU Memory Total", 12288),
        Sensor(SolisHardwareKind.GpuNvidia, "/gpu-nvidia/0", "NVIDIA RTX", SolisSensorKind.Temperature, "GPU Memory", 66),
        Sensor(SolisHardwareKind.GpuNvidia, "/gpu-nvidia/0", "NVIDIA RTX", SolisSensorKind.Temperature, "GPU Memory Junction", 78));

    MappedHardwareMetrics metrics = HardwareMetricMapper.Map(snapshot);
    Equal("/gpu-nvidia/0", metrics.SelectedGpuId, "默认未优先选择 NVIDIA 独显");
    Near(70, metrics.GpuUsagePercent, "GPU 使用率错误");
    Near(74, metrics.GpuCoreTemperatureC, "GPU 核心温度错误");
    Near(2.6, metrics.GpuCoreClockGhz, "GPU 核心频率错误");
    Near(245, metrics.GpuPowerW, "GPU 功耗错误");
    Near(60, metrics.GpuMemoryUsagePercent, "显存占用率错误");
    Near(7372.8, metrics.GpuMemoryUsedMb, "已用显存错误");
    Near(12288, metrics.GpuMemoryTotalMb, "总显存错误");
    Near(78, metrics.GpuMemoryTemperatureC, "应优先使用显存结温");
    Equal("NVIDIA RTX", metrics.GpuName, "GPU 名称错误");
}

static void PreferredGpuOverridesDefault()
{
    HardwareSnapshot snapshot = Snapshot(
        Sensor(SolisHardwareKind.GpuNvidia, "/gpu-nvidia/0", "NVIDIA RTX", SolisSensorKind.Load, "GPU Core", 70),
        Sensor(SolisHardwareKind.GpuAmd, "/gpu-amd/0", "AMD Radeon", SolisSensorKind.Load, "GPU Core", 35));

    MappedHardwareMetrics metrics = HardwareMetricMapper.Map(snapshot, "/gpu-amd/0");
    Equal("/gpu-amd/0", metrics.SelectedGpuId, "手动 GPU 选择未生效");
    Near(35, metrics.GpuUsagePercent, "读取了错误 GPU 的使用率");
}

static void VramUsageCanBeDerived()
{
    HardwareSnapshot snapshot = Snapshot(
        Sensor(SolisHardwareKind.GpuNvidia, "/gpu-nvidia/0", "NVIDIA RTX", SolisSensorKind.SmallData, "GPU Memory Used", 4096),
        Sensor(SolisHardwareKind.GpuNvidia, "/gpu-nvidia/0", "NVIDIA RTX", SolisSensorKind.SmallData, "GPU Memory Total", 16384));

    MappedHardwareMetrics metrics = HardwareMetricMapper.Map(snapshot);
    Near(25, metrics.GpuMemoryUsagePercent, "未由已用/总显存推导占用率");
}

static void MemoryTemperatureUsesHighestDimm()
{
    HardwareSnapshot snapshot = Snapshot(
        Sensor(SolisHardwareKind.Memory, "/ram", "Total Memory", SolisSensorKind.Load, "Memory", 42),
        Sensor(SolisHardwareKind.Memory, "/memory/dimm/0", "DIMM 0", SolisSensorKind.Temperature, "DIMM #0", 48),
        Sensor(SolisHardwareKind.Memory, "/memory/dimm/0", "DIMM 0", SolisSensorKind.Temperature, "Thermal Sensor Critical Limit", 95),
        Sensor(SolisHardwareKind.Memory, "/memory/dimm/1", "DIMM 1", SolisSensorKind.Temperature, "DIMM #1", 52));

    MappedHardwareMetrics metrics = HardwareMetricMapper.Map(snapshot);
    Near(42, metrics.MemoryUsagePercent, "内存占用率错误");
    Near(52, metrics.MemoryTemperatureC, "没有取内存条实时温度最高值");
}

static void MemoryAndPhysicalStorageDetailsAreMapped()
{
    HardwareSnapshot snapshot = Snapshot(
        Sensor(SolisHardwareKind.Memory, "/ram", "Total Memory", SolisSensorKind.Load, "Memory", 37.5),
        Sensor(SolisHardwareKind.Memory, "/ram", "Total Memory", SolisSensorKind.Data, "Memory Used", 12),
        Sensor(SolisHardwareKind.Memory, "/ram", "Total Memory", SolisSensorKind.Data, "Memory Available", 20),
        Sensor(SolisHardwareKind.Memory, "/memory/dimm/0", "DIMM 0", SolisSensorKind.Temperature, "DIMM #0", 48),
        Sensor(SolisHardwareKind.Storage, "/nvme/0", "Samsung 990 PRO", SolisSensorKind.Load, "Used Space", 65),
        Sensor(SolisHardwareKind.Storage, "/nvme/0", "Samsung 990 PRO", SolisSensorKind.Temperature, "Composite Temperature", 43),
        Sensor(SolisHardwareKind.Storage, "/hdd/1", "WDC 4TB", SolisSensorKind.Load, "Used Space", 72),
        Sensor(SolisHardwareKind.Storage, "/hdd/1", "WDC 4TB", SolisSensorKind.Temperature, "Temperature", 36),
        Sensor(SolisHardwareKind.Storage, "/nvme/2", "No Temperature", SolisSensorKind.Load, "Used Space", 10),
        Sensor(SolisHardwareKind.Storage, "/nvme/2", "No Temperature", SolisSensorKind.Temperature, "Composite Temperature", 0));

    MappedHardwareMetrics metrics = HardwareMetricMapper.Map(snapshot);
    Near(12, metrics.MemoryUsedGb, "内存已用容量错误");
    Near(32, metrics.MemoryTotalGb, "内存总容量错误");
    MappedStorageDevice[] devices = metrics.StorageDevices?.ToArray() ?? Array.Empty<MappedStorageDevice>();
    Equal(3, devices.Length, "未保留全部物理硬盘");
    MappedStorageDevice nvme = devices.Single(device => device.Id == "/nvme/0");
    MappedStorageDevice hdd = devices.Single(device => device.Id == "/hdd/1");
    Equal("Samsung 990 PRO", nvme.Name, "NVMe 名称错误");
    Near(65, nvme.UsagePercent, "NVMe 占用错误");
    Near(43, nvme.TemperatureC, "NVMe 温度错误");
    Equal("WDC 4TB", hdd.Name, "HDD 名称错误");
    Equal(null, devices.Single(device => device.Id == "/nvme/2").TemperatureC,
        "缺失的硬盘温度不应显示为 0°C");
}

static void NvmeSelectionWorks()
{
    HardwareSnapshot snapshot = Snapshot(
        Sensor(SolisHardwareKind.Storage, "/nvme/0", "NVMe A", SolisSensorKind.Temperature, "Composite Temperature", 40),
        Sensor(SolisHardwareKind.Storage, "/nvme/0", "NVMe A", SolisSensorKind.Temperature, "Warning Temperature", 85),
        Sensor(SolisHardwareKind.Storage, "/nvme/1", "NVMe B", SolisSensorKind.Temperature, "Composite Temperature", 52),
        Sensor(SolisHardwareKind.Storage, "/ssd/2", "SATA SSD", SolisSensorKind.Temperature, "Temperature", 70));

    MappedHardwareMetrics automatic = HardwareMetricMapper.Map(snapshot);
    Equal("/nvme/1", automatic.SelectedNvmeId, "默认没有选择当前温度最高的 NVMe");
    Near(52, automatic.NvmeTemperatureC, "NVMe 默认最高温错误");

    MappedHardwareMetrics manual = HardwareMetricMapper.Map(snapshot, preferredNvmeId: "/nvme/0");
    Equal("/nvme/0", manual.SelectedNvmeId, "手动 NVMe 选择未生效");
    Near(40, manual.NvmeTemperatureC, "手动 NVMe 温度错误");
}

static void SnapshotStorePublishesHardware()
{
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
        new NetworkThroughputReading(false, null, null, null, null, "NotSampled"),
        CodexMetricsReading.Empty("NotSampled"),
        DateTimeOffset.FromUnixTimeSeconds(100));

    SolisMetricsSnapshot snapshot = store.Current;
    Near(4.8, snapshot.Cpu.ClockGhz.Value, "CPU 主频未写入统一快照");
    Near(60, snapshot.Gpu.MemoryUsagePercent.Value, "显存占用未写入统一快照");
    Near(78, snapshot.Gpu.MemoryTemperatureC.Value, "显存温度未写入统一快照");
    Near(52, snapshot.Memory.TemperatureC.Value, "内存温度未写入统一快照");
    Near(40, snapshot.Storage.NvmeTemperatureC.Value, "NVMe 温度未写入统一快照");
    Near(132, snapshot.Fps.Value, "FPS 未写入统一快照");
}

static void WindowsSourceReadsCurrentInterface()
{
    var source = new WindowsNetworkCounterSource();
    NetworkCounterReadResult result = source.ReadSelected();
    True(result.Snapshot is not null,
        $"未找到当前出口网卡：{result.ErrorCategory ?? "Unknown"}");
    True(!string.IsNullOrWhiteSpace(result.Snapshot!.InterfaceName), "出口网卡名称为空");
}
}
