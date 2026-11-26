# KeyMouseStats · 键鼠统计

> **v4 重写（.NET 10 + Avalonia 12.1）已发布 `4.0.0-alpha.1` 首版**，正式版是 3.4.3。
> 发布说明见 [`docs/releases/v4.0.0-alpha.1.md`](docs/releases/v4.0.0-alpha.1.md)。
> 方案与进度见 [`docs/v4-rewrite-plan.md`](docs/v4-rewrite-plan.md) 与
> [`docs/v4-plan/04-wbs.md`](docs/v4-plan/04-wbs.md)；
> 实时采集接线见 [`docs/v4-live-capture.md`](docs/v4-live-capture.md)；
> 页面视觉改版见 [`docs/v4-ui-art-pass.md`](docs/v4-ui-art-pass.md)；
> 真机验收清单见 [`docs/v4-acceptance.md`](docs/v4-acceptance.md)。
> 自检：`Kms.App.exe --selfcheck-ui`；帧基准：`Kms.App.exe --bench-ui`。
> V3 的 13 套主题/美术皮肤与自定义壁纸已迁移；侧栏 `+` 打开皮肤画廊。

<p align="center">
  <strong>本地优先的 Windows 键鼠使用统计：记录、可视化、日报，全程离线。</strong>
</p>

<p align="center">
  <a href="https://github.com/2814899313/KeyMouseStats/releases/latest"><img src="https://img.shields.io/github/v/release/2814899313/KeyMouseStats?style=flat-square&color=6f9df7" alt="Release"></a>
  <a href="https://github.com/2814899313/KeyMouseStats/actions/workflows/build.yml"><img src="https://img.shields.io/github/actions/workflow/status/2814899313/KeyMouseStats/build.yml?branch=main&style=flat-square&label=Windows%20build" alt="Windows build"></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-2f7dd1?style=flat-square" alt="Platform">
  <img src="https://img.shields.io/badge/data-local%20only-42a66c?style=flat-square" alt="Data local only">
</p>

<p align="center">
  <a href="https://github.com/2814899313/KeyMouseStats/releases/latest/download/KeyMouseStats.exe">下载完整版</a>
  · <a href="https://github.com/2814899313/KeyMouseStats/releases/latest/download/KeyMouseStats.Clean.exe">下载清爽版</a>
  · <a href="CHANGELOG.md">更新日志</a>
  · <a href="docs/">文档</a>
  · <a href="https://github.com/2814899313/KeyMouseStats/issues">问题反馈</a>
</p>

![碧血丹心主题 · 总览](docs/screenshots/wuxia-overview.png)

## 简介

KeyMouseStats 是一个单文件 Windows 桌面程序，用来自动记录键盘与鼠标的使用情况，并把它变成可读的统计：

- 悬浮小组件实时显示今日击键、点击、APM、目标进度与存储占用；
- 详细面板提供 9 个分析页，覆盖趋势、时段、按键、应用、鼠标动作、字符输入与日报；
- 统计数据只保存在本机 `%APPDATA%\键鼠统计`，不联网、不需要账号、不上传任何数据。

程序统计**按键次数、组合关系与鼠标动作**，不记录实际输入的文字。

## 功能概览

| 模块 | 内容 |
|---|---|
| 键盘 | 击键计数、逐键排行、组合键、全键热力图、按住时长分布；按今日 / 近 7 天 / 近 30 天 / 近 90 天筛选 |
| 鼠标 | 左右中侧键、滚轮、DPI 校准后的移动距离与方向、快速甩动与折返识别、屏幕点击热区（32 像素网格 / 平滑热力两种模式，支持多屏） |
| 时间 | 活跃时长、空闲时段、连续使用段（含开始原因）、时段分布、周 × 24 小时作息矩阵、年度热力日历 |
| 趋势 | 按天趋势与环比、近 2–14 天多日小时曲线、本周七日曲线、异常高值标注、未运行日期灰带、今日"进行中"标注 |
| 应用 | 前台应用归因、原生图标、自定义分类、应用 × APM 交叉分析、使用时长占比、应用 × 时段 / 操作 / 会话 |
| 洞察 | 会话时间轴、操作密度、快捷键节省量估算、历史基准与分位数、区间回顾与对比（可导出 CSV / PNG） |
| 日报 | 每天首次打开面板自动生成昨日简报（Markdown），包含核心数字、时段、应用 Top 5、字符构成与提醒 |
| 数据 | 按年 / 月永久归档，每日备份轮转，导入、导出与多机合并，逐日数据自检 |
| 字符 | 读取本机 Rime 上屏日志，显示字数、高频字词、输入效率与应用分布（可选功能，见下文） |

### 数据深入

每个图表都可以悬停查看明细，点击数字卡或图表可打开详情视图；筛选条件（时间范围、指标）在页内切换，并会带到导出结果里。

![多日小时曲线](docs/screenshots/hourly-comparison.png)

## 系统要求

- Windows 10 / 11（x64 或 x86）
- 无需安装 .NET 运行时：程序基于系统自带的 .NET Framework 4.x 构建
- 未做商业代码签名，首次运行可能出现 Windows SmartScreen 提示

## 下载与安装

