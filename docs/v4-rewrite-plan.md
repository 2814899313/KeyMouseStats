# 键鼠统计 v4 复刻方案

> 目标：用 **.NET 10 LTS + Avalonia 12.1 + 自研输入层** 重写 v3.4.1，换取
> ① 更广的兼容性（ARM64 原生、混合 DPI、可绿色免安装）
> ② 更高的画面质量（GPU 合成、矢量主题、真动画）
> ③ 突破 GDI+ 软件光栅的性能天花板
>
> 编写依据：对 95 个源文件 / 45,843 行代码的实测勘察，以及本机 `ThemePerf` 基准实测值。

## 方案分册

| 文档 | 内容 |
|---|---|
| **本文** | 决策、架构、模块映射、阶段、风险、验收 |
| [`baselines.md`](baselines.md) | 全部实测基线数字（性能 / 体积 / 数据样本） |
| [`v4-plan/01-ui-spec.md`](v4-plan/01-ui-spec.md) | 布局换算、字号阶梯、**13 套主题令牌全值**、9 页逐页规格、悬浮组、图表原语、golden 清单 |
| [`v4-plan/02-api-mapping.md`](v4-plan/02-api-mapping.md) | GDI+/WinForms → Avalonia 12.1 逐条映射、无等价物清单、保留的 P/Invoke |
| [`v4-plan/03-data-spec.md`](v4-plan/03-data-spec.md) | 领域模型、13 张表、时间单位、**24 条统计口径陷阱**、迁移不变量、隐私规格 |
| [`v4-plan/04-wbs.md`](v4-plan/04-wbs.md) | 159 人日任务级分解、关键路径、第一周计划 |
| [`v4-plan/README.md`](v4-plan/README.md) | 分册索引与前置条件 |

---

## 0. 结论先行

| 问题 | 结论 |
|---|---|
| 能不能做 | **能。** 逻辑层可利用率高（约 44% 代码可搬运），数据层已有良好接缝（`IStatsStore`） |
| 是不是 1:1 | **不是。** 数字可以做到 1:1（可验收），版式可做到结构 1:1，**像素不可能 1:1**（渲染引擎不同） |
| 会不会更快 | **会，而且提升是数量级的。** 静态帧 11.05 ms → 目标 ≤1.5 ms；动画从 33–125 fps 抖动 → 稳定 120 fps |
| 代价是什么 | 体积从 49.0 MB 涨到约 90–120 MB；2 MB「清爽版」闸门必然作废；单人全职约 **9.5 个月**（159 人日，见 [`04-wbs.md`](v4-plan/04-wbs.md)） |
| 最大风险 | 透明/点击穿透悬浮窗在 Avalonia 上的行为差异；82 个测试靠反射进 `Dashboard`，迁移期必须先重构接缝 |

**DeftSharp 的结论（与你设想不同）：不建议引入，或只在非计数路径做可选项。** 详见 §11 风险 R3。

---

## 1. 现状基线（全部实测，非估算）

### 1.1 代码规模

| 项 | 数量 |
|---|---|
| 产品源码 | 95 个 `.cs` / **45,843 行** |
| 类型总数 | 323 个 |
| 测试代码 | 104 个 `.cs` / 15,371 行（82 逻辑 + 15 渲染 + 1 基准） |
| 最大单元 | [Dashboard.cs](../Dashboard.cs) 6,055 行；[KeyMouseWidget.cs](../KeyMouseWidget.cs) 3,880 行 |
| 第三方 NuGet | **0** |
| 原生依赖 | 仅 `e_sqlite3.dll` 3.41.2（x64 1,691,648 B / x86 1,308,672 B），编译期内嵌 |

### 1.2 性能基线（本机实测，2026-11-22）

`test.bat ThemePerf`（[tests/ThemePerf.cs](../tests/ThemePerf.cs)，对 1072×724 客户区连续 30 次 `OnPaint` 取均值）：

```
Keyboard 1x:   11.05 ms/frame      ← 1072×724，单帧纯 CPU 光栅
Keyboard 1.5x: 13.74 ms/frame      ← 1608×1086
```

这是**单帧静态重绘**的成本，不含窗口合成、不含 DWM、不含 800 次画刷分配的开销。换算填充率约 **70 Mpx/s**，是 GDI+ 软件光栅的典型水平。

关键判断：**v3 的瓶颈不在代码，在平台。** 因为 [Dashboard.cs:423-425](../Dashboard.cs) 已经实现了 32bppPArgb 场景缓存位图、[Dashboard.cs:2148-2165](../Dashboard.cs) 已经做了 `SetClip` 脏区 + 整窗 Blit、[Dashboard.cs:552-558](../Dashboard.cs) 甚至记录了「局部失效会残留上一帧折线」的踩坑注释。该做的软件优化都做了，仍然 11 ms/帧——这正是必须换平台的证据。

### 1.3 部署基线

| 项 | v3 现状 |
|---|---|
| 正式版体积 | `KeyMouseStats.exe` 49.0 MB（89 条内嵌资源：77 jpg + 20 png + ico） |
| 清爽版体积 | `KeyMouseStats.Clean.exe` 1.10 MB（零内嵌资源，CI 断言 ≤2 MiB，[build.yml:31-41](../.github/workflows/build.yml)） |
| 运行时依赖 | 系统自带 .NET Framework 4.x（[build.bat:4-5](../build.bat) 用 `v4.0.30319\csc.exe`） |
| 权限 | `requireAdministrator`（[app.manifest:13](../app.manifest)，为绕过 UIPI 捕获提权游戏的输入） |
| DPI | PerMonitorV2（[app.manifest:29-30](../app.manifest)），代码内 `WM_DPICHANGED` 重算 + 五种不同命名的缩放因子 |
| 目标系统 | Win7 / 8.1 / 10 / 11（manifest `supportedOS`） |

### 1.4 架构基线（这部分是资产）

| 已有设计 | 位置 | 对 v4 的价值 |
|---|---|---|
| 存储后端接缝 `IStatsStore` | [StatsStore.cs:57-77](../StatsStore.cs) | 可直接沿用，v4 换 SQLite 实现即可 |
| 无锁 SPSC 环形缓冲 | [EventRing.cs:70-71](../EventRing.cs)（32768 容量） | 原样搬运，钩子零分配的关键 |
| 单写线程 + 512 事件/1s 批量事务 | [DbWriter.cs:168](../DbWriter.cs)、[:283-291](../DbWriter.cs) | 原样搬运 |
| 13 张表 schema + `schema_rev` | [KmsSchema.cs:25](../KmsSchema.cs)、[:37-52](../KmsSchema.cs) | 数据库格式不变，v4 可直接读 v3 库 |
| 迁移守恒断言 | [V2Migrator.cs:683-725](../V2Migrator.cs) | 移植为 v4 的验收用例 |
| 布局纯函数 | [LayoutFrame.cs:28-38](../LayoutFrame.cs)、[LayoutFlow.cs](../LayoutFlow.cs) | 语义可搬，数值需按 DIP 重算 |
| 迟滞状态机 / 口径纯函数 | `FlowAnalyzer` `RangeAnalysis` `ContextCost` `TrendCutoff` 等 | 高比例直接搬运 |

