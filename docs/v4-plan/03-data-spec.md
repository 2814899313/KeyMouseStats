# 03 · 数据与统计口径规格

> 用途：v4 的 `Kms.Core` / `Kms.Storage` 施工依据，以及差分对拍的检查清单。
> **本文列出的 24 条口径，任何一条搬错都会让 v4 的数字与 v3 不一致。**

---

## 1. 领域模型

### 1.1 `DayRecord`（v3 的位置：`KeyMouseWidget.cs:149-183`）

v3 把数据模型和 UI 放在同一个文件里。v4 搬到 `Kms.Core/Model/DayRecord.cs`，字段不增不减：

| 字段 | 类型 | 单位/说明 |
|---|---|---|
| `Date` | `DateTime` | 本地日（`yyyy-MM-dd` 语义） |
| `Keys` `Clicks` | `long` | 日总数，**唯一真源**（派生桶要与它对齐） |
| `Left` `Right` `Middle` `XButtons` | `long` | 分键位点击 |
| `Wheel` `HWheel` | `long` | 滚轮 = **事件次数**，不是格子数；`HWheel` 是横向子集 |
| `MovePx` `MoveMeters` | `double` | 像素 / 米（`counts / dpi * 0.0254`） |
| `HourKeys[24]` `HourClicks[24]` | `long[]` | 小时桶 |
| `KeyCounts` | `Dictionary<int,long>` | vk → 次数 |
| `PhysicalKeys` `PhysicalVks` | `Dictionary<int,long>` | 物理键位 |
| `ComboCounts` | `Dictionary<string,long>` | `"ctrl_c"` 风格 |
| `Apps` | `Dictionary<string,AppUsage>` | 键 = `(进程路径, 标题)` |
| `ActiveSeconds` `IdleSeconds` | `double` | 秒 |
| `ActiveHours[24]` | `double[]` | 秒 |
| `Sessions` `IdlePeriods` | `List<ActiveSession>` | — |
| `AppObservedSeconds` | `double` | **无条件累加**，与 `Apps` 之和不相等 |
| `AppSwitches` `PeakApm` | `long` | — |
| `Cross` | `CrossDay` | 交叉遥测（含独立的 `Clicks`！） |
| `Holds` | `Dictionary<int,KeyHold>` | 逐键按住统计 |
| `ClickCells` | `Dictionary<int,long>` | 全通道汇总，**与旧版逐格一致** |
| `ClickCellsV1` | `Dictionary<long,long>` | 三通道，key = `(channel<<32) \| cellKey` |

⚠️ v4 改造：`Dictionary` 全部换成 `IReadOnlyDictionary` 暴露，内部可换 `Dictionary` 或更省内存的结构；
但**枚举顺序**要注意——v3 依赖 `Dictionary` 的插入序做排行（`KeyRanking` 取 `[0]`），
换成 `SortedDictionary` 或并发容器会改变排序结果。

### 1.2 `AppUsage` / `ActiveSession` / `KeyHold` / `CrossDay`

| 类型 | 关键字段（需在 M1 核对源码确认全部字段） |
|---|---|
| `AppUsage` | `ActiveSeconds`、`Keys`、`Clicks`、分类 id/source、题名 |
| `ActiveSession` | `StartMs`、`EndMs`、`Seconds` |
| `KeyHold` | `HoldCount`、`HoldTotalMs`、`HoldMaxMs`、`HoldBuckets`、`Holds`、墙钟占用 |
| `CrossDay` | 小时桶、会话应用、**独立 `Clicks`**（与 `DayRecord.Clicks` 不同源） |

---

## 2. 存储：13 张表（`KmsSchema.cs:37-52`，`schema_rev = 1`）

