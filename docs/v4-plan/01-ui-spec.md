# 01 · 界面复刻规格（UI Spec）

> 本文是 v4 的施工图：**每一页画什么、多大、画在哪、数据从哪来、交互怎么算。**
> 配套文档：`02-api-mapping.md`（GDI+ → Avalonia 对照）、`03-data-spec.md`（数据与口径）。

---

## 1. 布局基准与换算

### 1.1 基准画布（v3 唯一真源）

`Dashboard.cs:239-241`：

```csharp
private const int BW = 1072, BH = 724;      // 基准画布
private const int ContentX = 176, ContentY = 64;   // 内容区左上（侧栏 176 宽）
private const int TabY = 52, TabH = 40;            // 页签条
```

`LayoutFrame.cs:28-38` 再固化一批：`BaseW/BaseH = 1072/724`、`PageX/PageY`、
`RightMargin = 40`、`FooterBlockH = 58`、`MinWidth = 320`、`MinHeight = 240`。

### 1.2 v3 的两种缩放做法（v4 必须统一成一种）

| 位置 | 做法 | 代码 |
|---|---|---|
| Dashboard | 一次 `ScaleTransform(_s)`，之后全按 96dpi 逻辑像素画 | `Dashboard.cs:2190` |
| KeyMouseWidget / FocusPill / FocusBreakToast / PeripheralHealthView | 不设变换，逐点 `x * _s` | `KeyMouseWidget.cs:3139-3172` |
| 对话框 | `AutoScaleMode.None` + 手动 `_uiScale` | `CustomThemeDialog.cs:89-90` |

**问题**：没有全站唯一缩放函数，同一概念有 5 个变量名（`_s` / `_dpiScale` / `_scale` / `_uiScale` / `scale`）。

### 1.3 v4 的换算规则（一条公式，全站唯一）

```
逻辑尺寸(v3 像素 @96dpi)  ──×1.0──>  Avalonia DIP      ← 基准画布 1072×724 直接当 DIP 用
实际像素                  =  DIP × ScalingFactor(当前屏幕)
```

关键决定：**1072×724 的语义从「像素」改为「DIP」**。

理由与影响：

1. Avalonia 的 `ScalingFactor` 由平台给出（PerMonitorV2），不需要手算 `g.DpiX/96`。
2. 因此 v3 里所有 `S(v) = v * _s`、`x * _s`、`font = new Font(..., 13 * _s)` **一律删除乘数**，
   直接写字面值。这消除了 131 处字体乘法与 190 处尺寸乘法。
3. 窗口客户区：v3 用 `ClientSize = BaseW*_s × BaseH*_s`（`KeyMouseWidget.cs:2181-2183`）；
   v4 用 `Width = 320, Height = 210`（DIP），由 Avalonia 自动乘 ScalingFactor。
4. 唯一保留乘法的场景：**离屏导出**（海报），因为它要固定像素输出（见 §5）。

### 1.4 字号阶梯

v3 字体族（`Dashboard.cs:1085-1101`、`KeyMouseWidget.cs:2348-2351`）：

| 用途 | 字族 | 字号（逻辑像素） |
|---|---|---|
| 标题 / 正文 | `Microsoft YaHei UI` | 25 / 14 / 13 / 12 / 11 |
| 大数字 / 轴标 | `Segoe UI` | 38 / 29 / 21 / 19 |
| 键名 | `Cascadia Mono` | 11 |
| HUD | YaHei UI 10 + Segoe UI 21 | — |

字体单位：**全部 `GraphicsUnit.Pixel`**（`Dashboard.cs:1085-1101`），
理由见 `KeyMouseWidget.cs:2335-2343` 的注释——避免「几何乘 `_s`、文字又被 GDI+ 按 DPI 二次放大」的错位。

v4 规则：

- 字号阶梯收敛为 **6 档**，进 `ThemeTokens.Typography`：
  `Display 38 / H1 29 / H2 21 / Body 13 / Caption 11 / Mono 11`。
  v3 的 25/19/14/12 是同一档在不同场景的手调，合并后需逐页比对（golden 会抓住差异）。
