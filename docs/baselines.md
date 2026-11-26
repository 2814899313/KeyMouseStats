# v3 → v4 基线快照

> 用途：v4 的所有性能/体积/数据对比都以本文件为准。**任何 v4 改造前后都要能复现这些数字。**
> 采集时间：2026-11-22（v3.4.1）
> 采集机器：Windows 10.0.26200（Win11）· AMD64 · .NET Framework 4.8.09221
>
> ⚠️ **版本口径**：以 `Version.cs` 的 `BuildInfo.Version` 为唯一真源（当前 `3.4.1`）。
> 仓库里曾存在一个 3.2.2 的旧构建（16:44，51.4 MB），19:15 已重新构建为 3.4.1（49.0 MB）。
> **性能数字取自 3.4.1 源码**（`test.bat` 直接编译产品源码，不读 exe），故不受旧构建影响。

---

## 1. 构建与工具链现状

| 项 | 值 |
|---|---|
| 编译器 | `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`（C# 5，无 Roslyn） |
| 目标框架 | .NET Framework 4.x |
| 已安装 .NET SDK | **仅 8.0.425**（⚠️ .NET 8 LTS 已于 2026-11-10 结束支持；需装 .NET 10 SDK） |
| 已安装运行时 | .NET Framework 4.8.09221 |
| NuGet 依赖 | 0 |
| 原生依赖 | `e_sqlite3.dll` 3.41.2（x64 1,691,648 B / x86 1,308,672 B） |

## 2. 代码规模

| 项 | 值 |
|---|---|
| 产品源码 | 95 个 `.cs` / **45,843 行** |
| 类型数 | 323 |
| 测试源码 | 104 个 `.cs` / 15,371 行 |
| 测试清单 | 82 逻辑 + 15 渲染 + 1 基准（`test.bat:65/67/68`） |
| 最大单文件 | `Dashboard.cs` 6,055 行 / 349 KB |
| 第二大 | `KeyMouseWidget.cs` 3,880 行 / 212 KB |

## 3. 性能基线（实测）

命令：`test.bat ThemePerf`（对 1072×724 客户区连续 30 次 `OnPaint` 取均值）

```
Keyboard 1x:   11.05 ms/frame     (1072 × 724)
Keyboard 1.5x: 13.74 ms/frame     (1608 × 1086)
```

推导：

| 指标 | 值 |
|---|---|
| 像素填充率 | ≈ 70 Mpx/s（1072×724 = 776,128 px ÷ 11.05 ms） |
| 理论最高帧率（单帧不含合成） | ≈ 90 fps，实际因定时器与合成远低于此 |
| Dashboard 悬停帧间隔 | 实测被夹在 **8–30 ms**（`Motion.FrameInterval`），即 33–125 fps 抖动 |
| Dashboard 主刷新 | 1000 ms 整窗 `Invalidate`（`Dashboard.cs:529-544`） |
| Widget 主刷新 | 250 ms 整窗 `Invalidate`（`KeyMouseWidget.cs:2299-2301`） |

> 结论：**平台已达上限。** `Dashboard` 已实现场景缓存位图（`Dashboard.cs:423-425`）、
> `SetClip` 脏区 + 整窗 Blit（`Dashboard.cs:2148-2165`）、双缓冲 + `AllPaintingInWmPaint`
> （`Dashboard.cs:523-524`）。软件优化已做尽，剩余成本是 GDI+ 本身。

## 4. 体积基线

| 产物 | 大小 | 说明 |
|---|---|---|
| `KeyMouseStats.exe` | 49.0 MB | 89 条内嵌资源（77 jpg + 20 png + ico）+ 2 份 e_sqlite3（3.4.1 构建，2026-11-22 19:15） |
| `KeyMouseStats.Clean.exe` | 1.10 MB | 零内嵌资源，CI 断言 ≤2 MiB |
| 内嵌资源条数 | 89 托管 + 2 原生 | `build.bat:25-27` + `theme-extra-resources.rsp` |

## 5. 数据基线（本机真实数据，用于 v4 差分对拍）