---

## 2. 把「1:1」重新定义成可验收的东西

原话「1:1 复现」在跨渲染引擎时不成立。建议拆成三层，各配验收手段：

| 层级 | 定义 | 验收手段 | 容差 |
|---|---|---|---|
| **L1 数据 1:1** | 同一份输入 → 所有统计数字完全相同 | 差分对拍（§10.2） | **0 差异**（浮点用相对误差 1e-9） |
| **L2 结构 1:1** | 每个页面的元素位置/层级/信息架构一致 | golden 截图 + 几何断言 | 元素边界 ±2 px，颜色 ΔE ≤ 2 |
| **L3 像素 1:1** | 逐像素完全相同 | — | **明确放弃**（字体栅格化/抗锯齿不同） |

对外表述建议改为：**「数据与版式 1:1，画质全面升级」**。海报导出图（`DailyReportPoster` 1440×3）会与 v3 不同，这是设计决定而非缺陷。

---

## 3. 技术栈定版

| 组件 | 选定 | 依据 | 备注 |
|---|---|---|---|
| 运行时 | **.NET 10 LTS** | 2025-11-11 发布，支持到 **2028-11-14**（[官方支持策略](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)） | ⚠️ **不要选 .NET 8**：LTS 已于 2026-11-10 到期，今天已在支持期外 |
| UI | **Avalonia 12.1.x** | 当前稳定线（NuGet 12.1.6，2026-11 更新）；Windows 上默认 PerMonitor DPI、Skia 渲染 | 锁 patch 版本；12 是大版本，M3 先做 spike |
| 渲染 | Skia（Avalonia 内置）+ `Composition` 图层 | 静态层可缓存为合成层，动效只改变换矩阵 | 这是拿到 120fps 的核心手段 |
| 输入（计数） | **自研移植**（钩子 + Raw Input） | v3 已有看门狗与 30ms 去重，且设备身份必须靠 Raw Input | 见 §11 R3 |
| 输入（键盘/鼠标订阅） | 可选 `DeftSharp.Windows.Input` | 仅用于非计数的「快捷键拦截」类需求 | **不用于统计计数** |
| 存储 | `Microsoft.Data.Sqlite`（SQLitePCLRaw） | 省掉 300 行裸 P/Invoke 与 `NativeBoot` 释放逻辑 | 数据库文件格式与 v3 完全兼容 |
| 测试 | xUnit + 自研 golden 比对 + BenchmarkDotNet | 替代 csc 编译独立 exe 的土办法 | 需要保留「单文件可跑」的调试便利 |
| 打包 | `dotnet publish` self-contained + ReadyToRun，single-file | 保持绿色免安装体验 | 见 §11 R6 体积预算 |

**明确不引入的东西**（避免过度工程）：不引入 MVVM 框架全家桶（CommunityToolkit.Mvvm 够用）、不引入 ORM（表结构是手写 DDL，用 `SqliteCommand` 即可）、不引入 DI 容器（构造注入 + 一个 `AppServices` 静态组合根足够）。

---

## 4. 目标架构

### 4.1 解决方案结构

```
KeyMouseStats.sln
├─ src/
│  ├─ Kms.Core/                 net10.0          → 纯逻辑，零 UI 依赖，可 Linux 跑测试
│  │   ├─ Model/                DayRecord, AppUsage, HoldRecord, DeviceProfile…
│  │   ├─ Stats/                RangeAnalysis, FlowAnalyzer, ContextCost, HourProfile,
│  │   │                        TrendCutoff, AxisScale, AppFlow, ReleaseAnalytics,
│  │   │                        PeriodReport, DailyReport, PressDuration, ShortcutStats
│  │   ├─ Layout/               LayoutFrame, LayoutFlow, ChipLedger, TimelineWindow, TimelineDrag
│  │   └─ Time/                 IClock, UnixTime, MonotonicClock
│  ├─ Kms.Storage/              net10.0          → 数据访问 + 迁移 + 打包
│  │   ├─ Schema/               KmsSchema（13 表，schema_rev 保持 1）
│  │   ├─ Sqlite/               SqliteStatsStore（替换 KmsDb 的 P/Invoke 层）
│  │   ├─ Text/                 TextStatsStore（v13 降级后端）
│  │   ├─ Migration/            V2Migrator, EpochFix, SnapshotBridge
│  │   └─ Package/              KmsPackage（.kms 增量包）
│  ├─ Kms.Input/                net10.0-windows  → 采集层，可在控制台宿主里跑
│  │   ├─ Hooks/                WH_KEYBOARD_LL / WH_MOUSE_LL + 看门狗
│  │   ├─ RawInput/             WM_INPUT 解码、RawMouseInput/RawKeyInput
│  │   ├─ Devices/              键盘/鼠标身份、友好名、磨损模型
│  │   └─ Activity/             活跃/空闲积分（GetLastInputInfo）
│  ├─ Kms.Render/               net10.0 + Avalonia → 图表与排版原语（无窗口、无 VM）
│  │   ├─ Tokens/               ThemeTokens（13 套 + 自定义皮肤）
│  │   ├─ Charts/               折线/柱/热区/键盘图/环图 的几何计算与绘制
│  │   └─ Text/                 文本度量缓存、省略号、字体回退链
│  ├─ Kms.App/                  net10.0-windows + Avalonia 12.1 → 应用外壳
│  │   ├─ Views/                Dashboard(9 页)、Widget、FocusPill、GameHud、Dialogs
│  │   ├─ ViewModels/
│  │   ├─ Theming/              13 套主题资源 + 自定义皮肤引擎
│  │   ├─ Posters/              海报/战报导出（RenderTargetBitmap → PNG）
│  │   └─ Interop/              透明/穿透/置顶/UIPI 等 P/Invoke 兜底
│  └─ Kms.Oracle/               net10.0          → 差分对拍工具（§10.2）
└─ tests/
   ├─ Kms.Core.Tests/           是从旧 82 个逻辑测试迁移
   ├─ Kms.Storage.Tests/
   ├─ Kms.Input.Tests/          看门狗、去重、设备归因
   ├─ Kms.Golden.Tests/         截图基线（v3 没有，必须新建）
   └─ Kms.Benchmarks/           替代 ThemePerf
```

