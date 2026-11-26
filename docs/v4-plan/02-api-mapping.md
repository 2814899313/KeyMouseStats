# 02 · API 映射表（GDI+ / WinForms → Avalonia 12.1）

> 用途：M3/M4 施工时逐行对照，避免「顺手换了个不一样的东西」导致画质漂移。
> 标 ⚠️ 的是**没有等价物**或**语义有细微差别**的，必须按备注处理。
> 标 🔬 的是需要用 spike 实测确认的（Avalonia 12 具体 API 名以实测为准）。

---

## 1. 绘制入口

| v3 | 出现量 | v4 | 注意 |
|---|---|---|---|
| `protected override void OnPaint(PaintEventArgs e)` | 26 | `public override void Render(DrawingContext ctx)` | Avalonia 的 `Control.Render` 每次重绘调用；不要在里面做布局计算 |
| `e.Graphics` | 71 | `ctx`（`DrawingContext`） | `DrawingContext` 是**记录式**的，全部调用会先入指令表再交给 Skia |
| `Invalidate()` | 多 | `InvalidateVisual()` | — |
| `Invalidate(rect)` | 多 | **没有 `InvalidateVisual(Rect)` 重载** ⚠️ | 已核实：Avalonia 只有无参 `InvalidateVisual()`。局部重绘靠**图层**实现，不靠接口。若确有需要：自绘时 `PushClip(rect)` + 失效整块 |
| `DoubleBuffered = true` / `SetStyle(OptimizedDoubleBuffer…​)` | 38 | **删除** | Avalonia 合成器天生双缓冲，不需要也不会生效 |
| `Application.DoEvents()` / 手动 `Refresh()` | 少量 | **删除** | 用 `Dispatcher.UIThread.Post` |
| `_sceneCache`（32bppPArgb 场景位图）+ `BlitScene` | Dashboard 专用 | `RenderTargetBitmap` 缓存层 + `ctx.DrawImage` | v3 手工做的这件事，v4 交给**图层（Visual 分层）**，不再手工 blit |

## 2. 形状与画刷

| v3 | 出现量 | v4 | 注意 |
|---|---|---|---|
| `new SolidBrush(color)` | ~600 | `new SolidColorBrush(color)` / `ImmutableSolidColorBrush` | **常量画刷提到 static 字段**，这是零分配的关键 |
| `new Pen(color, w)` | ~200 | `new Pen(brush, w)` / `ImmutablePen` | 同上 |
| `LinearGradientBrush` + `ColorBlend` | 多 | `LinearGradientBrush` + `GradientStops` | 🔬 `ColorBlend` 的插值位置语义要对齐（0..1 相对位置） |
| `PathGradientBrush` + `SetSigmaBellShape` | `ClickHeatmap` 云状热斑 | **无等价物** ⚠️ | 用 `RadialGradientBrush` + 多层高斯近似重做；视觉会有差异，需 golden 特批 |
| `HatchBrush` | 少量 | **无等价物** ⚠️ | 用平铺 `DrawingBrush`/`TileBrush` 或直接画 |
| `GraphicsPath` / `FillPath` / `DrawPath` | **232** | `StreamGeometry` + `ctx.DrawGeometry(brush, pen, geo)` | 几何**缓存为 static**；`StreamGeometry` 是冻结的，画一次可复用 |
| `GraphicsPath.AddArc`（圆角） | 多 | `StreamGeometryContext.ArcTo` 或直接用 `RoundedRect` + `ctx.DrawRectangle(brush, pen, roundedRect)` | 首选后者 |
| `FillRectangle` / `DrawRectangle` | 多 | `ctx.FillRectangle(brush, rect)` / `ctx.DrawRectangle(pen, rect)` | — |
| `FillEllipse` / `DrawEllipse` | 多 | `ctx.DrawEllipse(brush, pen, center, rx, ry)` | 参数是**中心点+半径**，不是左上角+宽高 —— 换算最容易错 |
| `DrawLine` | 52+ | `ctx.DrawLine(pen, p1, p2)` | — |
| `DrawLines` | 多 | `StreamGeometry` 或逐段 `DrawLine` | 折线点多时用几何 |
| `DrawArc` | 多（环图） | `StreamGeometry` + `ArcTo` | — |
| `DrawBezier` | Ori/Gujian | `StreamGeometry` + `CubicBezierTo` | — |
| `FillPolygon` | 多 | `StreamGeometry` + `ctx.DrawGeometry` | — |
| `SmoothingMode = AntiAlias` | 多 | **删除** | Skia 默认抗锯齿 |
| `CompositingMode / CompositingQuality` | 少量 | `ctx.PushOpacity` / `RenderOptions` | — |

