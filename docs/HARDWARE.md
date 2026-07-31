# Solis Monitor 硬件配置与引脚表

本文档记录当前已经完成并验证的 Solis Monitor 硬件基线，供后续固件开发、移植和故障排查使用。

## 1. 硬件配置清单

| 模块 | 当前配置 | 说明 |
| --- | --- | --- |
| 主控模组 | ESP32-S3-WROOM-1-N8R2 | 用户提供的标识为 `D2N8R2`；其中 `N8R2` 对应 8 MB Flash 和 2 MB PSRAM |
| Flash | 8 MB Quad SPI Flash | `N8` |
| PSRAM | 2 MB Quad SPI PSRAM | `R2`；固件应选择 QSPI/Quad PSRAM，不是 OPI/Octal PSRAM |
| 显示屏 | 3.97 英寸 IPS，480 × 800 | 用户提供亮度为 450 cd/m²；旧固件使用旋转方向 `3`，逻辑画布为横屏 800 × 480 |
| 显示驱动 IC | NT35510 | 16 位 Intel 8080（I80）并行接口，RGB565 像素格式 |
| 屏幕连接器 | 51 Pin FPC，0.3 mm 间距，下接 | 原理图标注为 4 英寸 480 × 800 屏幕接口 |
| 温湿度传感器 | DHT11 标准模块/器件 | 数据脚带 10 kΩ 上拉，工作电压 3.3 V |
| USB 转串口 | CH340C | 用于固件下载和 UART0 日志；不是 ESP32-S3 原生 USB |
| 自动下载电路 | 2N7002DW | CH340C 的 DTR/RTS 控制 `GPIO0` 和 `EN` |
| 用户按键 | 1 个轻触按键 | 低电平有效，带 10 kΩ 上拉和 0.1 µF 电容 |
| 背光电路 | PT4110E89E 升压驱动 | `LCD_PWM` 控制背光；旧固件当前仅作高低电平开关使用 |
| 主电源 | USB VBUS 5 V 输入，板载 3.3 V 电源 | 为 ESP32-S3、传感器和逻辑部分供电 |
| USB Type-C | 原理图提供卧式/立式接口选项 | 两种封装可按装配需要二选一或同时保留；USB 数据线连接 CH340C |
| 触摸功能 | 未配置 | 当前原理图和旧代码中没有触摸控制器或触摸引脚 |

> 说明：当前原理图写作 `ESP32-S3-WROOM-1 N8R2`。`D2` 未出现在原理图的正式订货型号中，因此本文不对其含义作额外推断；内存规格按官方 `N8R2` 定义记录。

## 2. ESP32-S3 GPIO 引脚表

| GPIO | 网络/功能 | 方向 | 电气或启动说明 |
| ---: | --- | --- | --- |
| 0 | LCD `DB0` | 输出 | 同时是下载启动配置脚；上电/复位时拉低会进入下载模式 |
| 1 | LCD `DB1` | 输出 | 16 位 I80 数据总线 |
| 2 | LCD `DB2` | 输出 | 16 位 I80 数据总线 |
| 3 | LCD `DB3` | 输出 | 16 位 I80 数据总线 |
| 4 | LCD `DB4` | 输出 | 16 位 I80 数据总线 |
| 5 | LCD `DB5` | 输出 | 16 位 I80 数据总线 |
| 6 | LCD `DB6` | 输出 | 16 位 I80 数据总线 |
| 7 | LCD `DB7` | 输出 | 16 位 I80 数据总线 |
| 8 | LCD `DB8` | 输出 | 16 位 I80 数据总线 |
| 9 | LCD `DB9` | 输出 | 16 位 I80 数据总线 |
| 10 | LCD `DB10` | 输出 | 16 位 I80 数据总线 |
| 11 | LCD `DB11` | 输出 | 16 位 I80 数据总线 |
| 12 | LCD `DB12` | 输出 | 16 位 I80 数据总线 |
| 13 | LCD `DB13` | 输出 | 16 位 I80 数据总线 |
| 14 | LCD `DB14` | 输出 | 16 位 I80 数据总线 |
| 15 | LCD `DB15` | 输出 | 16 位 I80 数据总线 |
| 16 | LCD `DCX` / Data-Command | 输出 | 旧代码中为 `DC` |
| 17 | LCD `CSX` / Chip Select | 输出 | 低电平选中屏幕 |
| 18 | LCD `WRX` / Write Strobe | 输出 | I80 写时钟 |
| 19 | LCD `RDX` / Read Strobe | 输出 | 已占用 ESP32-S3 原生 USB D- 默认引脚 |
| 20 | LCD `RESX` / Reset | 输出 | 已占用 ESP32-S3 原生 USB D+ 默认引脚 |
| 21 | `BUTTON_PIN` | 输入 | 低电平有效；10 kΩ 上拉，0.1 µF 对地 |
| 38 | `LCD_PWM` | 输出 | 背光控制，可用于开关或 PWM 调光 |
| 43 | UART0 TXD | 输出 | 连接 CH340C RXD，用于日志和下载通信 |
| 44 | UART0 RXD | 输入 | 连接 CH340C TXD，用于下载通信 |
| 47 | `DHT11_PIN` | 输入/单总线 | DHT11 数据，10 kΩ 上拉至 3.3 V |
| EN | 芯片使能/复位 | 输入 | 由上拉、RC 复位和 CH340C 自动下载电路共同控制 |