| 表 | 职责 | 关键列 / 坑 |
|---|---|---|
| `meta` | 基线键（`migrated_from_format`、`last_export_ms`、`epoch_fix_v1`）+ `PRAGMA user_version` | 迁移闸门全靠它 |
| `events_window_session` | 前台窗口会话事实 | `start_ts_ms`/`end_ts_ms`/`active_ms`、`window_title`（**隐私**）、`process_path` |
| `events_input_burst` | 输入突发段 | `key_count = Count+1`、`variance = M2/Count`（**总体方差**）、`backspace_rate = backspaces/(Count+1)`、`iki_*_ms` |
| `daily_click_grid` | 点击热区格子 | `(channel, cell_x, cell_y, screen_id)` 唯一 |
| `daily_summary` | 日汇总 | ⚠️ **写线程只更新计数列，`active_seconds`/`sessions`/`peak_apm`/`hold_*` 从不更新**（`DbWriter.cs:742-765`） |
| `daily_hour_agg` | 小时桶 | — |
| `daily_key_agg` | 按键桶 | — |
| `window_daily_agg` | 窗口维度聚合 | ⚠️ `move_px` 恒 0（写入口 6 个参数不含 move，`DbWriter.cs:541-554`），**不可与 `daily_summary.move_px` 对比** |
| `month_archive` / `month_archive_app` | 月归档 | — |
| `app_category_rule` | 用户分类规则 | `category_source` 是 CHECK 四值 |
| `migration_log` | 迁移幂等账本 | 主键去重 |
| `kms_import_log` | `.kms` 包导入账本 | `(package_id, record_key)` 唯一仲裁 |

**`daily_summary.origin` 有四种取值**：`shadow`（`DbWriter.cs:758`）、`migrated`（`V2Migrator.cs:506`）、
`snapshot-bridge`（`V2Migrator.cs:780`）、默认 `native`（`KmsDb.cs:424-426`）。
⚠️ 守恒断言**只认** `origin='migrated'`（`V2Migrator.cs:699`）。

> ⚠️ **参考实现的口径**：对拍用的 v3 参考值必须由 **`test.bat` 编译当前源码**得到
> （它产出的 `kms_test_core.dll`），**绝不能直接用仓库里的 `KeyMouseStats.exe`**。
> 理由：本仓库就真实发生过 exe 落后于源码的情况（exe 3.2.2 / 源码 3.4.1，差两个小版本）。
> 用旧 exe 当"标准答案"，会把 v3 的 bug 和差异一起固化进 v4 的验收。

**PRAGMA**（`KmsDb.cs:503-510`）：`journal_mode=WAL`（仅写连接）、`synchronous=FULL`、
`foreign_keys=ON`、`busy_timeout=5000`、`temp_store=MEMORY`。
v4 换驱动后**必须逐项复验**，尤其 `synchronous=FULL` —— 它是数据安全底线。

---

## 3. 时间单位约定（v4 不许改）

| 场景 | 单位 | 位置 |
|---|---|---|
| 事件时间戳 | **Unix epoch 毫秒** | `EventClock` 以 `UnixTime.NowMs()` 为锚（`DbWriter.cs:39-51`） |
| 按住时长 / 组合键 | **Stopwatch tick**（非毫秒） | `PressDuration.cs:121/154`、`ShortcutStats.cs:43/72` |
| 时长累加 | `double` 秒 | `ActiveSeconds` 等 |
| 日期键 | 本地时区 `yyyy-MM-dd` 字符串 | `DbWriter.cs:881-889` |
| 文本后端 | 日记录用 `yyyy-MM-dd`（`date=2026-11-22`）；设置里的时间戳用 `"O"` 往返格式（如 `wellbeing_v1` 末位 `2026-11-22T15:42:01.8602032+08:00`） | 实物核对自 `v13-text-current.txt` |
| 毫秒↔tick | **四舍五入**，禁止截断 | `DbWriter.cs:48-51` 显式避开 ±1ms IKI 量化误差 |

⚠️ epoch 修复用 SQLite 的 `strftime/unixepoch/'localtime'`（`KmsSchema.cs:421-424`），
与 C# 侧的本地时区换算**是两套逻辑**，跨时区/夏令时会日界错位。

---

## 4. 24 条统计口径陷阱（差分对拍逐条验）

### 4.1 累加与分母

1. **`active_ms` 只收 ≤15 s 的间隔**（`ActiveGapMs=15000`，`DbWriter.cs:173`）：>15 s 挂机不累加，但 `end_ts_ms` 照推（`DbWriter.cs:440-441`）。
2. **突发段三个分母不同**：`key_count = Count+1`、`variance = M2/Count`、`backspace_rate = backspaces/(Count+1)`（`DbWriter.cs:727-735`），而 Welford 的 n 用**已自增**的 `_burst.Count`（`:491-492`）。
3. **滚轮量纲**：`WheelAccumulator` 按 120 结算并跨包留余数（`MouseDistance.cs:14-32`），但 `DbWriter.CountWheel` 只 `+1` —— 库存的是**事件次数**。
4. **区间积分只算「保证活跃」的前缀**（`ActivityTracker.Sample`，`ActivityTime.cs:77-92`），且采样间隔 >5 s 直接 `Break(ObservationGap)` 不补计。
5. **`daily_summary` 缺活跃时长**（见 §2 表内注）——直接读库算日均活跃得 0。