## 3. 文本（最容易出观感差异的部分）

| v3 | 出现量 | v4 | 注意 |
|---|---|---|---|
| `g.DrawString(text, font, brush, rect, sf)` | **222** | `FormattedText` + `ctx.DrawText(ft, origin)` | Avalonia 的 `DrawText` 需要**左上角起点**，不是矩形居中；居中要自己算 |
| `g.MeasureString(text, font)` | 60+ | `FormattedText.Width` / `Height` | 度量结果与 GDI+ **不一致**（字距/单位不同），所有依赖测量的布局要重算 |
| `StringFormat { Trimming = EllipsisCharacter, NoWrap }` | 多 | `FormattedText.MaxTextWidth` + `TextTrimming.CharacterEllipsis` | — |
| `TextFit.Ellipsis/VisibleChars/LineHeight`（逐字 `MeasureString`） | `LayoutFlow.cs:124-168` | **删除，改用 `TextTrimming`** | 这是 v3 的性能热点之一，删掉即净赚 |
| `DailyReportPoster.Wrap`（手拆行） | 海报 | `FormattedText.MaxTextWidth` 自动换行 | 断行算法不同 → 海报版式必然有差异（已在计划中接受） |
| `new Font(family, size, style, GraphicsUnit.Pixel)` | **131** | `new Typeface(family, style, weight)` + `FormattedText { FontSize = size }` | **不再需要单位参数**；DIP 即逻辑像素 |
| `FontFamily.Families` 遍历做回退 | 海报 | `FontManager.Current.SystemFonts` 判断存在性 | 回退链语义保留 |
| `TextRenderingHint.ClearTypeGridFit` | 3 | `TextOptions.SetTextRenderingMode(visual, TextRenderingMode.SubpixelAntialias)` ✅ | 已核实：`TextOptions.TextRenderingMode` 默认 `Auto`，`SubpixelAntialias` 即 ClearType 式次像素。⚠️ 只在**不透明背景**有效；透明悬浮窗上会退回灰度 AA，笔画略轻 |
| `TextRenderer.DrawText`（GDI，非 GDI+） | 8+ | `ctx.DrawText` | ⚠️ 这是**另一个渲染引擎**（GDI 而非 GDI+），换到 Skia 后 ListView 表头的字会变 |
| `Graphics.DrawImage(icon, rect)` | 多 | `ctx.DrawImage(bitmap, destRect)` | — |
| `DrawImageUnscaled` | 多 | `ctx.DrawImage(bitmap, new Rect(x,y,w,h))` | 传原始尺寸即等价 |

**字体族结论**：`Microsoft YaHei UI` / `Segoe UI` / `Cascadia Mono` 三个家族保留，
但 `Cascadia Mono` 需加回退链（v3 界面层没做回退，见 `01-ui-spec.md` §1.4）。

## 4. 图像