- 字体族收敛为 **2 + 1**：`YaHei UI`（中英混排）、`Segoe UI`（纯数字）、`Cascadia Mono`（键名）。
- ⚠️ **`Cascadia Mono` 不是系统必装字体**（多数机器只有装了 Windows Terminal 才有）。
  v3 界面层**没有**字体回退（只有海报 `DailyReportPoster.cs:1012-1031` 做了回退）。
  v4 必须改成：`Cascadia Mono → Consolas → Courier New`，或直接嵌入字体子集。

---

## 2. 主题令牌（ThemeTokens）

### 2.1 来源

`Themes.cs:155-181` 的 `ArtTheme`：15 个颜色 + 1 个半径，13 套（数组索引 0–12）。

原本任务描述里的「12 套」是错的：`All` 数组实际 **13 套**（`Themes.cs:160-174`），
另有 1 套「用户自定义壁纸皮肤」由 `CustomThemeStore.ActiveTheme` 运行时生成、不进数组。

### 2.2 令牌源文件（v4 直接采用，作为唯一真源）

`src/Kms.Render/Tokens/themes.json`：

```jsonc
[
  { "id":0,  "name":"午夜蓝",            "radius":13,
    "background":"#0F141E","card":"#18202E","raised":"#1D2737","line":"#303F54",
    "text":"#E9EDF6","muted":"#A0ABBC","accent":"#729BFF","green":"#4BD2A8",
    "orange":"#F1BB6F","purple":"#B19DF8","red":"#FF6B81","cyan":"#4CC9F0",
    "sidebar":"#0C111A","onAccent":"#101622" },

  { "id":1,  "name":"奶油手账",          "radius":4,
    "background":"#F1EBDD","card":"#FFFCF5","raised":"#FFFCF5","line":"#CEC1AB",
    "text":"#382F27","muted":"#746452","accent":"#9C4A30","green":"#357258",
    "orange":"#93651B","purple":"#805C82","red":"#B73943","cyan":"#336E80",
    "sidebar":"#E8DECB","onAccent":"#FFFFFF" },

  { "id":2,  "name":"奥日 · 灵光森林",    "radius":13,
    "background":"#050D20","card":"#0B1D34","raised":"#102B43","line":"#2B6078",
    "text":"#F0FCFF","muted":"#9ABCCB","accent":"#54D7F0","green":"#65D6B4",
    "orange":"#F2C66D","purple":"#A783E8","red":"#D75B9C","cyan":"#6DEAFF",
    "sidebar":"#050B19","onAccent":"#07111D" },

  { "id":3,  "name":"霓虹夜航",          "radius":5,
    "background":"#130E25","card":"#201536","raised":"#2C1B49","line":"#604579",
    "text":"#F5ECFF","muted":"#C0AED7","accent":"#E68EFF","green":"#63E3BC",
    "orange":"#FFCB7A","purple":"#B49BFF","red":"#FF83B4","cyan":"#62DCEE",
    "sidebar":"#0D091C","onAccent":"#200E30" },

  { "id":4,  "name":"蓝图工坊",          "radius":0,
    "background":"#EAF0F7","card":"#FFFFFF","raised":"#FFFFFF","line":"#9BB2CD",
    "text":"#142C4D","muted":"#4F6581","accent":"#235DB4","green":"#237654",
    "orange":"#976016","purple":"#7655A7","red":"#B73353","cyan":"#176C8E",
    "sidebar":"#DDE7F3","onAccent":"#FFFFFF" },

  { "id":5,  "name":"暖暖 · 星愿织梦",    "radius":18,
    "background":"#F8F4FC","card":"#FFFCFF","raised":"#F4ECF8","line":"#DACBE3",
    "text":"#473B59","muted":"#796884","accent":"#9263AA","green":"#548F8D",
    "orange":"#B78945","purple":"#AE75A2","red":"#BF537B","cyan":"#578BAD",
    "sidebar":"#F0E8F6","onAccent":"#FFFFFF" },

  { "id":6,  "name":"士官长 · 战术终端",  "radius":6,
    "background":"#0C141C","card":"#14212A","raised":"#1C2C35","line":"#3C5360",
    "text":"#E5ECE8","muted":"#A9BABF","accent":"#ADC184","green":"#7EB99F",
    "orange":"#D5AF62","purple":"#A0AFBF","red":"#E27464","cyan":"#63B8D8",
    "sidebar":"#0A1118","onAccent":"#152119" },

  { "id":7,  "name":"生化危机 · 档案",    "radius":5,
    "background":"#111416","card":"#1B2326","raised":"#293236","line":"#536366",
    "text":"#EBEEE9","muted":"#B7C2C1","accent":"#DA7773","green":"#8AB9A1",
    "orange":"#D6AE72","purple":"#ACA6BA","red":"#E27474","cyan":"#7BAEBB",
    "sidebar":"#0B1012","onAccent":"#221113" },

  { "id":8,  "name":"我的世界 · 方块",    "radius":2,
    "background":"#101D18","card":"#1B2C23","raised":"#293E2D","line":"#617458",
    "text":"#EDF0DB","muted":"#B5C6A5","accent":"#9BCB70","green":"#75BDA0",
    "orange":"#DEB26B","purple":"#B6A2D6","red":"#DF8573","cyan":"#70C9CF",
    "sidebar":"#0B1711","onAccent":"#16220F" },

  { "id":9,  "name":"小丑牌 · 幻彩牌桌",  "radius":7,
    "background":"#101F23","card":"#193037","raised":"#25424A","line":"#507178",
    "text":"#F5E9CB","muted":"#ADC5C5","accent":"#F08075","green":"#87C3A2",
    "orange":"#E6B964","purple":"#BB99D6","red":"#ED6F68","cyan":"#6AD3DD",
    "sidebar":"#0B181C","onAccent":"#271316" },

  { "id":10, "name":"大侠立志传 · 碧血丹心","radius":5,
    "background":"#091318","card":"#122226","raised":"#1C3031","line":"#6B6048",
    "text":"#F2E7CA","muted":"#BDB49D","accent":"#B7453F","green":"#54A194",
    "orange":"#C8A55E","purple":"#8872A4","red":"#B74449","cyan":"#62B5B3",
    "sidebar":"#071014","onAccent":"#FFF1D2" },

  { "id":11, "name":"欧洲卡车 · 长途之路","radius":7,
    "background":"#07111E","card":"#0E1C2B","raised":"#14283B","line":"#31506A",
    "text":"#EDF4F8","muted":"#93A9B7","accent":"#E7A23B","green":"#58BCA5",
    "orange":"#F0A840","purple":"#8D86C9","red":"#E46E5C","cyan":"#3CCBE7",
    "sidebar":"#08131F","onAccent":"#16130D" },

  { "id":12, "name":"古剑奇谭 · 琴心剑魄","radius":7,
    "background":"#07121B","card":"#10232D","raised":"#17343D","line":"#557070",
    "text":"#F1E8D3","muted":"#A8B7B3","accent":"#49BFC3","green":"#68A98D",
    "orange":"#D5AA58","purple":"#8F78B6","red":"#B74E46","cyan":"#62E0DD",
    "sidebar":"#061018","onAccent":"#081416" }
]
```