未列出的 GPIO 当前没有在原理图和旧固件中确认用途，后续使用前应重新核对模组限制及 PCB 连接。

## 3. LCD 接口汇总

| LCD 信号 | ESP32-S3 | LCD 信号 | ESP32-S3 |
| --- | ---: | --- | ---: |
| DB0 | GPIO0 | DB8 | GPIO8 |
| DB1 | GPIO1 | DB9 | GPIO9 |
| DB2 | GPIO2 | DB10 | GPIO10 |
| DB3 | GPIO3 | DB11 | GPIO11 |
| DB4 | GPIO4 | DB12 | GPIO12 |
| DB5 | GPIO5 | DB13 | GPIO13 |
| DB6 | GPIO6 | DB14 | GPIO14 |
| DB7 | GPIO7 | DB15 | GPIO15 |
| DCX | GPIO16 | CSX | GPIO17 |
| WRX | GPIO18 | RDX | GPIO19 |
| RESX | GPIO20 | LCD_PWM | GPIO38 |

屏幕初始化基线：

```cpp
Arduino_DataBus *bus = new Arduino_ESP32LCD16(
    16, 17, 18, 19,
    0, 1, 2, 3, 4, 5, 6, 7,
    8, 9, 10, 11, 12, 13, 14, 15);

Arduino_GFX *gfx = new Arduino_NT35510(bus, 20, 3);
```

## 4. 关键硬件约束

1. **GPIO0 与 LCD DB0 共用。** LCD 或外部电路在复位期间不得把 GPIO0 持续拉低，否则 ESP32-S3 会进入下载模式。原理图也给出了烧录停留在 `Connecting...` 时手动拉低 GPIO0 的提示。
2. **当前 Type-C 不是原生 USB。** GPIO19 和 GPIO20 已分别用于 LCD `RDX`、`RESX`，Type-C 的 USB D+/D- 连接到 CH340C。
3. **PSRAM 必须配置为 QSPI。** `N8R2` 的 2 MB PSRAM 是 Quad SPI；选择 OPI PSRAM 会导致初始化失败。
4. **完整 RGB565 帧占用约 750 KiB。** 480 × 800 × 2 = 768,000 字节；2 MB PSRAM 可以容纳一至两份像素数据，但还需为网络、字体、图片和运行时保留空间，推荐使用局部缓冲和按区域重绘。
5. **背光使用 20 kHz PWM。** GPIO38 通过 LEDC 驱动 PT4110 EN；设备设置允许 10%–100% 亮度，熄屏状态使用 0%。默认亮度为 100%，夜间计划默认关闭；夜间计划或 PC 长时间失联触发熄屏时仍保留用户设置的亮度，重新唤醒后恢复。
6. **DHT11 不适合高频采样。** 当前固件通过 GPIO47 每 2 秒读取一次；校验、超时或非法读数会立即标记为不可用，下一次成功读取自动恢复。当前近似校准以 `29°C / 61%RH → 26.7°C / 79%RH` 为单点基准：温度补偿板内升温，湿度通过 Magnus 饱和水汽压关系换算并限制到 0–100%RH。它只能改善当前工作区间，后续仍应使用多点配对数据替换。

## 5. 历史旧固件基线（Arduino / PlatformIO）

| 项目 | 旧工程配置 |
| --- | --- |
| 构建系统 | PlatformIO |
| 开发框架 | Arduino |
| PlatformIO 开发板 | `esp32-s3-devkitc-1` |
| 图形驱动 | Arduino_GFX 1.3.4 |
| 旧 UI 框架 | LVGL 8.2.0 |
| 屏幕旋转 | `3`，横屏 |
| 串口波特率 | 115200 bit/s |
| 上传波特率 | 921600 bit/s |
| PSRAM 编译标志 | `BOARD_HAS_PSRAM` |
| 分区表 | `user_huge_app.csv` |

`BOARD_HAS_PSRAM` 只表示代码需要 PSRAM；实际构建环境仍必须把内存类型配置为 QSPI PSRAM。

## 6. 信息来源

- 原理图：[`SCH_ESPMonitor_Plus.pdf`](../reference/SCH_ESPMonitor_Plus.pdf)
- 界面参考：[`template.jpg`](../reference/template.jpg)
- 旧固件提炼参考：[`LEGACY_REFERENCE.md`](./LEGACY_REFERENCE.md)
- 原始 `old_code` 已在用户确认外部备份后删除；关键文件哈希和历史配置见提炼参考
- Espressif 官方 N8R2 规格：[ESP32-S3-DevKitC-1 ordering information](https://docs.espressif.com/projects/esp-idf/en/stable/esp32s3/hw-reference/esp32s3/user-guide-devkitc-1.html)