| v3 | v4 | 注意 |
|---|---|---|
| `new Bitmap(w, h, PixelFormat.Format32bppPArgb)` | `new RenderTargetBitmap(new PixelSize(w,h))` | 离屏渲染 |
| `Graphics.FromImage(bmp)` | `using var ctx = rtb.CreateDrawingContext()` | — |
| `Image.FromStream(manifestResourceStream)` | `new Bitmap(stream)` / `AssetLoader.Open(uri)` | 资源改为 `AvaloniaResource` |
| `Bitmap.Save(path, ImageFormat.Png)` | `rtb.Save(path)` | — |
| `ImageAttributes + ColorMatrix（透明度）` | `ctx.PushOpacity(a)` 或 `ImageBrush { Opacity }` | 不要重造 ColorMatrix |
| `InterpolationMode.HighQualityBicubic` | `RenderOptions.SetBitmapInterpolationMode(visual, BitmapInterpolationMode.HighQuality)` | — |
| `LockBits/SetPixel`（热力图） | `WriteableBitmap` + `ILockedFramebuffer` | ⚠️ 内存布局与 stride 需自己管 |
| 96 MB 贴图预算 + 代数递增释放 | 同思路，换成 `Bitmap` 引用计数 + `Dispose` | `NikkiTheme.cs:36/43-54` 的机制必须保留 |

## 5. 几何与变换

| v3 | v4 | 注意 |
|---|---|---|
| `g.ScaleTransform(_s, _s)` | `ctx.PushTransform(Matrix.CreateScale(s, s))` | v4 **只有海报导出**会用；界面层不缩放（见 `01-ui-spec.md` §1.3） |
| `g.TranslateTransform` | `ctx.PushTransform(Matrix.CreateTranslation(dx,dy))` | — |
| `g.Save()` / `g.Restore()` | `using (ctx.PushClip/PushTransform/PushOpacity(…))` | 用 `using` 保证还原 |
| `g.SetClip(rect)` | `using (ctx.PushClip(rect))` | — |
| `Matrix.TransformPoints(pts)` | `matrix.Transform(point)` / `Point.Transform` | — |
| `GraphicsPath.IsVisible(pt)` | `geometry.FillContains(pt)` / `StrokeContains` | 命中测试用（`ChipLedger` 迁移） |
| `Region`（圆角窗体） | **无公开等价物** ⚠️ | 两条路：①`TransparencyLevelHint = Transparent` + 自绘圆角边框 ②P/Invoke `SetWindowRgn`。需 spike 定 |
| `RectangleF` / `PointF` / `SizeF` | `Rect` / `Point` / `Size`（double） | 精度从 float → double，比较时注意 |

## 6. 窗口

| v3 | v4 | 注意 |
|---|---|---|
| `Form` | `Window` | — |
| `FormBorderStyle.None` | `SystemDecorations = SystemDecorations.None` | — |
| `TopMost = true` | `Topmost = true` | — |
| `ShowInTaskbar = false` | `ShowInTaskbar = false` | — |
| `Opacity = 0.96` | `Opacity = 0.96` | 🔬 需要 `TransparencyLevelHint` 才能在半透明下正确合成 |
| `TransparencyKey` / `WS_EX_LAYERED` | `TransparencyLevelHint = [WindowTransparencyLevel.Transparent]` | ⚠️ Windows 上逐像素 alpha 的行为与 WinForms 不同，**这是 R2 风险的核心** |
| `WS_EX_TRANSPARENT`（点击穿透） | **仍需 P/Invoke `SetWindowLong(GWL_EXSTYLE)`** | Avalonia 无公开 API |
| `WS_EX_NOACTIVATE` + `ShowWithoutActivation` | `ShowActivated = false` ✅ + P/Invoke 兜底 | 已核实属性存在，但语义是「**首次显示时**是否激活」，不等于「永不激活」。点击是否夺焦点仍需 spike，必要时加 `WS_EX_NOACTIVATE` |
| `WM_DPICHANGED` 手工重算 | 自动（PerMonitorV2） | ⚠️ 但**自绘内容里的位图缓存要按新 `RenderScaling` 失效**，否则拖动跨屏会糊 |
| `g.DpiX / 96` | `TopLevel.GetTopLevel(ctrl)?.RenderScaling` | 只在必要处读，不要缓存全局值 |
| `Screen.AllScreens` / `WorkingArea` | `Screens.All` + `WorkingArea`（PixelRect，需除以 scaling） | — |
| `SetProcessDPIAware()` | **删除** | — |
| `AutoScaleMode`（Dpi/None） | **删除** | 统一由 Avalonia 缩放 |
| `Application.OpenForms` 遍历主题刷新 | 自维护窗口注册表 + `ResourceDictionary` 动态绑定 | v3 的 `RefreshThemeSurfaces()`（`Themes.cs:316-328`）要重做 |
| `NotifyIcon` | `TrayIcon` + `NativeMenu` | ⚠️ `ShowBalloonTip` 没有等价物（v3 用它报"钩子安装失败"）→ 用自己的 toast 窗或 WinRT |
| `Clipboard.SetImage` | `TopLevel.Clipboard.SetBitmapAsync` | 异步，需 await |
| `SaveFileDialog` / `OpenFileDialog` | `TopLevel.StorageProvider.SaveFilePickerAsync` | 异步 API |
| `MessageBox.Show` | **无内置** ⚠️ | 自建对话框；v3 有 `ThemedDialog` 可复用设计 |