> 上表 100% 来自 `Themes.cs:161-173`，包括「蓝图工坊 radius=0」「暖暖 radius=18」
> 「我的世界 radius=2」这些反直觉值——它们是刻意的视觉特征，**不要"顺手统一"**。

### 2.3 派生规则（必须一起移植，否则图例颜色会变）

| 派生物 | 公式 | 位置 |
|---|---|---|
| 三通道图例主色 | 左=`Cyan`、右=`Orange`、中=`Purple`、全部=`Accent` | `Themes.cs:114-121` |
| 通道渐变色阶 | `p<0.5`：`Mix(Card,Accent,0.20)` → `Accent`；`p≥0.5`：`Accent` → `Mix(Accent,Text,0.34)` | `Themes.cs:124-136` |
| 通道热力取色 | `log(1+count)/log(1+max)` 再 `pow(f, 0.85)` | `Themes.cs:139-144` |
| 颜色混合 | 逐通道线性插值（**不转 sRGB，直接整数通道**） | `Themes.cs:198-202` |
| 前景叠加 | `Composite(fg, alpha, bg)` | `Themes.cs:209` 起 |
| 对比度 | `RelativeLuminance` + `ContrastRatio` | `Themes.cs:209-246` 区段 |

⚠️ `Mix` 是**线性通道插值**而不是感知均匀空间插值。v4 若改用 OKLCH 会好看，但**图例会变**。
建议：v4 保留原算法直到 4.1，列进「不优化清单」。