### 4.2 依赖方向（严格，用 `<ProjectReference>` + 架构测试守住）

```
        ┌──────────────┐
        │   Kms.App    │  Avalonia 外壳、窗口、视图
        └──────┬───────┘
       ┌───────┴────────┬─────────────┐
       ▼                ▼             ▼
 ┌───────────┐   ┌────────────┐  ┌──────────┐
 │ Kms.Render│   │ Kms.Input  │  │Kms.Storage│
 └─────┬─────┘   └──────┬─────┘  └────┬─────┘
       └────────┬───────┴─────────────┘
                ▼
          ┌──────────┐
          │ Kms.Core │  ← 不许反向依赖任何上层
          └──────────┘
```

规则：
1. `Kms.Core` 不得引用 Avalonia、不得引用 `System.Windows.Forms`、不得有 P/Invoke。
2. `Kms.Render` 可以有 Avalonia，但**不许**有 `Window`、不许有 `Timer`、不许有数据库访问。
3. `Kms.Storage` 不许引用 Avalonia。
4. 架构测试：用程序集引用断言（`typeof(Core).Assembly.GetReferencedAssemblies()`）在 CI 里守住。

### 4.3 三个关键重构决策

**决策 1：`Store` 全局静态单例 → `IDataContext` 注入。**
现在 `Store` 是 [KeyMouseWidget.cs:758](../KeyMouseWidget.cs) 的静态类，`DayRecord` 就在它上面（[:149](../KeyMouseWidget.cs)），测试靠重置 `Store.History.Clear()` 保证隔离。v4 改为 `IDataContext` 实例，测试各建一个。**这一步不做，82 个测试就无法并行跑。**

**决策 2：`Dashboard` 的 9 个页签 → 9 个 View + 9 个 ViewModel。**
现在 9 个页面是 `partial class Dashboard` 分散在 8 个文件（`AppPanel.cs`、`TextPanel.cs`、`ClickHeatmap.cs`、`InsightsFlow.cs`、`DailyReportPanel.cs`、`HourlyTrend.cs`、`KeyboardHeatmap.cs`…），共用 44 个私有字段级的绘制状态。v4 每个页面是独立的 `UserControl` + `ViewModel`，共享的只有 `PageContext`（主题令牌、布局基准、命中区登记）。

**决策 3：命中测试与绘制解耦。**
现在悬停命中靠 `ChipLedger`（[ChipLedger.cs:27-43](../ChipLedger.cs)）在绘制时顺手登记矩形——绘制的顺序即命中的顺序，非常隐晦但有效。v4 改为：绘制产出**命中区列表**（`IReadOnlyList<HitTarget>`），由 View 统一处理鼠标事件。保留「同一次布局产生绘制与命中」的语义，但不绑定到 `Graphics` 调用顺序。

---

## 5. 模块映射表（95 个文件全覆盖）

图例：**搬运** = 改语法即可；**改写** = 保留算法、重做接口；**拆解** = 一个文件拆成「逻辑 + UI」两处；**重画** = 像素层重写。

### 5.1 纯逻辑内核（→ Kms.Core，约 6,100 行）

| 文件 | 行数 | 方式 | 说明 |
|---|---|---|---|
| UnixTime.cs | 36 | 搬运 | 唯一 Unix 毫秒基准，`62135596800000` 常量必须保留 |
| AxisScale.cs | 52 | 搬运 | 纯数学 |
| TrendCutoff.cs | 104 | 搬运 | 「今天未结束」同期裁剪口径 |
| HourProfile.cs | 62 | 搬运 | 注意 metric 2 单位是分钟，与 0/1 的「次数」混用同一矩阵 |
| AppFlow.cs | 176 | 搬运 | APM 唯一口径 |
| RangeAnalysis.cs | 389 | 搬运 | `Sd` 除 n-1、分位线性插值、`BuildBins` 末箱塞最大值 |
| ContextCost.cs | 134 | 搬运 | 上下文切换损耗 |
| FlowAnalyzer.cs | 677 | 搬运 | ⚠️ [FlowAnalyzer.cs:94](../FlowAnalyzer.cs) `DeepMinMinutes = 10`，与文件头注释「15 分钟」矛盾；**已定取 10**（以行为为准），并同步改注释，见 §13 D9 |
| PeriodReport.cs | 394 | 搬运 | 周报/月报聚合 |
| DailyReport.cs | 522 | 搬运 | 只读 Rime 日志，不画 |
| ReleaseAnalytics.cs | 191 | 搬运 | 交叉遥测、键位六桶 |
| PressDuration.cs | 419 | 拆解 | 算法（单调时钟、8 档直方图、墙钟并集）搬走；设置 UI 重画 |
| ShortcutStats.cs | 226 | 拆解 | 组合键状态机搬走；`ShortcutSavingsSettings` 窗口重画 |
| CategoryService.cs | 168 | 搬运 | 责任链 + 四值封闭枚举 |
| PrivacyMask.cs | 73 | 搬运 | 脱敏纯函数，默认关闭 |
| KmsPackage.cs | 266 | 搬运 | `.kms` 包 SHA256 + 幂等账本 |
| ChipLedger.cs | 40 | 改写 | 保留语义，脱离绘制调用顺序（§4.3 决策 3） |
| TimelineWindow.cs | 108 | 搬运 | 秒↔像素纯函数，已有单测 |
| TimelineDrag.cs | 161 | 搬运 | 拖动状态机，纯逻辑 |
| LayoutFrame.cs | 274 | 改写 | 基准画布 1072×724 的语义保留，数值改为 DIP 语义 |
| LayoutFlow.cs | 153 | 改写 | `TextFit` 里 3 个方法依赖 `Graphics.MeasureString`，改用 Avalonia 的 `FormattedText` |
| FocusMonitor.cs | 286 | 搬运 | 滞回状态机 |
| FocusBreak.cs | 181 | 搬运 | 微休息状态机 |
| KeyPalette.cs | 255 | 搬运 | 按键→色槽 HashMap + HSL |
| SkinPrompt.cs | 181 | 搬运 | 提示词模板，纯字符串 |
| AppTelemetry.cs | 67 | 改写 | 逻辑纯，但依赖静态 `Store` |
| EventRing.cs | 159 | 搬运 | 无锁 SPSC，`Capacity=32768` 原样保留 |
| Motion.cs | 409 | 改写 | 时间轴算法搬走；`SystemParametersInfo` 动画偏好探测移到 App 层 |

### 5.2 存储与迁移（→ Kms.Storage，约 3,400 行）