从 [Releases](https://github.com/2814899313/KeyMouseStats/releases/latest) 下载，二选一：

| 版本 | 体积 | 说明 |
|---|---|---|
| `KeyMouseStats.exe` | 约 45 MB | 完整版，包含 13 套主题的全部美术资源 |
| `KeyMouseStats.Clean.exe` | 约 0.7 MB | 清爽版，统计功能相同，不含美术资源，体积最小 |

程序是单文件绿色软件：放到任意可写目录运行即可，无需安装，也不会写入注册表以外的系统位置（开机自启为可选项，默认关闭）。

## 使用

| 操作 | 结果 |
|---|---|
| 单击悬浮小组件 | 切换 今日 / 目标 / 累计 / 存储 |
| 双击悬浮小组件 | 打开详细统计面板 |
| 拖动悬浮小组件 | 调整桌面位置 |
| 右键悬浮小组件 | 打开托盘菜单：详细面板、DPI 校准、置顶、鼠标穿透、游戏 HUD、开机自启、外观 / 动效、重置统计、退出 |
| 面板左下角 | 切换 9 个分析页；页内筛选会带到详情视图与导出 |

数据保存在 `%APPDATA%\键鼠统计`：`stats.txt` 为当月数据，`2026\2026-xx.txt` 为历史月档，`backup\` 为每日备份，`日报\` 为 Markdown 日报。想清空全部数据，删除该目录即可。

## 主题

内置 13 套主题，其中 9 套带完整页面美术（奥日 · 灵光森林、欧洲卡车 · 长途之路、大侠立志传 · 碧血丹心、古剑奇谭 · 琴心剑魄、暖暖 · 星愿织梦、士官长 · 战术终端、生化危机 · 档案、我的世界 · 方块、小丑牌 · 幻彩牌桌），另有午夜蓝、奶油手账、霓虹夜航、蓝图工坊 4 套轻量配色。

美术资源随 EXE 内嵌，后台加载、有界缓存，切换主题不重新下载任何文件。新增主题的资源约定见 [docs/theme-resources.md](docs/theme-resources.md)。

| 古剑奇谭 | 我的世界 |
|---|---|
| ![古剑奇谭](assets/gujian-overview.jpg) | ![我的世界](assets/minecraft-overview.jpg) |

## 字符内容统计（可选）

「字符内容」页不是通用功能：只有同时使用 **小狼毫（Weasel）+ 雾凇拼音 + commit_log 插件** 时才有数据，其他输入法不支持。不满足条件时，该页显示"未找到 commit_log.csv"，其余键鼠统计不受影响。

程序只**读取**输入法自己写出的 `%APPDATA%\Rime\commit_log.csv`：不挂钩子、不读剪贴板、不写入该文件；字符数据不进入 `stats.txt`，也不参与导入合并与键鼠导出。启用条件与自检方法见 [docs/text-content.md](docs/text-content.md)。

## 数据与隐私

- 所有统计只保存在本机，不联网、不需要账户、不依赖云服务。
- 不记录输入的文字内容；应用归因只记录前台进程、窗口标题与累计量。
- 点击热区只在本地按 32 像素网格聚合点击次数：不截屏、不读取屏幕内容，位置明细仅保留 30 天。
- 密码输入框与剪贴板不在统计范围内。
- 历史数据默认永久保留，可按年月归档；可随时导出 CSV 或在设置中删除。

更详细的说明见 [SECURITY.md](SECURITY.md)；统计口径见 [docs/statistics.md](docs/statistics.md)。

## 从源码构建

要求：Windows 10/11，系统自带的 .NET Framework 4.x C# 编译器（`csc.exe`），无需安装其他工具。

```bat
build.bat            :: 构建完整版 KeyMouseStats.exe
build-clean.bat      :: 构建清爽版 KeyMouseStats.Clean.exe
test.bat             :: 运行全部逻辑测试并刷新预览截图（需要交互桌面）
test.bat --logic-only :: 只跑逻辑测试，供 CI 使用
```

版本号只有一个来源：`Version.cs` 中的 `BuildInfo.Version` / `BuildInfo.FileVersion`；每次发布同时更新 `CHANGELOG.md` 与 `docs/releases/v<版本>.md`。标签与程序集版本不一致时，Release 工作流会拒绝发布。

## 项目结构

```
KeyMouseStats/
├─ *.cs                 产品源码（单工程，csc 直接编译）
├─ assets/              主题与图标资源（构建时嵌入 EXE）
├─ tests/               逻辑测试与预览渲染脚本
├─ docs/
│  ├─ releases/         每个版本的发布说明
│  ├─ screenshots/      README 与文档用截图
│  ├─ research/         设计研究
│  ├─ planning/         开发计划与待办
│  ├─ examples/         输出示例
│  ├─ statistics.md     统计口径
│  ├─ text-content.md   字符内容页说明
│  └─ theme-resources.md 主题资源约定
├─ tools/               构建辅助脚本
├─ build.bat / build-clean.bat / test.bat
└─ .github/workflows/   构建与发布 CI
```

## 文档

- [更新日志](CHANGELOG.md) · [各版本发布说明](docs/releases/)
- [文档索引](docs/README.md)
- [统计口径](docs/statistics.md)
- [字符内容统计](docs/text-content.md)
- [主题资源扩展](docs/theme-resources.md)
- [参与开发](CONTRIBUTING.md) · [安全与隐私](SECURITY.md)

## 参与

欢迎通过 [Issues](https://github.com/2814899313/KeyMouseStats/issues) 反馈问题或提出建议。提交代码前请阅读 [CONTRIBUTING.md](CONTRIBUTING.md)，并在本地跑通 `build.bat` 与 `test.bat --logic-only`。

提交内容中请勿包含个人统计数据、编译产物或批量测试截图。
