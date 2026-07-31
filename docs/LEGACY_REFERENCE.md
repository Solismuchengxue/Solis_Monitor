# 旧固件提炼参考

本文档保存 `old_code` 删除前提炼出的、后续重写固件仍可能需要的已验证参数。它是迁移参考，不代表新固件必须继续采用 Arduino、Arduino_GFX 或 LVGL。

## 1. 已验证的显示基线

| 项目 | 旧固件配置 |
| --- | --- |
| 屏幕控制器 | NT35510 |
| 面板物理分辨率 | 480 × 800 |
| 总线 | ESP32-S3 LCD_CAM / I80，16 位并行 |
| Arduino_GFX | 1.3.4 |
| LVGL | 8.2.0 |
| 旋转参数 | `3`，逻辑分辨率 800 × 480 |
| 像素格式 | RGB565，16 bit |
| LVGL 字节交换 | `LV_COLOR_16_SWAP 1` |
| 旧驱动默认总线频率 | 8 MHz（构造时未显式指定） |
| 单次最大传输 | 2046 像素，即 4092 字节 |
| LVGL 刷新缓冲 | `screenWidth * 32` 像素，内部 RAM |
| 触摸 | 未配置 |

旧固件中已验证的显示构造关系：

```cpp
Arduino_DataBus *bus = new Arduino_ESP32LCD16(
    16, 17, 18, 19,
    0, 1, 2, 3, 4, 5, 6, 7,
    8, 9, 10, 11, 12, 13, 14, 15);

Arduino_GFX *gfx = new Arduino_NT35510(bus, 20, 3);
```

完整引脚表见 [`HARDWARE.md`](./HARDWARE.md)。与显示初始化直接相关的控制脚为：

- `DC/RS`：GPIO16
- `CS`：GPIO17
- `WR`：GPIO18
- `RD`：GPIO19
- `RST`：GPIO20
- 背光：GPIO38；旧固件在初始化前拉低，屏幕初始化成功后拉高

NT35510 的旧驱动来源注释指向 `hi631/LCD_NT35510-MRB3971`。旋转参数 `3` 对应 MADCTL 的 `MY | MV`。后续如更换驱动，先以纯色、色条和方向测试确认 RGB/BGR、字节序和坐标方向，再迁移界面。

## 2. 旧电脑监控串口协议

旧固件通过 UART0、115200 bit/s 接收 JSON；字段值均按字符串解析。旧实现没有可靠的帧分隔协议，只是累计当前串口字节并尝试反序列化，重写时不应原样沿用。

| JSON 字段 | 含义 | 旧界面单位/处理 |
| --- | --- | --- |
| `SCPUCLK` | CPU 频率 | 示例 `4100`；旧界面误标为 Hz，实际应按数据源确认，通常为 MHz |
| `SCPUUTI` | CPU 使用率 | `%` |
| `SMEMUTI` | 内存使用率 | `%` |
| `SGPU1CLK` | GPU 频率 | 按数据源确认单位 |
| `SGPU1UTI` | GPU 使用率 | `%` |
| `SNIC4DLRATE` | 下载速率 | `MB/s` |
| `SNIC4ULRATE` | 上传速率 | `MB/s` |
| `TCPU` | CPU 温度 | `°C` |
| `TGPU1DIO` | GPU 温度 | `°C` |
| `PCPUPKG` | CPU 封装功耗 | `W` |
| `PGPU1TDPP` | GPU TDP 百分比 | `%` |
| `FCPU` | CPU 风扇转速 | `RPM` |
| `FGPU1` | GPU 风扇转速 | `RPM` |

GPU 功耗曾按 `(PGPU1TDPP × GPUTDP) / 100` 估算，其中 `GPUTDP` 被硬编码为 215 W。这不是通用规则，新实现应直接接收实际 GPU 功耗或把额定 TDP 作为电脑端配置。

## 3. 旧 Wi-Fi、天气与本地传感器行为