### 4.2 两套口径并存（不要合并）

6. **两套 APM**：`AppFlow.Item.Apm = (Keys+Clicks)*60/Seconds`（`AppFlow.cs:24/83/183`）；`PersonalStats` 的「每秒活跃击键」= `Keys/ActiveSeconds`、点击是 `Clicks*60/ActiveSeconds`（`StatisticsReport.cs:31-35`）。
7. **两套点击计数**：`day.Cross.Clicks` 由 Raw Input 独立累加（`ReleaseAnalytics.cs:116`），与 `day.Clicks`（钩子路径）**不同源**；`SampleData` 里专门用 `day.Cross.Clicks = day.Clicks` 对齐（`SampleData.cs:237`）。
8. **应用归因门槛**：`AppObservedSeconds` 无条件累加（`AppTelemetry.cs:15`），但 `AppUsage.ActiveSeconds` 只在「前后同进程路径 + 间隔 ≤5 s」累加（`:17-23`）。**Σ应用时长 ≠ 观测时长**，「未归因」必须单列（`AppActivity.cs:646-654`，并计入 `categoryKeys[4]`）。

### 4.3 周期与边界

9. **「今天算不算」**：`RangeRules.CompleteEnd` 把今天排除（`RangeAnalysis.cs:25-28`）、`TryNormalize(includeToday)`（`:43-50`）；但 `RangeStats.Of` 的 `Days` 是自然日（`:317`）、日均分母却是 `Observed`（`:286-288`）；`HoldTrendReport` 再用 `IncludesRunningToday`（`:297-301`、`HoldTrendReport.cs:47-51`）手工接上今天。
10. **同比窗口**：`TrendCutoff.Compare` 的 `Hours = now.Hour`（`TrendCutoff.cs:40`），窗口是 `[00:00, 当前整点)`，当前不完整小时不计；只支持 metric 0/1（`:43-47`）；负零另做处理（`:89-91`）。
11. **本地时区切日/切小时**（`DbWriter.cs:876-889`）vs epoch 修复用 SQLite `'localtime'`（`KmsSchema.cs:421-424`）。

### 4.4 统计量定义

12. **样本标准差**除 `n-1`（`RangeAnalysis.cs:91-97`）。
13. **分位数线性插值**（`RangeAnalysis.cs:80-88`）。
14. **分箱把最大值塞进最后一箱**（`RangeAnalysis.cs:180-183`）。
15. **趋势方程少于 3 点不可用**（`RangeAnalysis.cs:211/221`）。
16. **作息矩阵量纲混用**：metric 2 = `ActiveHours[h]/60.0`（**分钟**），与 metric 0/1 的「次数」共用 `Average[7,24]` 与 `Max`（`HourProfile.cs:47-48`）。
17. **心流阈值自相矛盾**：`FlowConfig.DeepMinMinutes = 10`（`FlowAnalyzer.cs:94`），但文件头（12 行）与 `:222` 注释都写 15 分钟。**已定取 10**（以代码行为为准），注释同步修正，见主方案 §13 D9。
18. **滑窗去重**：`BuildMinuteSegments` 每 60 s 桶只归属一次（`FlowAnalyzer.cs:516-552`）；`Normalize` 把同应用缺口 ≤20 s 的会话合并（`MicroCheckoutSeconds`，`:256-277`）。两者都会改变总时长与粘性分母。
19. **按住时长分档**：`Edges={50,100,200,500,1000,2000,5000}` 严格 `<`，第 0 档 `<50ms`、末档 `≥5s`（`PressDuration.cs:186-191`）；`MaxHoldMs=60000` 之外与 `<=0` 一律 `Discard`（`:122-126`）。

### 4.5 减法与容错