## 7. 输入与控件

| v3 | v4 | 注意 |
|---|---|---|
| `OnMouseMove` / `OnMouseDown` / `OnMouseUp` | `OnPointerMoved` / `OnPointerPressed` / `OnPointerReleased` | 25 处手写命中测试要改 |
| `MouseButtons` | `PointerPointProperties.IsLeftButtonPressed` 等 | — |
| `MouseEventArgs.Delta`（滚轮） | `PointerWheelEventArgs.Delta` | ⚠️ 量纲不同：Avalonia 是「行」不是 120 的倍数 |
| `KeyEventArgs.KeyCode` | `KeyEventArgs.Key`（`Key` 枚举） | 键名映射表要重做（`Cascadia Mono` 显示的键名） |
| `Panel` / `UserControl` + 自绘 | `Control` + `Render` | — |
| `ListView` / `ComboBox` / `DateTimePicker` | `ListBox` / `ComboBox` / `CalendarDatePicker` | ⚠️ 主题化方式完全不同（v3 是 owner-draw，v4 是 `Styles`） |
| `ToolTip` | `ToolTip` | — |
| `ToolStripMenuItem`（托盘菜单） | `NativeMenuItem` | 自绘菜单风格会丢，需决定是否接受 |
| `ImageList` | 无 | 用 `Bitmap` 直接绘制 |
| `Timer`（WinForms） | `DispatcherTimer` | 注意：UI 线程调度不同 |
| `Thread` / `Task.Run` / `ThreadPool.QueueUserWorkItem` | 同（BCL） | 原样可用 |
| `Control.BeginInvoke` | `Dispatcher.UIThread.Post` | — |
| `Cursor` | `Cursor`（`StandardCursorType`） | — |
| `SystemParametersInfo(SPI_GETCLIENTAREAANIMATION)` | 仍需 P/Invoke | 无障碍「减少动画」偏好 |

## 8. 必须保留的 P/Invoke（换栈后依然存在）

| 能力 | 用到的 API | 为什么不能去掉 |
|---|---|---|
| 键盘/鼠标钩子 | `SetWindowsHookEx(WH_KEYBOARD_LL/WH_MOUSE_LL)`、`CallNextHookEx`、`UnhookWindowsHookEx` | DeftSharp 也走这套，看门狗逻辑只有自己有 |
| Raw Input | `RegisterRawInputDevices`、`GetRawInputData`、`GetRawInputDeviceList`、`GetRawInputDeviceInfo` | **设备身份唯一来源** |
| 前台窗口 | `GetForegroundWindow`、`GetWindowText`、`GetWindowThreadProcessId` | 应用归因 |
| 进程路径 | `OpenProcess`、`QueryFullProcessImageName`、`CloseHandle` | 应用归因 |
| 空闲判定 | `GetLastInputInfo` | 活跃/空闲积分 |
| 会话通知 | `WTSRegisterSessionNotification` | 锁屏/切换用户暂停统计 |
| 免打扰 | `SHQueryUserNotificationState` | 全屏/勿扰检测 |
| 显示器几何 | `MonitorFromWindow`、`GetMonitorInfo` | 全屏检测 |
| 窗口扩展样式 | `SetWindowLong(GWL_EXSTYLE)` | 点击穿透 / 不激活 |
| 窗口圆角 | `SetWindowRgn`（若走方案②） | 圆角窗体 |
| 原生 DLL 释放 | `LoadLibraryW` / `FreeLibrary` | ⚠️ **可删**：改用 NuGet 的 `SQLitePCLRaw` 后不再需要 |

