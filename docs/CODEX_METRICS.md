# Codex 指标采集设计

本文说明 Solis Monitor 如何取得 Codex 的项目、模型、上下文、周额度和账户累计 Token。实现入口为：

```text
app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Codex/CodexMetricsCollector.cs
app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Codex/CodexAccountUsageReader.cs
```

## 数据来源与边界

采集器只读访问 `%CODEX_HOME%`；未设置该环境变量时回退到 `%USERPROFILE%/.codex`：

- `session_index.jsonl`：会话 ID 与任务标题；
- `sessions/**/*.jsonl`：会话元数据、回合上下文、Token 计数和额度快照。

这些 JSONL 是 Codex 桌面版的内部本地格式，不是公开稳定 API。采集器不会读取认证文件，不会把对话正文、提示词或 Codex Token 写入 Solis 快照或发送给 ESP32。由于目标事件与正文共存在同一 JSONL 文件中，实现仍需逐行读取文件并按事件类型筛选，不能描述为“完全不接触正文文件”。

## 最后活动任务

1. 读取每个会话文件首行的 `session_meta`，取得 ID、`cwd` 和 `source`。
2. 排除 `source.subagent` 会话。
3. 按文件最后写入时间选择最新主会话。
4. 用 ID 在 `session_index.jsonl` 查找 `thread_name`，作为任务名；缺失时回退到项目名。
5. 项目名取 `cwd` 最后一级目录。
6. 从最后活动会话的最新 `turn_context` 读取模型与推理强度。

采集器最多每 5 秒刷新一次。最后一条有效 Token 计数超过 10 分钟时，Codex 状态变为“不活跃”，但保留最后一次有效数值。该状态表示任务指标的新鲜度，不表示 Codex 进程是否运行。

## 上下文

只解析 `payload.type == "token_count"` 的必要字段：

```text
上下文已用 K = last_token_usage.input_tokens / 1000
上下文总计 K = model_context_window / 1000
上下文占用率 = input_tokens / model_context_window × 100
```

这里显示的是 Codex 事件报告的数字，不是 Solis Monitor 对对话文本重新分词估算的结果。

## 账户累计 Token

小屏“账户累计 TOKEN”显示账户生命周期累计值，不显示当前任务 `token_count` 中的 `total_token_usage.total_tokens`，也不把本地所有任务文件自行相加。

读取流程：

1. 定位 Codex 桌面版随附的 `%LOCALAPPDATA%/OpenAI/Codex/bin/<版本目录>/codex.exe`；
2. 启动 `codex.exe app-server --stdio`；
3. 发送 `initialize`；
4. 调用第一方方法 `account/usage/read`；
5. 读取响应中的 `lifetimeTokens`，写入设备快照。

该流程复用 Codex 桌面版已有登录状态，不读取 `auth.json` 或其他认证文件。首次启动立即后台读取；成功后 6 小时刷新，失败后 5 分钟重试，不阻塞桌面端 1 Hz 快照发布。2026-07-23 实测返回 `2,958,484,287`，小屏显示为 `2.96B`，与 Codex 桌面版个人资料中的 29.6 亿一致。

## 周额度识别

### 七天窗口

- 读取 `rate_limits.primary` 和 `rate_limits.secondary`；
- `window_minutes` 必须是有限数字；
- `10079` 到 `10081` 分钟（即 `10080±1`）视为七天窗口，以兼容轻微的序列化或边界差异；
- 5 小时窗口 `300` 等其他窗口不参与周额度显示；
- 剩余百分比为 `clamp(100 - used_percent, 0, 100)`。

### 显式映射与降级规则

当前本机真实样本确认了两种额度标识：

| `limit_id` | 类别 | 固定显示名 |
|---|---|---|
| `codex` | 主额度 | `主周额度` |
| `codex_bengalfox` | Spark 额度 | `GPT-5.3-Codex-Spark` |

映射使用大小写不敏感的完整匹配，不再用 `gpt`、`5.3`、`spark` 或 `codex` 子串猜测。也可用完整、稳定显示名进行等价匹配。

遇到未知未来 ID 时不丢弃数据，也不要求名称包含 `codex`：`primary` 降级为主额度，`secondary` 降级为 Spark 额度。已知 ID 优先于槽位，例如 `limit_id=codex` 的七天数据即使出现在 `secondary`，仍属于主额度。

### 重置时间

`resets_at` 支持：