| 文件 | 行数 | 方式 | 说明 |
|---|---|---|---|
| KmsSchema.cs | 438 | 搬运 | 13 表 DDL + `schema_rev=1` **一字不改**（[KmsSchema.cs:25](../KmsSchema.cs)） |
| KmsDb.cs | 734 | 改写 | 24 个 `sqlite3_*` P/Invoke → `Microsoft.Data.Sqlite`；否则整层保留 |
| DbWriter.cs | 1,033 | 改写 | 写线程/批量/状态机全保留；SQL 文本因驱动替换需重测 |
| V2Migrator.cs | 745 | 改写 | 守恒断言（[:683-725](../V2Migrator.cs)）移植为测试用例 |
| StatsStore.cs | 181 | 搬运 | 接口与选择器逻辑直接可用 |
| NativeBoot.cs | 302 | **删除** | 由 NuGet 包的原生库加载取代（清爽版桩一并消失） |
| DataManagement.cs | 522 | 改写 | 备份轮转/校验逻辑保留，入口改为 CLI + UI |

### 5.3 输入与设备（→ Kms.Input，约 4,000 行）

| 文件 | 行数 | 方式 | 说明 |
|---|---|---|---|
| KeyboardDeviceIdentity.cs | 276 | 搬运 | 注册表 `FriendlyName` + FNV-1a Id + 内置键盘判定 |
| MouseDeviceIdentity.cs | 174 | 搬运 | 复用键盘的路径/注册表函数 |
| PeripheralWear.cs | 955 | 搬运 | ⚠️ 标定常数在 [:275-277](../PeripheralWear.cs)：键盘 5000 万次 / 鼠标 8000 万次 |
| PeripheralMouseWear.cs | 823 | 搬运 | 近 7 天点击环 |
| ClickScreenIdentity.cs | 99 | 搬运 | 多屏身份 |
| MouseDistance.cs | 408 | 拆解 | WHEEL_DELTA 余数结算 + DPI↔米换算搬走；校准对话框重画 |
| ActivityTime.cs | 361 | 拆解 | 积分算法搬走；两个设置窗重画 |
| AppActivity.cs | 651 | 改写 | 窗口捕获 P/Invoke 保留；分类/聚合逻辑搬走 |
| ShellNotification.cs | 48 | 搬运 | `SHQueryUserNotificationState` |
| AutoStart.cs | 427 | 改写 | 计划任务 COM 迟绑定保留，需在 v4 重新验证 |
| AutomationTrigger.cs | 160 | 改写 | 异步脚本/Webhook |
| Wellbeing.cs | 278 | 拆解 | 判定与文案搬走；全屏几何探测留输入层 |
| RimeLog.cs | 1,022 | 搬运 | 纯文件解析 |
| SampleData.cs | 271 | 改写 | 生成算法搬走；`Screen.AllScreens` 换 Avalonia 的 Screens |

### 5.4 UI 与渲染（→ Kms.Render + Kms.App，约 32,000 行）

| 文件 | 行数 | 方式 | 说明 |
|---|---|---|---|
| Dashboard.cs | 6,055 | **重画 + 拆 9** | 外壳（侧栏/页签/页脚/主题选择器）+ 9 个页面各成 View |
| KeyMouseWidget.cs | 3,880 | **重画 + 拆 4** | 窗口 / 4 模式绘制 / 钩子宿主 / 托盘，必须彻底拆开 |
| ClickHeatmap.cs | 1,501 | 重画 | `PathGradientBrush.SetSigmaBellShape` 在 Skia 无对应物，改用径向渐变 + 多层模糊；几何 `heatLeft = Cw * 0.721f`（[:899-926](../ClickHeatmap.cs)）需重算 |
| CustomTheme.cs | 1,713 | 改写 | 抽色算法搬走；`BuildComposite` 的位图烘焙改 `RenderTargetBitmap`；线程/代际失效机制保留 |
| CustomThemeDialog.cs | 1,551 | 重画 | 30 处原生控件 + 5 个自绘子面板 |
| TextPanel.cs | 1,250 | 重画 | 8 张字符统计图 |
| AppPanel.cs | 1,093 | 重画 | 列表 + APM + 占比环 |
| DailyReportPoster.cs | 980 | **改写** | 排版算法（`Wrap`/`PickFamily` 回退链，[:1012-1031](../DailyReportPoster.cs)）搬走，绘制改 `RenderTargetBitmap`；接受像素差异 |
| PeriodPoster.cs | 938 | 改写 | 同上，1440×3 输出 |
| ReportDesign.cs | 652 | 重画 | `ReportPage/Deck/Header/HelpOverlay/IconButton` 五件套 → Avalonia 控件 |
| ReportHabits.cs | 550 | 重画 | 纯像素层 |
| StatisticsReport.cs | 544 | 拆解 | `PersonalStats` 口径搬 Core（[:13-59](../StatisticsReport.cs)）；5 页图表重画 |
| DailyReportPanel.cs | 541 | 重画 | 5 张卡 |
| InsightsFlow.cs | 541 | 重画 | 心流看板 + 时光机 |
| KeyboardHeatmap.cs | 537 | 重画 | 6 种配列几何是数据表，绘制薄，性价比最高 |
| FocusPillForm.cs | 537 | 重画 | 无边框 + `WS_EX_NOACTIVATE` + 展开动画 |
| RangeReport.cs | 535 | 拆解 | 两个 `Build` 搬 Core；两个 Visual 重画 |
| HourlyTrend.cs | 436 | 重画 | `HourlyAxis` 搬 Core，绘制重写 |
| PeripheralHealthView.cs | 456 | 重画 | `HealthLayout` 纯函数保留 |
| PressReport.cs | 493 | 拆解 | 两页数据 `Build` 搬 Core；绘制重画 |
| Insights.cs | 493 | 拆解 | `TimeInsights`（[:12-46](../Insights.cs)）搬 Core；6 个 `Paint*` 重画 |
| FlowBoard.cs | 278 | 搬运 | 已是纯数据层 |
| ReleaseReport.cs | 236 | 拆解 | `CrossReportData.Build` 搬 Core |
| WeeklyReview.cs | 211 | 拆解 | `RhythmReportData.Build` 搬 Core |
| HoldTrendReport.cs | 210 | 拆解 | `HoldTrendData.Build` 搬 Core |
| BattleReport.cs | 211 | **改写** | `MeasureHeight`/`Paint` 手工互锁（注释自认易错）→ 改 Avalonia 的两遍布局，天然消除该风险 |
| DailyReportBattle.cs | 152 | 改写 | 落盘 + 剪贴板 → Avalonia `Clipboard` |
| Themes.cs | 506 | 改写 | `ArtTheme` 13 套（[Themes.cs:160-174](../Themes.cs)）→ 主题资源字典；`HeatScale`/`ChannelScale` 搬 Render |
| NikkiTheme.cs | 320 | 改写 | 贴图异步解码 + 96MB 预算（[:36](../NikkiTheme.cs)）+ `DrawCached` 缩放缓存 → Avalonia `Bitmap` + 自定义绘制 |
| CustomThemeDialog / WellbeingSettingsDialog / DataSettingsDialog | 1,551+170+305 | 重画 | 原生控件堆叠，改写成本低于重画，但主题要统一 |
| KmsUi.cs | 161 | 重画 | 4 个系统对话框 → Avalonia 对话框 |
| IconPack.cs | 559 | 改写 | 矢量图标生成搬 Render；抠底烘焙改 `WriteableBitmap` |
| UiPrimitives.cs | 24 | 搬运 | 圆角路径工厂 → 直接用 Avalonia `CornerRadius` |
| GameHud.cs | 66 | 重画 | 330×92 穿透 HUD |
| FocusBreakToast.cs | 382 | 重画 | 淡出 + 三按钮命中 |
| 9 套美术主题（Halo 41 / Resident 32 / Minecraft 55 / EuroTruck 18 / Wuxia 106 / Ori 75 / Gujian 84 / Balatro / Nikki 贴图） | 约 500 | 重画 | 纯转发类，薄；但贴图 → 矢量的决策影响成本 3 倍（见 §13 D6） |
| AssemblyInfo.cs / Version.cs | 17+22 | 改写 | → csproj 的 `<Version>` + 生成 `BuildInfo` |