| 文件 | 大小 | 用途 |
|---|---|---|
| `%LOCALAPPDATA%\KeyMouseStats\kmstats.db` | 7.7 MB | v3 主库（13 表，`schema_rev=1`） |
| `kmstats.db-wal` | 4.0 MB | WAL 未合并，真实运行态 |
| `kmstats.db.bak-3995-fix` | 7.5 MB | **3995 年时间基准缺陷的修复前备份**（回归关键输入） |
| `%APPDATA%\键鼠统计\stats.txt` | 1.6 MB | v13 文本后端现行档 |
| `2026-09.txt` / `2026-10.txt` | 2.3 MB / 340 KB | 月归档 |
| `RECOVERED-stats-20261110-234312.txt` | 3.4 MB | 崩溃恢复产物（坏档样本） |
| `stats-synthetic-20261111-095130.txt` | 42 KB | 合成示例档 |
| `migration.log` | 0.2 KB | 迁移记录 |

> ⚠️ **隐私**：上述文件含真实窗口标题与进程路径。**禁止直接提交进 Git。**
> 本地样本放 `_fixtures/`（已 gitignore）；入库版本必须先用 `PrivacyMask.MaskTitle/MaskPath`
> 等价逻辑清洗，见 `_fixtures/README.md`。

### 5.1 实际解析结果（已核对，2026-11-22）

对 `_fixtures/v3/v13-text-current.txt` 逐行解析确认：

| 项 | 值 |
|---|---|
| 文件头 | `# 键鼠统计数据文件 v13 / 正式版 3.4.1` |
| `format` | `13` |
| 总行数 / 大小 | 9,699 行 / 1,650,585 字节 |
| 记录天数 | **22 天**（`date=` 出现 22 次） |
| 键值分布 Top | `app`(4,281)、`session_app_v1`(1,974)、`combo`(536)、`app_hours_v1`(532)、`session`(382)、`idle_period`(307)、`app_keys_v1`(245) |
| 其他关键字段 | `mouse_dpi=216.726075755037`、`art_theme=6`、`keyboard_layout=1`、`keep_days=0`、`backup_keep=7`、`idle_threshold=300` |

⚠️ **确认了清洗要点**：`app_rule=` / `hardware_v1=` / `keyboard_v1=` 的值是 **base64**
（例如 `5pys5L2T` = `本体`），里面含真实进程路径与窗口标题；解码后即明文，必须清洗。
`stats.txt` 里的标题不是明文、而是 base64 —— 这一点代码注释里没写明，是实物验证出来的。

## 6. v4 验收目标（对应 `v4-rewrite-plan.md` §7.1）

| 指标 | v3 | v4 目标 |
|---|---|---|
| 静态页重绘 | 11.05 ms | ≤ 1.5 ms |
| 静态页重绘 @1.5x | 13.74 ms | ≤ 2.5 ms |
| 动画帧 P95 | 8–30 ms | ≤ 10 ms |
| 钩子回调 P99 | 未量化 | < 200 µs、零分配 |
| 启动到托盘 | 未量化 | ≤ 1.5 s |
| 内存峰值 | 未量化 | ≤ 250 MB |
| 空闲 CPU | 未量化 | ≤ 0.1% |
| 安装包 | 49.0 MB | ≤ 120 MB |

**待补测**（v3 未提供，M1 前补齐）：启动到托盘耗时、空闲/活跃 CPU 占用、内存峰值、钩子回调耗时。

## 7. 复现方法

```bat
:: 性能基线
test.bat ThemePerf

:: 全量逻辑测试（CI 同款）
test.bat --logic-only

:: 渲染冒烟（会写 previews/）
test.bat

:: 构建
build.bat
build-clean.bat
```

---

## 8. 参考实现自检（2026-11-22，3.4.1）

**结论：82/82 全绿。v3 可以作为 L1 差分对拍的标准答案。**

| 轮次 | 环境 | 结果 |
|---|---|---|
| 第 1 次 | 受限沙箱 | 77/82，5 项失败（`PageSkinTests`、`IconPackTests`、`CustomThemeTests`、`MonthlyStorageTests`、`Report110Tests`） |
| 第 2 次 | 本机非沙箱 | **82/82 通过，退出码 0，耗时 109 s** |

5 项失败全部归类为**沙箱环境限制**，不是产品缺陷：

- `PageSkinTests` / `IconPackTests` / `CustomThemeTests`：测试未重定向数据根，会写真实 `%APPDATA%\键鼠统计`，沙箱只允许写工作区。
- `MonthlyStorageTests` / `Report110Tests`：`stats.txt` 归档与 `Image.Save` 落到受限路径。

因此 v4 不需要为「复刻已知缺陷」做任何特殊处理。若后续再次出现失败，先按「是否写真实数据根 / 是否依赖交互桌面」排查，再判定为真缺陷。