20. **「未归位」是减法算出来的**：`counts[5] = max(0, day.Keys - ΣKeyCounts)`（`ReleaseAnalytics.cs:163-168`）。
21. **自动重复 vs 丢失抬起**：1200 ms 内同物理键重复按下判为自动重复、不产生新击键（`ShortcutStats.cs:22/68-83`）；`HoldTracker.Press` 对同一 vk 的第二次按下把旧样本按「丢失抬起」**丢弃而非结算**（`PressDuration.cs:88-104`）。
22. **恢复时刻两种取法**：`ContextCost.FindRecovery` 要么 `burst.StartMs`（击键数达标），要么 `row.StartMs + minActiveSeconds*1000`（时长达标）（`:130-143`）；滞后 = `recovery - distractor.StartMs`（`:79-89`）。
23. **异常信号门槛**：`DailySignal` 要求基线 ≥14 天且 `Z ≥ 2`，且只对「今天以前」成立（`ReleaseAnalytics.cs:184-190`）；缓存 60 s、上限 400 条（`:173-180`）。
24. **包记录键不含结束时间**：`KmsSessionRecord.RecordKey = "S|day|start|指纹(path)"`（`KmsPackage.cs:46-49`）—— 同起点不同时长会被账本判为同一条而跳过。

---

## 5. 迁移不变量（M1 必须做成自动化测试）

v3 已有的 5 条守恒断言（`V2Migrator.cs:683-725`），v4 全部继承：

| # | 断言 | 说明 |
|---|---|---|
| ① | 日击键总和一致 | 文本档 Σkeys == 库 `daily_summary` Σkeys |
| ①b | 日点击总和一致 | 同上 |
| ② | 网格总和一致 | `daily_click_grid` Σcount == 日点击 |
| ③ | 小时桶总和一致 | `daily_hour_agg` Σ == 日总数 |
| ④ | 月档总和一致 | `month_archive` Σ == 日总数 |

附加要求：

- 迁移**单事务**（`V2Migrator.cs:198-216`），失败全回滚。
- 迁移前**只读解析**，绝不调有损的 `Store.ExportPayload`（`:146-154`）。
- 备份含 `manifest.txt`（sha256 / 相对路径 / 字节数）并**逐字节复核**（`:293-349`）。
- 幂等靠 `migration_log` 主键 + 每日 `INSERT OR IGNORE`（`:516/623-635`）。

---

## 6. v4 的数据分层

```
Kms.Core
  Model/        DayRecord, AppUsage, ActiveSession, KeyHold, CrossDay, DeviceProfile
  Stats/        纯函数：输入模型，输出指标（不碰 IO、不碰 UI）
  Abstractions/ IClock, IDataContext, IEventSink
Kms.Storage
  Dto/          表行映射（WindowSessionRow, ClickCellRow …）
  Repos/        增删查（不含业务判断）
  Migration/    一次性脚本，幂等
```

规则：

1. `Model` 不带行为，`Stats` 不改模型（纯函数）。
2. `Dto` 与 `Model` **不共用类型**（v3 是共用的，导致 `DayRecord` 既当内存模型又当持久化结构）。
3. `IClock` 注入，测试用假时钟——v3 直接 `DateTime.Now`/`Stopwatch`，测试只能靠重置全局状态。

---

## 7. 隐私规格（v4 不得放宽）

| 数据 | v3 处理 | v4 要求 |
|---|---|---|
| 窗口标题 | 记录（base64 存文本档），单日上限 2000 条，超限塌缩为 `(process, "")` 并打 `overflow`（`DbWriter.cs:179/556-588`） | 保持一致；上限与塌缩规则不变 |
| 进程路径 | 记录 | 一致 |
| 应用条目 | 单日上限 2000，超限塌缩为「其他窗口（当日标题数量已达上限）」（`AppActivity.cs:433/557-561`） | 一致 |
| 上屏文字 | 不记窗口标题、不与窗口复合（`RimeLog.cs:17/134`）；界面默认模糊、悬停才明文（`TextPanel.cs:809/895/919/1239`） | **默认必须仍是模糊** |
| 防截屏 | **没有** `SetWindowDisplayAffinity`（全仓无此调用，仅文案提到「不截屏」） | 若要做，属新功能，不在复刻范围 |
| 导出脱敏 | `PrivacyMask` 纯函数，**默认关闭**（`PrivacyMask.cs:17-21`） | 默认关闭不变 |
| 联网 | 不联网（`README.md:101` 声明） | 唯一例外是用户显式配置的 Webhook（`AutomationTrigger`） |
