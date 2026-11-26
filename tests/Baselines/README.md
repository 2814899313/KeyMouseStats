# Golden 基线

由 `GoldenHarness.AssertMatches` 生成/比较，headless + Skia 渲染。

## 当前基线

- **整页 9 张**：`page-overview/trend/hours/keys/apps/insights/text/day/heat.png`（928×730 DIP，96 DPI）。
- **Hours 三档 DPI**：`page-hours-96.png`、`page-hours-144.png`、`page-hours-192.png`（1392×1095 / 1856×1460 px）。
- **悬浮窗 12 张**：`widget-m{0..3}-t{0,4,12}.png`（4 模式 × 默认/浅色/末尾深色主题，248×216 DIP）。
- **紧凑视口 2 张**：`page-text-compact.png`、`page-keys-compact.png`（928×650），用于验证短窗口下字词/图表与按键子视图不产生整页滚动。
- **报表 5 张**：`report-tab-0..4.png`（940×640，个人指标/区间/分布/交叉/按住趋势）。
- **整窗外壳 4 张**：`shell-overview.png`、`shell-window.png`、`shell-gujian.png`（古剑美术皮肤）、`shell-custom.png`（自定义壁纸）。
- **悬浮组 8 张**：`overlay-hud-t{0,4}.png`、`overlay-toast-t{0,4}.png`、`overlay-pill-t{0,4}.png`、`overlay-pill-expanded-t{0,4}.png`。
- **原语 2 张**：`primitives-card-96.png`、`primitives-heat-96.png`。
- 整页基线必须通过 `Window` + `CaptureRenderedFrame` 捕获；直接 `RenderTargetBitmap.Render(control)` 不会应用 `UserControl`/`ScrollViewer` 模板，只会得到空白背景。
- 捕获前会先做一次 CJK 字体预热，避免冷启动合成字重造成假失败。
- TextPage 使用 `tests/Fixtures/rime-log-sample.csv` 合成日志；其余页面不读真实 Rime 日志。

## 规则

- 命名：`<page>[-<dpi>].png`，例如 `page-hours-144.png`。
- 比对：灰度 SSIM ≥ 0.98；几何/色板断言另测。
- **基线变更必须走 PR review**，防止悄悄改外观。
- 缺失基线时测试会生成文件并故意失败；人工确认后提交 PNG，再重跑。
- 只有确认外观变更是有意为之，才允许用 `KMS_UPDATE_GOLDEN=1` 重写：

```bat
set KMS_UPDATE_GOLDEN=1
dotnet test tests\Kms.Golden.Tests\Kms.Golden.Tests.csproj -c Release --filter "FullyQualifiedName~PageGoldenTests"
set KMS_UPDATE_GOLDEN=
```

M3/M4 仍待补：悬浮组更多状态（FocusPill/Toast/HUD）的基线。真机跨屏 DPI 与 120fps 见 `docs/v4-acceptance.md`。