**汇总**：搬运 ≈ 6,100 行（13%）；改写/拆解 ≈ 14,300 行（31%）；重画 ≈ 25,400 行（55%）。

---

## 6. 阶段计划

单位：**人日**（1 人日 = 1 名工程师 1 天）。总计 **159 人日**，含 30% 风险缓冲约 **207 人日**。
任务级分解见 [`v4-plan/04-wbs.md`](v4-plan/04-wbs.md)。

| 阶段 | 内容 | 人日 | 关键验收（退出条件） |
|---|---|---|---|
| **M0** 立项与基线 | 建 sln/目录/CI；固化 v3 基线数字；导出真实数据固件 | 5 | ①CI 绿 ②`docs/baselines.md` 有实测数字 ③固件入库（含 3 种真实库 + 坏档 + `.kms` 包） |
| **M1** 纯逻辑内核 | `Kms.Core` + `Kms.Storage`；`Store` 拆成 `IDataContext`；13 表 schema 保持；文本后端；迁移 | 20 | **L1 差分对拍全绿**：固件上所有数字与 v3 完全一致 |
| **M2** 输入采集 | 钩子移植 + 看门狗 + Raw Input + 设备身份 + 磨损；无 UI | 15 | 同机与 v3 并行 24h，计数误差 < 0.1%；设备识别无一错配；P99 回调 < 200µs |
| **M3** 渲染内核 | `ThemeTokens`；13 套主题；`Kms.Render` 图表原语；**golden 基线体系**；DPI 三档 | 25 | 在 1x/1.5x/2x 下渲染 1 个代表性页面，几何 ±2px、静态帧 < 2ms |
| **M4** 主面板 | 9 个页面逐个迁移（每页含 VM + View + 命中区 + 交互） | 45 | 每页上线即过 golden + 交互冒烟；整窗动画稳定 120fps |
| **M5** 悬浮组与托盘 | Widget 4 模式、FocusPill、FocusBreakToast、GameHud、托盘菜单 | 18 | 跨屏拖动不变形；点击穿透；不抢焦点；`Opacity` 正确 |
| **M6** 报表与导出 | 报表窗 5 页；海报/战报 `RenderTargetBitmap` 4320px 输出 | 18 | 长图文字无截断；导出 < 2s；脱敏开关生效 |
| **M7** 打包与发布 | single-file + R2R；体积预算；双轨并存；灰度与回滚演练 | 10 | 体积达标；首启导入 v3 数据无损；回滚演练通过 |

### 6.1 排期方式

| 人员配置 | 日历工期 | 说明 |
|---|---|---|
| 单人全职 | **约 9.5 个月**（159 人日 ÷ 5 × 风险系数 1.3 ≈ 41 周） | M3/M4 是长尾 |
| 双人 | **约 5 个月** | 建议分工：A 做 M1+M2（后端与输入），B 做 M3+M4（渲染与页面） |
| 三人 | 约 3.5 个月 | 但 M4 的 45 人日难再拆，收益递减 |

### 6.2 推荐的最小可用路径（MVP，先跑起来）

如果不想等 9 个月才看到东西，**建议先切一条 MVP 纵深**（约 8 周）：

```
M0(5) → M1 核心存储(12) → M2 输入(15) → M3 令牌+1 个页面(10)
```

产出：**能装、能采集、能看「今日概览」一个页面的 v4.0-alpha**。用它验证三件事：透明悬浮窗手感、DPI 行为、真实用户的兼容性反馈。**这三件事的答案会决定剩下 25,000 行要不要按原计划重画。** 我强烈建议先走这条，而不是一次排到 M7。

---

## 7. 性能工程

### 7.1 目标预算

| 场景 | v3 实测 | v4 目标 | 手段 |
|---|---|---|---|
| 静态页重绘 | **11.05 ms** | **≤1.5 ms** | 静态层缓存为合成层，重绘只做拷贝 |
| 动画帧 | 8–30 ms 抖动（33–125 fps） | **≤8.3 ms 稳定 120 fps** | GPU 合成；只改变换矩阵 |
| 钩子回调 | 未量化 | **P99 < 200 µs，零堆分配** | 沿用 `EventRing` + 值类型事件 |
| 启动到托盘 | 未量化 | ≤1.5 s | ReadyToRun + 延迟加载贴图 |
| 内存 | 未量化 | ≤250 MB（含 96 MB 贴图预算） | 贴图按需解码 + 代际释放 |
| 无输入时 CPU | 250 ms 定时器常驻 | ≤0.1% | 改为事件驱动，去掉空转轮询 |

### 7.2 v3 的性能究竟花在哪（基于代码事实）

1. **每帧重新分配画刷组**：805 处 `new SolidBrush/Pen/LinearGradientBrush`，其中大量在 `OnPaint` 内。GDI+ 的 `Brush`/`Pen` 是托管包装 + 原生句柄，分配即意味着构造/析构 + GC 压力。
2. **整窗重绘是刻意的**：[Dashboard.cs:552-558](../Dashboard.cs) 注释写明局部失效会残留上一帧折线，所以动画帧必须整窗失效。
3. **软件光栅**：GDI+ 全部在 CPU 单线程合成，无 GPU 参与。11 ms ≈ 70 Mpx/s 就是它的能力上限。
4. **文本逐字测量**：`TextFit.Ellipsis/VisibleChars` 逐字调 `MeasureString`，`MeasureString` 全仓 60+ 处被当作布局决策依据。

