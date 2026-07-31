# AP Web 配网

## 触发方式

- 没有有效网络配置：启动后自动开放 `Solis-Monitor-xxxx` 热点。
- 已有配置：长按 GPIO21 约 5 秒进入配网；短按仍用于切换页面。
- 普通 Wi‑Fi 短暂掉线：只执行退避重连，不自动开放热点。
- 串口 `setup`、`show`、`reconnect`、`clear` 始终保留为救援入口。

## 使用步骤

1. 小屏显示热点名、`192.168.0.1` 和剩余时间后，用手机连接该热点。
2. 浏览器访问 `http://192.168.0.1`。通配 DNS 和 404 重定向也会将常见联网检测请求引导到门户。
3. 扫描并选择 Wi‑Fi，填写 Wi‑Fi 密码。
4. 保存成功后设备关闭 AP，切回 STA，并从双槽 NVS 重新加载配置后连接 Wi‑Fi。

已有配置时将密码留空会保留当前密码。Windows IPv4、固定 Device API 端口 `18472` 和设备令牌不在配网页面出现，而是在副屏开启发现后通过桌面端 6 位码配对自动写入。

手机可能将该无互联网 AP 标记为“需登录/认证”，此时系统认证弹窗会绑定到配网 Wi-Fi 并直接打开门户。如果普通浏览器在移动数据开启时无法访问 `192.168.0.1`，可使用认证弹窗，或临时关闭移动数据后再访问；这是手机对未认证网络的路由选择。

## 生命周期与故障边界

- 配网使用 ESP-IDF 原生 Wi‑Fi、HTTP Server、NVS 和轻量通配 DNS，不增加第三方运行时依赖。
- AP 地址固定为 `192.168.0.1/24`。由于用户当前局域网也使用 `192.168.0.0/24`，配网期间会主动断开 STA 并暂停 PC 指标请求；仍保留 AP+STA 无线模式用于 Wi‑Fi 扫描。
- 任意门户 HTTP 请求都会刷新活动时间；连续 10 分钟无请求后关闭 AP。
- 已有配置进入配网但未保存：超时后恢复原 STA 配置。
- 全新设备超时：关闭 AP 并保持未配置状态，重新上电可再次自动进入配网。
- 字段校验复用 `network_config_validate`；保存复用 `config_store_save` 的双槽写入流程。
- 保存失败时门户保持开启并返回错误；连接失败时按正常 STA 退避策略重试，不重新暴露 AP。
- “恢复默认设置”经页面二次确认后清除全部 Wi-Fi、PC 地址和设备令牌，保留设备名称、MAC 与固件；页面返回实际 AP 名后设备延迟重启并自动进入 AP。

## 已联网后的局域网管理

- STA 获得局域网地址后，同一套管理页面会在设备 IPv4 的 HTTP 80 端口启动；无需再次进入 AP 模式即可查看配置和扫描 Wi-Fi。
- 局域网页面不会回传 Wi-Fi 密码、PC 地址或设备令牌。可信家庭局域网内可直接修改 Wi-Fi 或执行页面内二次确认的恢复默认设置，不增加额外鉴权。
- 保存成功后继续复用双槽 NVS 和运行时重连流程，不维护第二套配置逻辑。
- 设备向 DHCP 申报主机名 `Solis_Monitor_XXXX`，`XXXX` 为 STA MAC 地址后四位十六进制。路由器可能在旧租约续期后才更新显示名。
- 局域网管理入口仅适用于可信局域网，当前为 HTTP、没有 TLS；不应暴露到公网或访客网络。

## 实现入口

- 门户与 Wi‑Fi 生命周期：`firmware/components/network_client/network_client.c`
- HTTP 生命周期、页面、Wi‑Fi 配置和恢复默认设置：
  `firmware/components/network_client/provisioning_portal.c`
- 6 位码和配对接口：
  `firmware/components/network_client/provisioning_portal_pairing.c`
- 设备状态、显示控制、远程重启和 OTA：
  `firmware/components/network_client/provisioning_portal_device.c`
- 门户私有状态与内部处理器声明：
  `firmware/components/network_client/private_include/provisioning_portal_internal.h`
- 通配 DNS：`firmware/components/network_client/captive_dns.c`，基于 ESP-IDF 官方 captive portal 示例的 CC0/Unlicense 实现。
- gzip 页面源文件：`firmware/components/network_client/portal.html`
- 确定性压缩脚本：`tools/generate_portal.py`
- 小屏配网状态：`firmware/components/metrics/dashboard_store.c` 与 `firmware/components/ui/ui.c`