### 2.4 贴图主题 vs 纯代码主题

| 类型 | 主题 | 实现方式 | v4 策略（已定 D6） |
|---|---|---|---|
| 纯代码 | 0 午夜蓝 / 1 奶油手账 / 3 霓虹夜航 / 4 蓝图工坊 | 12 行颜色 + `DrawLine` 细纹/网格 | 令牌化，直接重画 |
| 贴图 | 2 奥日 / 5 暖暖 / 6 士官长 / 7 生化 / 8 我的世界 / 9 小丑牌 / 10 大侠 / 11 欧卡 / 12 古剑 | 97 张位图（77 jpg + 20 png）+ `NikkiArt.DrawCached` 缩放缓存 + 96 MB 预算 | **先保位图**（省约 25 人日），4.1 转矢量 |

贴图装饰方面，即使贴图主题也有代码绘制：`CardOrnaments` / `Button` / `KeyCap` /
`DrawHud` / `Loading` / `RingAura`（`WuxiaTheme.cs:67-114`、`GujianTheme.cs:40-85`、
`OriTheme.cs:39-77`、`EuroTruckTheme.cs:15-17`）。这些要跟着主题一起重画。

---

## 3. 九个页面

通用结构（每页都是这个骨架）：

```
PageBase
├─ Header   标题 + 副标题 + 右上角操作/图例
└─ Body     卡片网格（v3 用 LayoutFrame 行切分）
```

页面清单与数据源：

| # | TabId | 页面 | v3 绘制入口 | 数据入口（搬 Kms.Core） | 行数 |
|---|---|---|---|---|---|
| 0 | Overview | 今日概览 | `Dashboard.PaintOverview` (2881) | `StatsStore` + `AppFlow` | ~740 |
| 1 | Trend | 趋势对比 | `PaintTrend` (3628) / `PaintTrendCompare` (3726) | `RangeAnalysis` + `TrendCutoff` | ~500 |
| 2 | Hours | 小时分布 | `PaintHours` (4144) + `HourlyTrend.PaintHourlyTrend` | `HourProfile` / `HourlyAxis` | ~440 |
| 3 | Keys | 按键分析 | `PaintKeys` (5910) + `PressReport` + `HoldTrendReport` + `PeripheralHealthView` | `Analysis.KeyRanking` + `PressDuration` | ~2,000 |
| 4 | Apps | 应用统计 | `AppPanel` | `AppActivity.Aggregate` + `AppFlow` | 1,093 |
| 5 | Insights | 洞察 / 心流 | `Insights.cs` 六个 `Paint*` + `InsightsFlow` | `FlowAnalyzer` + `ContextCost` | ~1,030 |
| 6 | Text | 字符内容 | `TextPanel`（8 张图） | `RimeLog` | 1,250 |
| 7 | Day | 日报 | `DailyReportPanel`（5 张卡） | `DailyReport.Build` | 541 |
| 8 | Heat | 点击/键盘热区 | `ClickHeatmap` + `KeyboardHeatmap` | `ClickHeat` + `KeyboardHeat.Layout` | 2,038 |

### 3.1 Overview 今日概览