- Wi-Fi 配置存放在 NVS 命名空间 `wifi`，键名为 `ssid`、`password`、`citycode`。
- 天气曾使用 TianAPI，并通过 weather.com.cn 接口定位城市；旧 API Key 和外部接口不应直接复制到新工程。
- 普通天气刷新间隔 `updateTimes = 60` 分钟。
- 其他天气/黄历刷新值 `uptateOthers = 3600` 分钟，但旧注释写“6 小时”，数值与注释不一致。
- DHT11 数据脚为 GPIO47；后续应限制采样频率并处理读取失败。
- 按键为 GPIO21，低电平有效，旧工程使用 OneButton。

## 4. 旧构建与分区配置

| 项目 | 值 |
| --- | --- |
| 构建系统 | PlatformIO |
| PlatformIO 环境 | `esp32-s3` |
| 平台 | `espressif32`，未锁定版本 |
| 开发板 | `esp32-s3-devkitc-1` |
| 框架 | Arduino |
| 串口监视器 | 115200 bit/s |
| 上传波特率 | 921600 bit/s |
| 编译标志 | `BOARD_HAS_PSRAM`、`-mfix-esp32-psram-cache-issue`、`CORE_DEBUG_LEVEL=3` |
| 分区表 | `user_huge_app.csv` |

旧分区表：

| 分区 | 类型 | 偏移 | 大小 |
| --- | --- | --- | --- |
| `nvs` | data/nvs | `0x9000` | `0x5000` |
| `otadata` | data/ota | `0xe000` | `0x2000` |
| `app0` | app/ota_0 | `0x10000` | `0x7F0000` |

## 5. 迁移时不要继承的问题

- LVGL 颜色交换、Arduino_GFX 私有初始化和旧 UI 代码彼此耦合，换驱动后必须重新验证颜色与方向。
- 串口 JSON 没有长度头或换行帧边界，解析失败时旧缓冲还会继续累积。
- CPU 频率单位标签有误，GPU 频率单位也未被协议明确规定。
- GPU 额定功耗硬编码为 215 W。
- PlatformIO 的 `espressif32` 平台版本未锁定，旧环境无法仅凭配置完全复现。
- 天气接口依赖第三方服务和密钥，不适合作为新架构的固定依赖。

## 6. 删除前文件指纹

以下 SHA-256 可用于与用户已有备份核对；路径是删除前的历史路径。

| 历史路径 | SHA-256 |
| --- | --- |
| `old_code/src/main.cpp` | `81FEF15213EB4BD5E64EB0964E2993AD954EF10AFDEF08D52B4C3C44D25AB864` |
| `old_code/platformio.ini` | `0F41323FF59BABFFE27A393458D5448C1472198F124B2842AA37564588062D2A` |
| `old_code/lib/Arduino_GFX/src/display/Arduino_NT35510.cpp` | `D1B421F8147D2CD58F4FF004CFC20FAE7BA0162A5C7767250AED84887968136E` |
| `old_code/lib/Arduino_GFX/src/databus/Arduino_ESP32LCD16.cpp` | `A7629443C9DB837C2E59B3C8E2777056776EC378628B5DE7FDAC451369C7A848` |
| `old_code/lib/lv_conf.h` | `AB5A096F80887D9F0498DB60B0AEB3F10D7B7049734FA4E71086EC6FA78A4C9F` |
| `old_code/user_huge_app.csv` | `F735DDE6CE2B2FF6E0A0E89529D540CA9AC06A88A3FB63BF4595E3FBDC823AEE` |

## 7. 清理记录

- 清理日期：2026-07-16
- 删除对象：工作区中的整个 `old_code` 目录
- 删除前规模：1617 个文件，约 173.26 MiB
- 备份状态：用户确认已有备份；备份位置和内容未在本工作区内验证
- 保留范围：硬件与引脚、显示驱动基线、串口字段、Wi-Fi/天气行为、构建配置、分区表和关键文件哈希