- Unix 秒数字；
- Unix 毫秒数字；
- 上述两者的数字字符串；
- 可由 `DateTimeOffset` 解析的 ISO 时间字符串。

数值绝对值达到 `100000000000` 时按毫秒处理，否则按秒处理。有效值统一转换为本机时间并格式化为 `MM-dd HH:mm`；非法或越界值显示为空，不影响同一快照的其他指标。

## 跨项目防回退

上下文属于最后活动任务，周额度则是账户级数据。采集器因此从所有主会话中分别保留主额度和 Spark 额度时间最新的事件，切换到带有旧额度快照的新项目时不会把 0% 错误恢复成历史 2%。后台真正产生更新事件后，额度可以正常变化，包括额度重置后从 0% 恢复到 100%。

全局扫描只有在两类额度都已找到后，才允许依据两类事件中较早的时间停止读取更旧会话；缺少任一类别时继续扫描，避免先发现 Spark 后漏掉主额度。

## 兼容性验证

桌面端冒烟测试覆盖：

- 最后活动主任务、项目、模型和推理强度；
- 子代理排除；
- 上下文计算；
- `account/usage/read` 的账户生命周期累计 Token 解析与空值处理；
- 两个已知 `limit_id` 的显式分类与固定名称；
- 未知且不含 `codex` 的未来 ID；
- `10079` 分钟兼容窗口与 `300` 分钟非周窗口；
- Unix 秒、整数毫秒和数字字符串毫秒重置时间；
- 项目切换时额度不回退，以及真实新事件允许额度恢复。

脱敏样例位于：

```text
app/tests/SolisMonitor.Metrics.SmokeTests/Fixtures/codex/session_complete.jsonl
```

样例使用虚构 ID、项目名、模型和数字，仅保留解析所需的内部结构，不包含真实任务正文、提示词、凭据或 Token。详细执行命令见 [TESTING.md](TESTING.md)。

## 解析诊断

内部格式损坏或变化时，采集器通过 `ErrorCategory` 给出不含原文的稳定类别：

| 类别 | 含义 |
|---|---|
| `SessionsNotFound` | `sessions` 目录不存在 |
| `SessionNotFound` | 没有可用主会话，例如只有子代理会话 |
| `SessionMetadataInvalid` | 存在会话文件，但首行 `session_meta` 缺失、损坏或没有 ID |
| `TokenCountNotFound` | 主会话中没有候选 `token_count` 事件 |
| `TokenCountInvalidJson` | 候选事件不是完整 JSON |
| `TokenCountEnvelopeInvalid` | `payload.type` 等事件外层结构不符合预期 |
| `TokenCountTimestampInvalid` | 时间戳缺失或不可解析 |
| `TokenCountFieldsMissing` | 事件存在，但没有任何可用上下文、累计 Token 或周额度字段 |
| `RateLimitsInvalid` | `rate_limits` 或周额度槽位类型不符合预期 |

额度结构损坏但上下文字段仍有效时，采集器继续发布上下文和 Online 状态，同时保留 `RateLimitsInvalid` 诊断；不会因为一个可选分组异常而清空全部 Codex 指标。底层 I/O 或权限异常继续使用异常类型名诊断。

## 首次扫描与增量策略

采集器不建立额外数据库，采用两个进程内索引：

- `_sessionCache` 按文件路径缓存首行解析出的会话 ID、项目目录与是否为主会话；
- `_weeklyReadLengths` 和当前会话 `_readLength` 保存已读字节偏移，后续刷新只读追加内容；
- 文件长度变小视为截断或重建，从文件开头重新读取；
- 每 5 秒最多刷新一次，避免桌面端 1 Hz 发布循环重复扫描；
- 周额度只有在主额度和 Spark 都已找到后，才按二者中较早的事件时间停止向旧会话扫描。

2026-07-22 的本机脱敏统计为 442 个会话文件、总计约 1551.72 MiB。仅读取全部首行元数据约 266 ms；按当前策略从最新文件找到两类额度读取 1 个文件、约 112.56 MiB，PowerShell 审计脚本耗时约 3.13 秒。该数字不是 C# 性能基准，但说明首轮成本主要来自最后活动的大型 JSONL，而不是目录枚举。

当前保留简单的内存索引：首轮数秒只发生在进程启动或文件截断后，日常刷新由字节偏移限制为新增数据；无需引入持久化数据库和新的隐私面。自动测试验证首次读取、追加事件和文件截断后恢复。若未来真实启动延迟持续超过可接受范围，再单独评估尾部索引或持久化索引。