| 元素 | 数据 | 备注 |
|---|---|---|
| 今日击键/点击/滚轮/移动 四大数 | `DayRecord.*` | 大数字用 `Segoe UI 38` |
| 主指标环形/条形对比 | `RangeStats.DailySeries` | — |
| 活跃/空闲时长 | `DayRecord.ActiveSeconds/IdleSeconds` | 秒为 double |
| APM 实时值 | `RateMemory` 60s 滚动 | — |
| 迷你趋势（7/30 日） | `RangeAnalysis` | — |

### 3.2 Trend 趋势对比

- 双曲线 + 同期裁剪：**今天未结束，只比已完成整小时**（`TrendCutoff.Compare`，`Hours = now.Hour`）。
- ⚠️ 只支持 metric 0/1（`TrendCutoff.cs:43-47`），v4 不要顺手扩展。
- 负零处理（`TrendCutoff.cs:89-91`）要保留。

### 3.3 Hours 小时分布

- 24 小时柱 + 纵轴刻度（`HourlyAxis.Compute/Label/Unit`，`HourlyTrend.cs:17-53`）。
- 作息矩阵（周×24）在 Insights 页，用 `HourProfile`。
- ⚠️ **量纲陷阱**：metric 2 是 `ActiveHours[h]/60.0`（**分钟**），与 metric 0/1 的「次数」
  共用同一个 `Average[7,24]` 与 `Max`（`HourProfile.cs:47-48`）。v4 拆成两个类型，避免混用。

### 3.4 Keys 按键分析（最复杂）

| 子块 | 来源 | 说明 |
|---|---|---|
| 按键排行 | `Analysis.KeyRanking` | 前 N 键 + 条形 |
| 按住时长直方图 | `PressDuration`，8 档 `<50/100/200/500/1000/2000/5000/≥5000 ms` | 严格 `<`，末档闭区间 |
| 按住时长趋势 | `HoldTrendReport` | 手工接今天（`IncludesRunningToday`） |
| 组合键 | `ShortcutStats.Ranking` | 1200ms 内同键重复 = 自动重复，不计数 |
| 键位热力图 | `KeyboardHeatmap` 6 种配列 | 几何是数据表，绘制薄 |
| 外设磨损 | `PeripheralWear` / `PeripheralMouseWear` | 卡片 + 编辑小面板 |

### 3.5 Apps 应用统计

- 列表 + APM + 时长占比 + 分类筛选 + 应用详情。
- ⚠️ **两套 APM**：`AppFlow` 用 `(Keys+Clicks)*60/Seconds`，
  `PersonalStats` 用 `Keys/ActiveSeconds`（`StatisticsReport.cs:31-35`）。不要合并。
- ⚠️ `AppObservedSeconds` 无条件累加，`AppUsage.ActiveSeconds` 只在「前后同进程 + ≤5s」累加，
  所以 **Σ应用时长 ≠ 观测时长**，「未归因」必须单列（`AppActivity.cs:646-654`）。

### 3.6 Insights 洞察 / 心流

| 子块 | 方法 | 说明 |
|---|---|---|
| 深度心流看板 | `PaintInsights` (101) | 5 分钟滑窗、60s 桶单次归属 |
| 日历 | `PaintCalendar` (112) + `PaintCalendarSummary` (270) | — |
| 会话时间轴 | `PaintSessionTimeline` (339) | 时光机（`TimelineWindow` + `TimelineDrag`） |
| 活跃时段 | `PaintPeriods` (418) / `PaintWeekActivity` (433) | — |
| 上下文切换成本 | `ContextCost.Analyze` | 滞后 = `recovery - distractor.StartMs` |

⚠️ 心流阈值自相矛盾：`FlowConfig.DeepMinMinutes = 10`（`FlowAnalyzer.cs:94`），
但文件头（12 行）与第 222 行注释都写 **15 分钟**。**已定取 10**（复刻以代码实际行为为准），并在 v4 里同步修正注释，见主方案 §13 D9。

### 3.7 Text 字符内容

