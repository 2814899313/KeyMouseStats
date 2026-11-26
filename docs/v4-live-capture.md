# v4 实时采集接线（方案 B）

日期：2026-11-23

## 为什么需要专用钩子线程

低级钩子的回调由系统在**安装钩子的线程取消息时**派发。Avalonia 的消息泵会按窗口/类型过滤，
实测把钩子装在 UI 线程时，连续注入 7 个键只能收到 4 个。

现在钩子装在 `HookThreadHost` 的专用线程：

```text
Kms.HookThread (GetMessage 未过滤泵)
  └─ LowLevelHooks → QueuedInputSink（只入队）
UI 线程
  └─ 30ms DispatcherTimer → CountingInputSink（改 StatsData）
       └─ EventRing → DbWriter 线程（写 SQLite）
```

这样可以保证：

- 钩子回调不碰数据模型，不会和 UI 枚举 `History` 撞车；
- UI 卡顿不会阻塞系统输入链；队列满时丢弃并计数；
- 看门狗的 `CheckHealth`/重装也在钩子线程执行。

## 统计口径

键盘事件在 `CountingInputSink.KeyEx` 中处理，与 v3 对齐：

1. `ShortcutTracker.Normalize`：左/右 Ctrl、Alt、Shift → `0xA0..0xA5`；
2. 物理键位区分自动重复与丢失抬起（1200 ms 窗口）；
3. 只有首次按下才计击键；
4. 首次按下同时写入：
   - `DayRecord.KeyCounts` / `HourKeys` / `Totals.Keys`
   - `PhysicalKeys` / `PhysicalVks`（32px 扫描码键位）
   - `HoldTracker`（按住时长、8 档直方图、按住活跃段）
   - `ShortcutStats`（`ctrl_a` 等组合键）
   - `RateTracker`（APM、5 分钟输入量、日峰值/全时峰值）
   - `AppUsage` 按键组（当前前台窗口快照）

鼠标：

- 点击/滚轮/移动计数与原 v3 一致；
- `ClickHeat` 记录 32px 屏幕网格与三通道（左/右/中/侧）；
- `PeripheralMouseWear` 通过 Raw Input 归到具体鼠标档案；
- 移动事件按 10 ms 合并后入环，文本路程仍逐事件精确累加。

## 存档

| 文件 | 作用 |
|---|---|
| `%LOCALAPPDATA%\KeyMouseStats4\stats.txt` | **主存档**，v13 完整保真；5 分钟 + 退出保存，原子替换 |
| `%LOCALAPPDATA%\KeyMouseStats4\kmstats.db` | 事件级镜像（13 表）；退出时把 active/hold/peak 等派生列同步进去 |

首启把 v3 `stats.txt`（当前月完整明细）复制为 v4 主存档，再由 `V2Migrator`
把全部月档导入 SQLite；两者合并后写回 v4 文本，确保 49 天历史与当前月逐键明细都不丢。
v3 文件不改动。

读取顺序：v4 文本 → v4 库 → v3 文本 → v3 库 → 开发固件。
有 `[day=` 但解析不出任何日记录时视为损坏，继续回退，不覆盖历史。

隔离测试可设置 `KMS4_DATA_DIR` 指向临时目录，不会碰真实数据。

## 已做的真实冒烟

- 注入 3 轮 A–G（42 个键事件）→ 钩子收到 43（含预热 F1），统计 21 次击键，0 丢弃；
- 按住时长 21 次全部结算，`hold_v1` 与 `kk` 同步落盘；
- `Ctrl+A` 产生 `combo=ctrl_a:1`，`physical_keys` 同时记录 Ctrl 与 A；
- 退出后 SQLite 的 `keys/clicks` 与文本一致；派生列由 `DbAggregateSync` 补齐；
- 首启隔离目录导入真实 v3 数据：49 天、327,863 击键 / 177,930 点击 / 424,892 滚轮 / 270,331 次按住；
  合并后的 v4 文本再用 `Kms.Oracle text` 解析，总量逐项一致；
- **同机 v3/v4 短时对拍**：v3 3.0.0 与 v4 同时运行，注入 30 次 Shift 点按，
  v3 当日击键 +30、v4 +30，clicks/wheel 双方均 +0；
- 全量测试 896 项通过（Release）。

## 24 小时并行对拍（待执行）

v3 没有 headless 模式，需要保持 v3 托盘程序与 v4 同时运行：

1. 记录起点：分别查询两库当日行
   ```sql
   SELECT keys, clicks, wheel, move_px
   FROM daily_summary WHERE day='YYYY-MM-DD';
   ```
   v3 库：`%LOCALAPPDATA%\KeyMouseStats\kmstats.db`
   v4 库：`%LOCALAPPDATA%\KeyMouseStats4\kmstats.db`
2. 正常使用 24 小时，中途不要退出任何一方。
3. 记录终点，比较 **当日增量**，而不是绝对值（v4 可能启动更晚）。
4. 验收：`keys/clicks/wheel` 误差 < 0.1%，队列 `DroppedEvents=0`，无崩溃。
5. v3 的 SQLite 不写 hold；按住时长只能比 v4 文本与 v3 `stats.txt` 的 `hold_v1` 总量。

## 仍未完成

- v3 与 v4 同机并行 24 小时计数对拍；
- 锁屏/休眠消息（`WM_WTSSESSION_CHANGE`）尚未接入 `ActivityMonitor.Message`；
- SQLite 镜像的 `window_daily_agg.key_g*` 与分类规则尚未实时写入（文本主存档完整）；
- v4 文本尚未做多份备份轮转（当前只有原子替换，没有 `.bak` 历史）。
