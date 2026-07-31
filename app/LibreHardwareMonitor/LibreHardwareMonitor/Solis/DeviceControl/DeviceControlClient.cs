#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Solis.Security;

namespace LibreHardwareMonitor.Solis.DeviceControl;

public sealed record DeviceControlResult(
    bool Success,
    string Message,
    DeviceDisplaySettings? Settings = null);

public sealed class DeviceControlClient : IDisposable
{
    private readonly DeviceTokenStore _tokenStore;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public DeviceControlClient(
        DeviceTokenStore tokenStore,
        HttpClient? httpClient = null)
    {
        _tokenStore = tokenStore ??
                      throw new ArgumentNullException(nameof(tokenStore));
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient(
            new HttpClientHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    public Task<DeviceControlResult> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        return SendAsync(
            HttpMethod.Get,
            "/api/control",
            null,
            cancellationToken);
    }

    public Task<DeviceControlResult> SaveAsync(
        DeviceDisplaySettings settings,
        CancellationToken cancellationToken = default)
    {
        if (settings is null || !settings.IsValid)
            return Task.FromResult(new DeviceControlResult(
                false,
                "亮度、夜间时间或时区设置无效。"));
        var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>(
                "brightness",
                settings.BrightnessPercent.ToString(CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>(
                "night_enabled",
                settings.NightEnabled ? "1" : "0"),
            new KeyValuePair<string, string>(
                "night_start",
                settings.NightStartMinute.ToString(CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>(
                "night_end",
                settings.NightEndMinute.ToString(CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>(
                "utc_offset",
                settings.UtcOffsetMinutes.ToString(CultureInfo.InvariantCulture))
        ]);
        return SendAsync(
            HttpMethod.Post,
            "/api/control",
            content,
            cancellationToken,
            settings);
    }

    public Task<DeviceControlResult> RestartAsync(
        CancellationToken cancellationToken = default)
    {
        return SendAsync(
            HttpMethod.Post,
            "/api/restart",
            new ByteArrayContent([]),
            cancellationToken);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    private async Task<DeviceControlResult> SendAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken,
        DeviceDisplaySettings? savedSettings = null)
    {
        if (!_tokenStore.TryGetPairedDevice(out _, out string? ipAddress) ||
            string.IsNullOrWhiteSpace(ipAddress)) {
            content?.Dispose();
            return new DeviceControlResult(
                false,
                "尚未配对副屏，请先通过设备向导完成配对。");
        }

        try
        {
            using var request = new HttpRequestMessage(
                method,
                $"http://{ipAddress}{path}");
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                _tokenStore.DeviceToken);
            request.Headers.ConnectionClose = true;
            request.Content = content;
            using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                cancellationToken).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync()
                .ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized) {
                return new DeviceControlResult(
                    false,
                    "设备令牌不匹配，请重新配对。");
            }
            if (!response.IsSuccessStatusCode) {
                return new DeviceControlResult(
                    false,
                    $"副屏控制失败（{(int)response.StatusCode}）。");
            }
            if (method == HttpMethod.Get) {
                if (!DeviceControlProtocol.TryParseSettings(
                        body,
                        out DeviceDisplaySettings? settings)) {
                    return new DeviceControlResult(
                        false,
                        "副屏返回了无法识别的显示设置。");
                }
                return new DeviceControlResult(true, "读取成功。", settings);
            }
            return new DeviceControlResult(
                true,
                path == "/api/restart"
                    ? "副屏正在重新启动。"
                    : "显示设置已保存。",
                savedSettings);
        }
        catch (HttpRequestException)
        {
            return new DeviceControlResult(false, "无法连接副屏。");
        }
        catch (TaskCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return new DeviceControlResult(false, "连接副屏超时。");
        }
        finally
        {
            content?.Dispose();
        }
    }
}