8 张图：上屏字数 / 词长分布 / 高频字 / 应用分布 / 节奏 / 等。
⚠️ 隐私：文字页默认模糊、悬停才明文（`TextPanel.cs:809/895/919/1239`）；
Rime 日志不记窗口标题、不与窗口复合（`RimeLog.cs:17/134`）。v4 必须保留这个默认。

### 3.8 Day 日报

5 张卡：时段 / 应用 / 习惯 / 字符 / 提醒。左侧日期导航。
数据来自 `DailyReport.Build`（`DailyReport.cs:89-105`），纯数据、不画。

### 3.9 Heat 热区（两块）

**屏幕点击热区**（`ClickHeatmap`）：

- 32px 格子聚合，单日格子上限 `MaxCellsPerDay = 72`。
- 三通道（全部/左/右/中）+ 屏幕/九宫格视图。
- ⚠️ 云状热斑用 `PathGradientBrush.SetSigmaBellShape`（`ClickHeatmap.cs`），
  **Skia 无对应物**，v4 要用「径向渐变 + 多层高斯近似」重做，视觉会有细微差异。
- 关键几何：`heatLeft = Cw * 0.721f`（`ClickHeatmap.cs:899-926`）——硬编码比例，v4 重算。

**键盘热力图**（`KeyboardHeatmap`）：

- 6 种配列（`KeyboardHeat.Layout` 返回矩形表），键帽热色 + 悬停下钻 + CSV 导出。
- 键数靠 `GetRawInputDeviceList` 猜（`KeyboardHeatmap.cs:177-219`）。

---

## 4. 悬浮组（5 个窗 + 托盘）

| 窗 | 尺寸(DIP) | 窗口特性 | 内容 | 风险 |
|---|---|---|---|---|
| **KeyMouseWidget** | 320×210（4 模式可变） | 无边框 / `ShowInTaskbar=false` / `Opacity 0.96` / `WS_EX_TOOLWINDOW` / 可切 `WS_EX_TRANSPARENT` 穿透 | 今日 / 目标 / 累计 / 存储 四模式 + 悬停微件 | **高**：唯一必须支持点击穿透的窗 |
| **FocusPill** | 224×38 常态 / 248×116 展开 | `TopMost` / `Opacity 0.96` / `WS_EX_NOACTIVATE\|LAYERED\|TOOLWINDOW` / `ShowWithoutActivation` | 心流胶囊 + 悬停展开遥测 + 呼吸灯 | 高：不抢焦点 + 尺寸动画 |
| **FocusBreakToast** | 右下角滑出 | `TopMost` / `Opacity 0.98` / `NOACTIVATE` | 15 s 淡出 + 三按钮（延后/静音/关闭） | 中：有生命周期定时器 |
| **GameHud** | 330×92 | `TopMost` / `Opacity 0.8` / `NOACTIVATE\|LAYERED\|TRANSPARENT` / `WM_MOUSEACTIVATE→MA_NOACTIVATE` | 实时 APM / 今日击键 / 本段时长 + 5 分钟趋势条 | 中：编辑模式可拖动 |
| **托盘** | 16/32/48 图标 | `NotifyIcon` | 4 项菜单（含"窗口置顶"勾选） | 中：Avalonia `TrayIcon` 的右键菜单是原生的，自绘菜单风格会丢 |

**统一要求（M5 逐条验收）**：

1. 跨屏拖动不变形（v3 靠 `WM_DPICHANGED` 重算，`FocusPillForm.cs:134-143`、`KeyMouseWidget.cs:2412-2426`）。
2. `Opacity` 生效（Avalonia 的透明窗在 Win10/11 有差异，见风险 R2）。
3. 不抢焦点、不进 Alt+Tab。
4. 点击穿透只在 Widget 的特定区域启用。

---

## 5. 导出（海报 / 战报）

| 产物 | 逻辑尺寸 | 输出 | 说明 |
|---|---|---|---|
| 日报海报 | `LogicalWidth = 1440` | ×`OutputScale = 3` → 4320 px 宽 | `DailyReportPoster.cs:21-23` |
| 周报/月报 | 同上 | 同上 | `PeriodPoster.cs:20-22` |
| 战报长图 | 1080 宽 | 自适应高 | `BattleReport.cs` |

