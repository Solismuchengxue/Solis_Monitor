# 天气数据

Solis Monitor 使用和风天气 API，在桌面端获取当天的天气描述、最低温和最高温，再通过现有设备 API 发送给小屏。API Key 只保存在当前 Windows 用户的本地配置中，不进入固件、源码、日志或 Git。

## 配置

在仓库根目录打开 PowerShell 7，执行：

```powershell
pwsh -File .\tools\Set-QWeatherConfig.ps1
```

脚本默认使用：

- API Host：`md3h2ew6qe.re.qweatherapi.com`
- 经度：`121.504751`
- 纬度：`38.837286`
- 配置文件：`%LocalAppData%\SolisMonitor\weather.json`

脚本会以不回显方式读取 API Key。配置完成后重启 Solis Monitor。若需要更换坐标或 Host，可使用参数：

```powershell
pwsh -File .\tools\Set-QWeatherConfig.ps1 -ApiHost '你的API Host' -Longitude 121.51 -Latitude 38.84
```

不要把 `weather.json` 复制到仓库，也不要在命令行参数中直接传递 API Key。

## 数据链路

1. 使用配置中的经纬度调用 GeoAPI 城市搜索；中国大陆使用 GCJ-02，其他地区使用 WGS84。GeoAPI 根据坐标自动返回地区名称和 Location ID，用户不再手工填写城市或地区。
2. 使用自动解析的 Location ID 调用天气实况接口读取当前 `text`、`windDir`、`windScale` 和 `icon`，调用三日天气预报接口读取当天的 `tempMin` 和 `tempMax`。
3. 写入统一指标快照中的位置、天气、风向、风力等级、图标索引和室外温度范围。
4. 设备 API 通过 schema 1 的向后兼容可选字段发送风向、风力等级和内部图标索引；小屏显示地点、天气、温度范围、动态图标及风向风力。

认证使用官方支持的 `X-QW-Api-Key` 请求头，API Key 不放在 URL 中。接口字段参见[和风天气身份认证](https://dev.qweather.com/docs/configuration/authentication/)、[GeoAPI 城市搜索](https://dev.qweather.com/docs/api/geoapi/city-lookup/)和[每日天气预报](https://dev.qweather.com/docs/api/weather/weather-daily-forecast/)。

## 刷新与失败行为

- 成功后每 1 小时刷新一次。
- 失败后依次等待 5、15、30 分钟重试，后续继续使用 30 分钟间隔。
- 最近一次成功结果最多保留 3 小时；超过 3 小时仍无法更新时，天气字段变为不可用并在小屏显示 `--`。
- 天气失败不会影响硬件、网络、Codex 或 DHT11 指标。
- 修改本地配置后需要重启 Solis Monitor。
- 和风天气响应可能带有 `Content-Encoding: gzip`；桌面端会先按响应头解压，再解析 JSON。

和风天气建议根据数据更新频率合理缓存；本项目的小时级刷新不会按 1 Hz 快照频率重复请求天气服务。

## 天气图标

小屏复用 `reference/assets/m00.png`～`m26.png`。桌面端把和风天气
`now.icon` 映射为 0～26 的内部索引，固件不直接依赖外部服务编码：

- `m00`：晴；`m01`：晴间多云；`m02`：阴或多云。
- `m03`～`m05`：阵雨、雷阵雨和强雷雨。
- `m06`：冻雨、雨夹雪或雨雪天气。
- `m07`～`m12`：小雨到特大暴雨。
- `m13`～`m17`：阵雪、小雪、中雪、大雪和暴雪。
- `m18`～`m20`：夜间晴、夜间多云和夜间阵雨。
- `m21`～`m23`：雾、霾和沙尘。
- `m24`～`m26`：热、冷和未知天气。

27 张图在构建时转换为 48×48 的 RGB565 + Alpha 资源。图标透明区域按
Alpha 混合，不会显示黑色方块。和风天气未知代码映射为 `m26`；字段不可用时
才不绘制图标。

完整的 62 个官方天气代码、内部索引和实际素材见
[WEATHER_ICONS.md](WEATHER_ICONS.md)。

## 开发与排查入口

- [和风天气开发控制台](https://console.qweather.com/project?lang=zh)：管理项目、API Host 和认证凭据。
- [和风天气开发者工具台](https://dev.qweather.com/api-explore/#/Weather/getWeatherNow)：在线调试天气实况等 API，核对请求参数、响应正文和响应头。
- [GeoAPI 城市搜索工具](https://dev.qweather.com/api-explore/#/Geo/getGeoCitylookup)：用经纬度反查地区名称和 Location ID。
- [高德坐标拾取器](https://lbs.amap.com/tools/picker)：查询中国大陆地区使用的 GCJ-02 经纬度。

和风天气的坐标参数按“经度,纬度”传递；GeoAPI 查询按官方要求格式化为最多两位小数。本项目当前使用 `121.504751,38.837286`，反查时发送 `121.5,38.84`。截图、日志和文档中必须遮盖 `X-QW-Api-Key`。
