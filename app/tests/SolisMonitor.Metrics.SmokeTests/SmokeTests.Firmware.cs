internal static partial class SmokeTests
{
static void FirmwareImageHeaderIsValidated()
{
    byte[] image = CreateFirmwareImage("1.2.3");

    FirmwareImageValidationResult valid =
        FirmwareImageValidator.Validate(image, 4096);
    True(valid.Success, $"合法固件应通过校验：{valid.ErrorMessage}");
    Equal("solis_monitor", valid.Image?.ProjectName, "固件项目名解析错误");
    Equal("1.2.3", valid.Image?.Version, "固件版本解析错误");
    Equal((ushort)0x0009, valid.Image?.ChipId ?? 0, "固件芯片 ID 解析错误");
    Equal(64, valid.Image?.Sha256.Length ?? 0, "固件 SHA-256 长度错误");

    byte[] wrongProject = (byte[])image.Clone();
    Array.Clear(wrongProject, 80, 32);
    Encoding.ASCII.GetBytes("other_project").CopyTo(wrongProject, 80);
    True(!FirmwareImageValidator.Validate(wrongProject, 4096).Success,
        "其他项目固件不应通过校验");

    byte[] wrongChip = (byte[])image.Clone();
    BinaryPrimitives.WriteUInt16LittleEndian(wrongChip.AsSpan(12, 2), 0);
    True(!FirmwareImageValidator.Validate(wrongChip, 4096).Success,
        "其他芯片固件不应通过校验");

    byte[] corrupted = (byte[])image.Clone();
    corrupted[200] ^= 0x5A;
    True(!FirmwareImageValidator.Validate(corrupted, 4096).Success,
        "SHA-256 不匹配的损坏固件不应通过校验");

    True(!FirmwareImageValidator.Validate(image, image.Length - 1).Success,
        "超过设备 OTA 槽容量的固件不应通过校验");
}

static void FirmwareUpdateUsesPairedToken()
{
    string directory = Path.Combine(
        Path.GetTempPath(),
        $"SolisMonitor.FirmwareUpdate-{Guid.NewGuid():N}");
    string firmwarePath = Path.Combine(directory, "solis_monitor.bin");
    try
    {
        Directory.CreateDirectory(directory);
        byte[] image = CreateFirmwareImage("1.2.3");
        File.WriteAllBytes(firmwarePath, image);
        var store = new DeviceTokenStore(directory);
        store.MarkPaired("Solis_Monitor_A1B2", "192.168.0.42");
        var handler = new FirmwareHttpMessageHandler(
            FirmwareStatusJson("1.2.2", 4096),
            FirmwareStatusJson("1.2.3", 4096),
            false);
        using var client = new HttpClient(handler);
        using var updater = new DeviceFirmwareUpdater(store, client);

        FirmwareUpdateResult result = updater.UpdateAsync(firmwarePath)
            .GetAwaiter().GetResult();

        True(result.Success, result.Message);
        Equal(3, handler.RequestCount, "OTA 应执行能力检查、上传和重启确认");
        Equal(store.DeviceToken, handler.AuthorizationToken,
            "OTA 请求未使用当前配对令牌");
        Equal(image.Length, handler.UploadedLength,
            "OTA 上传内容长度与固件不一致");
    }
    finally
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }
}

static void FirmwareUpdateInterruptionIsSafe()
{
    string directory = Path.Combine(
        Path.GetTempPath(),
        $"SolisMonitor.FirmwareInterruption-{Guid.NewGuid():N}");
    string firmwarePath = Path.Combine(directory, "solis_monitor.bin");
    try
    {
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(firmwarePath, CreateFirmwareImage("1.2.3"));
        var store = new DeviceTokenStore(directory);
        store.MarkPaired("Solis_Monitor_A1B2", "192.168.0.42");
        var handler = new FirmwareHttpMessageHandler(
            FirmwareStatusJson("1.2.2", 4096),
            null,
            true);
        using var client = new HttpClient(handler);
        using var updater = new DeviceFirmwareUpdater(store, client);

        FirmwareUpdateResult result = updater.UpdateAsync(firmwarePath)
            .GetAwaiter().GetResult();

        True(!result.Success, "传输中断不应报告成功");
        True(result.Message.Contains("保留原有可启动固件", StringComparison.Ordinal),
            "中断提示必须明确旧固件仍可启动");
        Equal(2, handler.RequestCount, "传输中断后不应继续轮询新版本");
    }
    finally
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }
}

static void FirmwareDeviceStatusIsParsed()
{
    const string json =
        """
        {"product":"Solis Monitor","chip":"esp32s3","project":"solis_monitor",
         "version":"1.2.2","max_image_size":4063232,"rollback":true}
        """;

    True(FirmwareUpdateProtocol.TryParseStatus(json, out FirmwareDeviceStatus? status),
        "合法 OTA 状态响应应能解析");
    Equal("1.2.2", status?.Version, "运行版本解析错误");
    Equal(4063232L, status?.MaxImageSize ?? 0, "OTA 槽容量解析错误");
    True(status?.RollbackEnabled == true, "回滚能力解析错误");

    True(!FirmwareUpdateProtocol.TryParseStatus(
            json.Replace("\"esp32s3\"", "\"esp32\"", StringComparison.Ordinal),
            out _),
        "其他芯片的 OTA 状态不应被接受");
}

static byte[] CreateFirmwareImage(string version)
{
    byte[] image = new byte[1024];
    image[0] = 0xE9;
    image[23] = 1;
    BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(12, 2), 0x0009);
    BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(32, 4), 0xABCD5432);
    Encoding.ASCII.GetBytes(version).CopyTo(image, 48);
    Encoding.ASCII.GetBytes("solis_monitor").CopyTo(image, 80);
    SHA256.HashData(image.AsSpan(0, image.Length - 32))
        .CopyTo(image, image.Length - 32);
    return image;
}

static string FirmwareStatusJson(string version, long maxImageSize) =>
    $$"""
      {"product":"Solis Monitor","chip":"esp32s3","project":"solis_monitor",
       "version":"{{version}}","max_image_size":{{maxImageSize}},"rollback":true}
      """;
}