### 7.3 v4 的六个具体手段

1. **图层化**：把「背景 / 静态图表 / 文字 / 动效元素」拆成独立视觉层，静态层烘焙成 `RenderTargetBitmap` 缓存，动效只推进变换。11 ms → 1 ms 主要靠这个。
2. **画刷常量化**：`ImmutableSolidColorBrush`/`ImmutablePen` 建静态字段，绘制期零分配。
3. **文本度量缓存**：`FormattedText` 按 `(文本, 字体, 字号, 宽度)` 做 LRU 缓存；省略号用 Avalonia 内置 `TextTrimming`，删掉逐字测量。
4. **命中区与绘制分离**（§4.3 决策 3）：悬停不再触发重绘，只更新悬停态视觉。
5. **主题切换走资源字典替换**：v3 靠 `RefreshThemeSurfaces()` 遍历 `Application.OpenForms` 全量 `Invalidate`（[Themes.cs:316-328](../Themes.cs)），v4 走动态资源绑定，不重建控件树。
6. **帧预算调度**：保留 v3 的 `Motion.FrameInterval` 自适应降级思路（弱机自动掉到 60/30 fps），不要盲目追 120 fps。

### 7.4 怎么证明性能变好了

- `Kms.Benchmarks` 里移植 `ThemePerf` 的等价基准（同样的 1072×724、同样的 30 帧均值），**同一台机器上跑 v3 与 v4 对比**，出柱状图。
- 加一条 CI 门禁：静态帧 > 3 ms 或动画帧 P95 > 10 ms 则构建失败。

---

## 8. 画质工程

### 8.1 现状：硬编码统治一切

| 项 | 数量 |
|---|---|
| `Color.FromArgb` 硬编码颜色 | **521** |
| `new Font(...)` 实例 | **131** |
| 字体族字面量（"Microsoft YaHei UI" 等） | **109** |
| 画刷/画笔构造 | 805 |
| 缩放因子变量名 | **5 种**（`_s` / `_dpiScale` / `_scale` / `_uiScale` / `scale`），无全局唯一函数 |

结论：画质升级的**前提是把这些数字收进设计令牌**，否则改一次配色要动 521 处。

### 8.2 设计令牌（`ThemeTokens`）

```csharp
// Kms.Render/Tokens/ThemeTokens.cs —— 目标形态
public sealed record ThemeTokens(
    Color Background, Color Card, Color Raised, Color Line,
    Color Text,     Color Muted, Color Accent, Color OnAccent,
    Color Green, Color Orange, Color Purple, Color Red, Color Cyan,
    Color Sidebar,
    double Radius,                      // 对应 ArtTheme.Radius
    Typography Typography);             // 字号阶梯 + 字体族 + 字重
```

- 13 套主题 + 1 套自定义皮肤 = 14 个令牌实例，**用 JSON 定义 + 强类型生成**，改主题只改一行。
- 对应关系：[Themes.cs:155-174](../Themes.cs) 的 `ArtTheme` 15 个颜色字段 + `Radius`，一一映射，不新增语义。

### 8.3 画面升级的四个着力点

| 着力点 | v3 | v4 | 收益 |
|---|---|---|---|
| **矢量主题** | 9 套主题靠 77 张 jpg + 20 张 png 贴图 | SVG/Path + 9-patch，任意 DPI 无损 | 体积↓、清晰度↑（需决策 D6） |
| **合成效果** | 圆角靠 `GraphicsPath`，阴影靠手画多层 | GPU `BoxShadow`/模糊/`AcrylicBlur` | 真正的毛玻璃与柔光 |
| **文本** | `TextRenderingHint.ClearTypeGridFit`（[Dashboard.cs:2189](../Dashboard.cs)） | Skia 抗锯齿 + 可选字形对齐；对关键数字保留字形 hinting | 笔画更平滑，但观感会变（需用户确认接受） |
| **动效** | 40 ms 定时器 + 手工插值，实测被夹在 8–30 ms | Avalonia `Animation` + 合成线程 | 稳定 120 fps、无抖动 |

⚠️ **API 缺失项要提前设计替代方案**：`PathGradientBrush.SetSigmaBellShape`（云状热斑）在 Skia 无对应物，`ClickHeatmap` 的云状效果需用「径向渐变 + 多层高斯近似」重做，视觉会有细微差别。

---

## 9. 数据兼容与迁移

### 9.1 原则：数据库格式不动，v4 只换访问层

- 13 张表、`schema_rev=1`、`daily_summary.origin` 四值、`epoch_fix_v1` 标记 —— **全部保持不变**（[KmsSchema.cs](../KmsSchema.cs)）。
- v4 首启：检测 `%LOCALAPPDATA%\KeyMouseStats\` 下的 v3 库 → 只读打开 → 校验 `user_version` → 备份后原地升级（不使用则新建）。
- `.kms` 增量包格式不变，v3/v4 可互传。

### 9.2 双轨并存（关键，别跳过）

```
v3 (3.x) 继续运行 ──写──> kms.db (v3 格式)
                              │ 只读
v4 (4.x) 新进程 ──────────────┘ 首启导入 → kms4.db (同 schema)
```

- v4 与 v3 **数据目录分离**，避免两个进程争 WAL 锁。
- v4 稳定后（建议 3 个月）再提供「从 v3 合并最新数据」的一次性工具，逻辑复用 `V2Migrator` 的快照桥（只补缺失日期）。

### 9.3 回滚

- v4 安装包自带 `--export-legacy` 参数，可把 `kms4.db` 导回 v13 文本格式。
- 保留 v3 安装包 6 个月。

---

## 10. 测试体系

### 10.1 现有 82 个逻辑测试的真实状况（这是隐藏工作量）

测试用反射钻进 `Dashboard` 的私有成员：

```csharp
// tests/KeyRangeTests.cs:4 —— 连纯区间逻辑都要先 new 一个 Form
using (Dashboard form = new Dashboard()) {
    FieldInfo tab = typeof(Dashboard).GetField("_tab", f);
    tab.SetValue(form, Enum.Parse(tab.FieldType, "Keys"));
    ...
}
```

**结论：82 个「逻辑测试」其实都依赖 WinForms 存在。** 迁移分三步：

| 步骤 | 动作 | 产出 |
|---|---|---|
| ① 抽接缝 | 把被测逻辑从 `Dashboard` 私有方法搬到 `Kms.Core` 公开函数 | 每个测试对应一个纯函数 |
| ② 换断言 | 手写 `throw new Exception` → xUnit `Assert` | 可并行、可定位 |
| ③ 保语义 | 用 v3 生成的期望值快照校验搬家没搬错 | 见 §10.2 |

### 10.2 差分对拍（保证 L1 数据 1:1）

新增 `Kms.Oracle`：把 v3 的 `kms_test_core.dll`（[test.bat:39](../test.bat) 已经在编它）当参考实现，对同一批固件输出 JSON。

> ⚠️ 参考实现必须**当次从源码编译**，不能拿仓库里现成的 `KeyMouseStats.exe` 当标准答案——
> 本仓库就发生过 exe 落后源码两个小版本（exe 3.2.2 / 源码 3.4.1）的情况。

```
固件（真实 v3 库 + 坏档 + .kms 包）
        │
        ├──> v3 Oracle  ──> expected.json   （参考值）
        └──> v4 Kms.Cli ──> actual.json
                              │
                       逐字段比对（1e-9）