> 结论：**77 条 P/Invoke 声明里，约 12 类（≈60 条）要原样保留。**
> 换 Avalonia 不会减少 Windows 平台工作量，只会减少绘制工作量。这一点要在排期里认清。

## 9. 迁移检查清单（每个页面收工时逐条打勾）

- [ ] 所有 `new SolidBrush/Pen/Font` 已提到 static 或令牌
- [ ] 没有任何 `* _s` / `* _uiScale` 残留
- [ ] 没有逐字 `MeasureString`（改 `TextTrimming`）
- [ ] `Render` 里没有 IO / 数据库 / 分配大对象
- [ ] 居中文本自己算了原点（不是假设 `DrawText` 居中）
- [ ] `DrawEllipse` 的「中心+半径」换算正确
- [ ] 命中区与绘制同源（`HitLayer`）
- [ ] **几何/画刷的内部内容变化后手工 `InvalidateVisual()`**（见 §10）
- [ ] 3 档 DPI 的 golden 通过
- [ ] 主题切换后无残影（v3 踩过的坑，`Dashboard.cs:552-558`）

---

## 10. 已核实的 API 事实（2026-11-22 查证）

下面几条推翻了先前草案里的写法，**实现时以本节为准**：

| # | 结论 | 影响 | 来源 |
|---|---|---|---|
| 1 | 只有无参 `InvalidateVisual()`，**没有** `InvalidateVisual(Rect)` | 局部重绘必须靠图层；不要照抄 v3 的「脏矩形 + Blit」 | [Custom rendering](https://docs.avaloniaui.net/docs/graphics-animation/custom-rendering) |
| 2 | **几何/画刷内部内容改变不会自动触发重绘**（框架缓存 `Render` 结果） | 图元复用缓存的 `StreamGeometry` 时，改了内容必须手工 `InvalidateVisual()`；否则出现「数据变了图没变」 | [Discussion #18627](https://github.com/AvaloniaUI/Avalonia/discussions/18627) |
| 3 | 文本次像素渲染是 `TextOptions.TextRenderingMode`（默认 `Auto`），`SubpixelAntialias` 等同 ClearType | 不要用 `RenderOptions`；同一个页面里两种模式可以共存，但视觉不统一 | [Text options](https://docs.avaloniaui.net/docs/graphics-animation/text-options/) · [TextRenderingMode](https://docs.avaloniaui.net/api/avalonia/media/textrenderingmode) |
| 4 | **点击穿透没有内置方案**，Windows 上必须 `SetWindowLong(WS_EX_TRANSPARENT)` | 与先前判断一致；确认 R2 风险真实存在 | [Discussion #13827](https://github.com/AvaloniaUI/Avalonia/discussions/13827) · [#11911](https://github.com/AvaloniaUI/Avalonia/discussions/11911) |
| 5 | `Window.ShowActivated` 存在，但只管「首次显示是否激活」 | 不抢焦点是另一件事，仍需 `WS_EX_NOACTIVATE` 或实测确认 | [Window API](https://reference.avaloniaui.net/api/Avalonia.Controls/Window/53C2C6E5) |
| 6 | `RenderTargetBitmap(PixelSize)` + `CreateDrawingContext()` + `Save()` 成立 | 海报/战报导出方案可用，`CreateDrawingContext` 会先把位图清为透明 | [RenderTargetBitmap](https://docs.avaloniaui.net/api/avalonia/media/imaging/rendertargetbitmap) |

> 仍未核实、必须在 T3.5 spike 里实测的：透明窗的 `Opacity` 合成正确性、
> `TransparencyLevelHint.Transparent` 与 `SetWindowRgn` 圆角共存、跨屏 DPI 变化时位图缓存失效时机。