**v4 关键改进**：v3 的 `BattleReport` 要求 `MeasureHeight` 与 `Paint` 的 y 推进
**逐步手工同步**（注释 `BattleReport.cs:77-98` 自认易错）。Avalonia 用两遍布局
（`Measure` → `Arrange` → `Render`）天然消除这个风险，**这是重写的一个真实收益**，不是玄学。

导出流程：`RenderTargetBitmap(4320, h)` → `Save(PNG)`。字体回退链沿用
`PickFamily(YaHei UI → YaHei → Segoe UI → Arial)` + 拉丁衬线
`Georgia→Times→Cambria→Arial` + 中文衬线 `SimSun→宋体→NSimSun→YaHei`
（`DailyReportPoster.cs:1012-1040`）。

---

## 6. 图表原语清单（`Kms.Render` 要提供什么）

从 26 处 `OnPaint` 归纳，v4 只需这些原语，其余都是组合：

| 原语 | 替代的 v3 代码 | 备注 |
|---|---|---|
| `RoundRect` | `UiPrimitives.Round` + 232 处 `GraphicsPath` | 半径来自主题 |
| `Card` | 卡片底板 + 边框 | — |
| `LineChart` | `DrawLines` / `PaintTrend` | 支持同期裁剪标记 |
| `BarChart` | `FillRectangle` 柱 | 支持负值与基线 |
| `HeatGrid` | `ClickHeat` / `KeyboardHeat` | 对数归一 + 0.85 gamma |
| `Donut` / `Ring` | `DrawArc` 环图 | APM / 占比 |
| `Sparkline` | HUD 5 分钟趋势条 | — |
| `Legend` | 图例 + 通道色带 | 用 `ChannelScale` |
| `KeyCapGrid` | 键帽 + 热色 | 6 种配列 |
| `CalendarGrid` | `PaintCalendar` | 7×N |
| `TextBlock` | `DrawString` + `AppText`（222 处） | 带省略号 |
| `HitLayer` | `ChipLedger` | 绘制产出命中区，不依赖调用顺序 |

每个原语都要：**① 支持 DPI 无关绘制 ② 命中区与绘制同源 ③ 可选 golden 快照 ④ 数据变化时主动 `InvalidateVisual()`**。

> ⚠️ ④ 是被核实过的坑：Avalonia 会缓存 `Render` 的结果，**几何/画刷的内部内容变了不会自动重绘**
> （[Discussion #18627](https://github.com/AvaloniaUI/Avalonia/discussions/18627)）。
> v3 靠 `Invalidate()` 一把梭，v4 必须逐原语显式失效，否则会出现「数据变了图没变」。
> 参见 `02-api-mapping.md` §10。

---

## 7. golden 基线清单（M3 必须产出）

每格 = 一页 × 一档 DPI，共 **9 × 3 = 27** 张，加悬浮组 **5 × 3 = 15** 张，共 **42 张**。

| 维度 | 取值 |
|---|---|
| 页面 | Overview / Trend / Hours / Keys / Apps / Insights / Text / Day / Heat |
| 悬浮 | Widget(4 模式) / FocusPill(2 态) / FocusBreakToast / GameHud |
| DPI | 100% (96) / 150% (144) / 200% (192) |
| 主题 | 至少跑 0 午夜蓝 + 1 奶油手账 + 5 暖暖（浅色/大圆角）+ 12 古剑（贴图）4 套 |

比对策略：**SSIM ≥ 0.98 + 关键元素包围盒 ±2 DIP + 色板 ΔE ≤ 2**。
基线入库（`tests/Baselines/`），变更走 PR review。

> v3 现状：15 个渲染测试会写图到 `previews/`，但 `previews/` 与 `tests/*.png`
> 都在 `.gitignore`（第 28-29 行），**仓库里没有任何基线图**。所以 v3 的画质回归
> 目前零保护——这是 v4 必须先补的洞。