```

固件必须包含**坑**，否则对拍没意义：

| 固件类型 | 要覆盖的坑 |
|---|---|
| 真实 v3 库 | `daily_summary` 的 `active_seconds` 从不更新（[DbWriter.cs:742-765](../DbWriter.cs)），直接读库算日均活跃会得 0 |
| 历史档 | `origin` 四值（`shadow`/`migrated`/`snapshot-bridge`/`native`） |
| 3995 年脏数据 | `EpochFixThresholdMs = 6e13` 修复路径（[KmsSchema.cs:333-380](../KmsSchema.cs)） |
| 只写日总数的老档 | `RepairDerivedCounts` 等比缩放 |
| 坏档 / 半截档 | 必须中止且不丢数据 |
| 跨时区 | 本地时区切日（[DbWriter.cs:876-889](../DbWriter.cs)）vs SQLite `'localtime'` |

需要重点核对的 24 处统计口径（完整清单见勘察记录），最容易搬错的 6 条：

1. `active_ms` 只累加 ≤15 s 的间隔（[DbWriter.cs:173](../DbWriter.cs)），>15 s 挂机不计但 `end_ts_ms` 照推。
2. 突发段三个分母不同：`key_count = Count+1`、`variance = M2/Count`、`backspace_rate = backspaces/(Count+1)`。
3. 滚轮：`WheelAccumulator` 按 120 结算留余数，但库里 `CountWheel` 只 `+1`（存事件次数，不是格数）。
4. 两套 APM：`AppFlow` 用 `(Keys+Clicks)*60/Seconds`，`PersonalStats` 用 `Keys/ActiveSeconds`。
5. 「未归位」是减法算出来的（`day.Keys - ΣKeyCounts`），可能为负需 `max(0, …)`。
6. 按住时长分档用严格 `<`，第 0 档 `<50ms`、末档 `≥5s`（[PressDuration.cs:186-191](../PressDuration.cs)）。

### 10.3 渲染 golden 基线（v3 完全没有，必须新建）

现状：15 个渲染测试会把图写进 `previews/`，但 **`previews/` 与 `tests/*.png` 都在 `.gitignore` 里**——仓库里没有任何基线图，测试只做冒烟 + 局部像素断言。这意味着**画质回归没有任何保护**。

v4 必须先补：

1. 为每个页面在 3 档 DPI（100%/150%/200%）各生成一张基线，入库（`tests/Baselines/`）。
2. 比对策略：不是逐像素相等，而是 **SSIM ≥ 0.98 + 关键元素包围盒 ±2px + 色板 ΔE ≤ 2**。
3. 基线变更必须走 PR review，防「悄悄改了外观」。

---

## 11. 风险登记

| ID | 风险 | 概率 | 影响 | 缓解 |
|---|---|---|---|---|
| **R1** | Avalonia 12 是新主版本，API/生态插件不全 | 中 | 高 | 锁 12.1.x patch；M3 前先做 5 天 spike 验证透明窗 + 自定义绘制 + 中文字体 |
| **R2** | 透明/点击穿透/无激活悬浮窗行为差异（Widget/FocusPill/GameHud/FocusBreakToast 全中） | **高** | 高 | M5 提到 M3 之后立即验证；P/Invoke `WS_EX_*` 兜底；准备「不透明圆角卡片」降级设计 |
| **R3** | **DeftSharp 覆盖不了设备身份** | **确定** | 中 | 见下 |
| R4 | `requireAdministrator` 不变（UAC 每次弹） | 高 | 中 | 换栈不解决；若真要免 UAC 需「服务 + UI 分离」架构，属独立项目，不在本方案内 |
| R5 | 低级钩子被反作弊拦（游戏场景） | 中 | 中 | 保持 Raw Input 为主、钩子为兜底；不放大风险面 |
| R6 | 体积 49.0 MB → 约 90–120 MB | **确定** | 中 | single-file + R2R；贴图转矢量；接受约 2 倍增长 |
| R7 | 「清爽版 ≤2 MB + 零资源」CI 闸门作废 | **确定** | 低 | 重定义为「零美术资源 + 共享运行时」或直接取消（见 D1） |
| R8 | 82 个测试靠反射进 `Dashboard`，无法直接迁移 | **确定** | 中 | 已含在 M1/M4 人日里（约 25 人日），不要漏算 |
| R9 | 字体缺失（`Cascadia Mono` 在部分机器不存在） | 中 | 低 | 嵌入字体或建显式回退链；v3 界面层当前**没有**回退（只有海报有） |
| R10 | `e_sqlite3` 3.41.2 → NuGet 内置版本升级后 WAL 兼容 | 低 | 高 | 用真实 v3 库做升级前后校验；保留手工回滚脚本 |
| R11 | golden 基线缺失导致画质回归不可控 | **确定** | 中 | M3 必须先建基线，再开始 M4 |
| R12 | ~~参考实现自身 5 项测试失败且未归类~~ | **已排除** | — | 2026-11-22 非沙箱重跑 **82/82 全绿**，5 项失败均为沙箱写权限限制，非产品缺陷。见 [`baselines.md`](baselines.md) §8 |

### R3 详解：为什么建议不引入 DeftSharp

三条事实：

1. **你现在已经在用同一套钩子。** [KeyMouseWidget.cs:2685](../KeyMouseWidget.cs)/[:2693](../KeyMouseWidget.cs) 就是 `SetWindowsHookEx(WH_KEYBOARD_LL/WH_MOUSE_LL)`，并且带看门狗：每 5 s 体检，识别「句柄变 0」与「句柄在但 30 s 无事件（被 `LowLevelHooksTimeout` 静默摘钩）」两种故障（[:2713-2719](../KeyMouseWidget.cs)）。DeftSharp 是同一组 P/Invoke 的封装，**不解决这两个故障**——换过去仍要自己写看门狗。
2. **它拿不到设备身份。** 项目有 64 处 Raw Input（`GetRawInputDeviceList`/`GetRawInputDeviceInfo`/`WM_INPUT`）支撑设备级磨损（`PeripheralWear.cs` 955 行 + `PeripheralMouseWear.cs` 823 行）。`WH_KEYBOARD_LL` 的 `lParam` 里**没有设备句柄**，低级钩子天然不知道「这次按键来自哪台设备」。DeftSharp 的 device information 是设备枚举，不是事件级来源归属（[项目说明](https://github.com/Empiree/DeftSharp.Windows.Input)）。所以磨损统计必须保留 Raw Input 自实现。
3. **它的收益是省样板代码。** 相对 4,000 行已验证的输入层，省下的几百行不值得放弃容错逻辑。

**建议**：`Kms.Input` 定义 `IInputSource` 接口，自研实现为默认；DeftSharp 作为可选实现保留在 `Kms.Input.DeftSharp` 分支里，**只允许用于非计数场景**（如「按键拦截/改键」这类未来需求）。M2 阶段可以花 2 人日做一次 A/B，用数据决定，而不是现在决定。

---

## 12. 发布策略

| 阶段 | 版本 | 范围 | 通道 |
|---|---|---|---|
| P1 | 4.0.0-alpha | MVP 纵深（M0–M3），仅内部 + 3–5 名志愿用户 | GitHub Pre-release |
| P2 | 4.0.0-beta | 9 页全量 + 悬浮组（M4–M5），公开 | Pre-release，与 3.x 并存 |
| P3 | 4.0.0 | 报表导出 + 打包（M6–M7） | 正式，3.x 进入维护 |
| P4 | 4.1 | 松动遗留项（贴图转矢量、性能二轮） | 正式 |

- 4.x 与 3.x **数据目录分离**，互不干扰，可同时安装。
- 4.x 首启提供「从 3.x 导入」向导，导入前自动备份（复用 `V2Migrator` 的 sha256 备份逻辑）。

---

## 13. 方案取值（已定，可直接据此施工）

下表是本方案的**结论性取值**，不是待办问题。需要变更时只改这张表，正文其他章节不必动。

| # | 决策 | 取值 | 依据 / 影响 |
|---|---|---|---|
| D1 | 清爽版（≤2 MB） | **保留，但闸门重定义**为「零美术资源 + 体积 ≤25 MB」 | 2 MB 在自包含发布下不可能；保留「轻量」卖点，撤掉不成立的门槛 |
| D2 | UAC 提权 | **继续 `requireAdministrator`** | 低级钩子要捕获提权游戏的输入（`app.manifest:7-12` 已写明）。彻底免 UAC 需「服务 + UI 分离」，是另一个项目 |
| D3 | 数据目录 | **独立目录**（`KeyMouseStats4`） | 避免 v3/v4 争 WAL 锁；回滚容易 |
| D4 | 双轨并存时长 | **6 个月** | 覆盖一个完整的用户升级周期，又不至于长期背两套 |
| D5 | 目标帧率 | **120 fps + 自适应降级** | 沿用 `Motion.FrameInterval` 的弱机退避思路，不盲目追帧 |
| D6 | 9 套贴图主题 | **先保留位图，4.1 再逐套转矢量** | 省约 25 人日；全案性价比最高的「先不优化」决定 |
| D7 | 文本渲染 | **关键数字保留字形 hinting**，正文交给 Skia | 大数字是这类应用的门面 |
| D8 | 交付节奏 | **按全量排期 M0→M7** | 一次交付完整版，不做 MVP 纵深 |
| D9 | `FlowConfig.DeepMinMinutes` | **10 分钟** | v3 代码实际行为是 10（`FlowAnalyzer.cs:94`）；文件头与 `:222` 注释写的 15 是过期描述。**复刻以行为为准，注释同步改成 10** |

> D6 / D8 已由你确认；D1–D5、D7、D9 由本方案给出默认值，如无异议即按此执行。

---

## 14. 不做 / 放弃清单（明确写下来，避免后期扯皮）

| 项 | 决定 | 理由 |
|---|---|---|
| 像素级 1:1 | 放弃 | 渲染引擎不同，投入产出比为零 |
| 海报/战报导出图与 v3 完全一致 | 放弃 | 字体度量与断行算法不同 |
| Win7 支持 | 放弃 | .NET 10 只支持 Win10 及以上（最低版本以官方支持矩阵为准）；Win7 用户占比可忽略 |
| 2 MB 清爽版 | 放弃或重定义 | 自包含发布不可能 2 MB |
| 单文件 51 MB 不变 | 放弃 | 预计 90–120 MB |
| 移植 `NativeBoot.cs` | 删除 | 由 NuGet 原生库加载取代 |
| 保留 `ActionFilterCommandLine` 式的壳层 | — | 无此代码，仅示意：不保留任何 GDI+ 绘制代码 |

---

## 附录 A：v3 资产清单（迁移时逐项对齐）

| 资产 | 位置 | 处理 |
|---|---|---|
| 89 条内嵌美术资源 | `assets/`（77 jpg + 20 png + ico） | 转 Avalonia `Asset` 或矢量 |
| 13 张表 DDL | [KmsSchema.cs](../KmsSchema.cs) | 保留，一字不改 |
| 82 个逻辑测试 | `tests/` | 三步迁移（§10.1） |
| 15 个渲染测试 | `tests/`（`RENDERS` 名单，[test.bat:67](../test.bat)） | 转 golden 基线（§10.3） |
| 2 个 CI 工作流 | `.github/workflows/` | 改写为 `dotnet build/test/publish` |
| 版本发布说明 | `docs/v*.md` | 保留历史，新增 `v4.*.md` |
| `design/` 视觉稿 | HTML + CSS + 6 张 preview PNG | 直接作为 v4 的视觉基准 |

## 附录 B：性能与画质验收表（填完这张表才算做完）

| 指标 | v3 基线 | v4 目标 | v4 实测 | 判定 |
|---|---|---|---|---|
| 静态页重绘 | 11.05 ms | ≤1.5 ms | | |
| 静态页重绘 @1.5x | 13.74 ms | ≤2.5 ms | | |
| 动画帧 P95 | 8–30 ms | ≤10 ms | | |
| 钩子回调 P99 | 未量化 | <200 µs | | |
| 启动到托盘 | 未量化 | ≤1.5 s | | |
| 内存峰值 | 未量化 | ≤250 MB | | |
| 空闲 CPU | 未量化 | ≤0.1% | | |
| 安装包体积 | 49.0 MB | ≤120 MB | | |
| 首启导入 v3 数据 | — | 0 丢失 | | |
| L1 差分对拍 | — | 0 差异 | | |
| golden 基线 | 不存在 | 全绿 | | |

---

*本文档基于 2026-11-22 的代码状态（v3.4.1）与实测数据编写。*
