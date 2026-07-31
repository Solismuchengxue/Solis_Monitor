#nullable enable

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Solis.Security;

namespace LibreHardwareMonitor.Solis.Firmware;

public sealed record FirmwareUpdateProgress(
    int Percent,
    string Stage,
    string Detail);

public sealed record FirmwareUpdateResult(
    bool Success,
    string Message,
    FirmwareImageInfo? Image = null);

public sealed class DeviceFirmwareUpdater : IDisposable
{
    private readonly DeviceTokenStore _tokenStore;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public DeviceFirmwareUpdater(
        DeviceTokenStore tokenStore,
        HttpClient? httpClient = null)
    {
        _tokenStore = tokenStore ??
                      throw new ArgumentNullException(nameof(tokenStore));
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient(
            new HttpClientHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
    }

    public async Task<FirmwareUpdateResult> UpdateAsync(
        string path,
        IProgress<FirmwareUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!_tokenStore.TryGetPairedDevice(out _, out string? ipAddress) ||
            string.IsNullOrWhiteSpace(ipAddress)) {
            return new FirmwareUpdateResult(
                false,
                "尚未配对副屏，请先通过设备向导完成配对。");
        }
        string deviceIpAddress = ipAddress!;

        progress?.Report(new FirmwareUpdateProgress(
            0, "正在校验", "读取设备 OTA 能力"));
        FirmwareDeviceStatus? deviceStatus;
        try
        {
            deviceStatus = await GetStatusAsync(
                deviceIpAddress,
                cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            return new FirmwareUpdateResult(false, exception.Message);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new FirmwareUpdateResult(false, "连接副屏超时。");
        }

        if (!deviceStatus.RollbackEnabled)
            return new FirmwareUpdateResult(false, "副屏尚未启用 OTA 失败回滚。");

        FirmwareImageValidationResult validation =
            FirmwareImageValidator.ValidateFile(path, deviceStatus.MaxImageSize);
        if (!validation.Success)
            return new FirmwareUpdateResult(false, validation.ErrorMessage!);

        FirmwareImageInfo image = validation.Image!;
        progress?.Report(new FirmwareUpdateProgress(
            2,
            "校验完成",
            $"{image.Version} · SHA-256 {image.Sha256.Substring(0, 12)}…"));

        try
        {
            using var request = CreateRequest(
                HttpMethod.Post,
                deviceIpAddress,
                "/api/ota");
            request.Content = new ProgressFileContent(
                path,
                image.Size,
                percent => progress?.Report(new FirmwareUpdateProgress(
                    Math.Max(2, Math.Min(92, 2 + percent * 90 / 100)),
                    "正在上传",
                    $"{percent}% · {image.Version}")));
            using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            string responseBody = await response.Content.ReadAsStringAsync()
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                return new FirmwareUpdateResult(
                    false,
                    response.StatusCode == HttpStatusCode.Unauthorized
                        ? "设备令牌不匹配，请重新配对。"
                        : $"副屏拒绝更新：{responseBody}");
            }

            progress?.Report(new FirmwareUpdateProgress(
                94, "正在重启", "固件已写入，等待副屏重新上线"));
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken)
                .ConfigureAwait(false);

            for (int attempt = 0; attempt < 30; attempt++)
            {
                try
                {
                    FirmwareDeviceStatus status = await GetStatusAsync(
                        deviceIpAddress,
                        cancellationToken).ConfigureAwait(false);
                    if (string.Equals(
                            status.Version,
                            image.Version,
                            StringComparison.Ordinal)) {
                        progress?.Report(new FirmwareUpdateProgress(
                            100, "更新完成", $"副屏正在运行 {status.Version}"));
                        return new FirmwareUpdateResult(
                            true,
                            $"固件 {status.Version} 更新成功。",
                            image);
                    }
                }
                catch (HttpRequestException)
                {
                }
                catch (TaskCanceledException)
                    when (!cancellationToken.IsCancellationRequested)
                {
                }

                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken)
                    .ConfigureAwait(false);
            }
            return new FirmwareUpdateResult(
                false,
                "副屏没有在规定时间内以新固件重新上线，可能已回滚。");
        }
        catch (IOException)
        {
            return new FirmwareUpdateResult(false, "上传期间无法继续读取固件文件。");
        }
        catch (HttpRequestException)
        {
            return new FirmwareUpdateResult(
                false,
                "固件传输中断；副屏会保留原有可启动固件。");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new FirmwareUpdateResult(
                false,
                "固件传输超时；副屏会保留原有可启动固件。");
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    private async Task<FirmwareDeviceStatus> GetStatusAsync(
        string ipAddress,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            ipAddress,
            "/api/ota/status");
        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new HttpRequestException("设备令牌不匹配，请重新配对。");
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"副屏 OTA 服务不可用（{(int)response.StatusCode}）。");
        string json = await response.Content.ReadAsStringAsync()
            .ConfigureAwait(false);
        if (!FirmwareUpdateProtocol.TryParseStatus(
                json,
                out FirmwareDeviceStatus? status)) {
            throw new HttpRequestException("副屏返回了无法识别的 OTA 状态。");
        }
        return status!;
    }

    private HttpRequestMessage CreateRequest(
        HttpMethod method,
        string ipAddress,
        string path)
    {
        var request = new HttpRequestMessage(
            method,
            $"http://{ipAddress}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _tokenStore.DeviceToken);
        return request;
    }

    private sealed class ProgressFileContent : HttpContent
    {
        private readonly string _path;
        private readonly long _length;
        private readonly Action<int> _progress;

        public ProgressFileContent(
            string path,
            long length,
            Action<int> progress)
        {
            _path = path;
            _length = length;
            _progress = progress;
            Headers.ContentType = new MediaTypeHeaderValue(
                "application/octet-stream");
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _length;
            return true;
        }

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context)
        {
            await SerializeFileAsync(stream, CancellationToken.None)
                .ConfigureAwait(false);
        }

        #if !NETFRAMEWORK
        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken)
        {
            await SerializeFileAsync(stream, cancellationToken)
                .ConfigureAwait(false);
        }
        #endif

        private async Task SerializeFileAsync(
            Stream stream,
            CancellationToken cancellationToken)
        {
            using FileStream file = new(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                65536,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] buffer = new byte[65536];
            long sent = 0;
            while (true)
            {
                int read = await file.ReadAsync(
                    buffer,
                    0,
                    buffer.Length,
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;
                await stream.WriteAsync(
                    buffer,
                    0,
                    read,
                    cancellationToken).ConfigureAwait(false);
                sent += read;
                _progress((int)Math.Min(100, sent * 100 / _length));
            }
        }
    }
}
