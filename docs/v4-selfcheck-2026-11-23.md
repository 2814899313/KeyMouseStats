# v4 悬浮窗样式自检 · 2026-11-23

## 环境

| 项目 | 值 |
|---|---|
| 机器 | 妖妖（本机） |
| OS | Windows 10.0.26200 |
| 渲染缩放 | 1.5 |
| 命令 | `Kms.App --selfcheck-ui` |
| 采集状态 | 未启动 `InputService`（不挂钩子） |

## 结果

`Pass = true`，主窗 9 页可创建，HUD 点击穿透状态为 `true`。

| 窗口 | 可见 | 置顶 | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | 结论 |
|---|---|---|---|---|---|---|
| 主面板 | ✅ | — | — | — | — | ✅ |
| Widget | ✅ | ✅ | ✅ | ✅ | — | ✅ |
| FocusPill | ✅ | ✅ | ✅ | ✅ | — | ✅ |
| GameHud | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| FocusBreakToast | ✅ | ✅ | ✅ | ✅ | — | ✅ |

原始 JSON：`%TEMP%\kms-v4-selfcheck.json`。

## 边界

- 本自检证明 Win32 扩展样式已设置，不替代人工鼠标穿透/焦点测试。
- 自检同时记录了 `WindowFromPoint` 结果，但多悬浮窗重叠时不能作为穿透判据，未纳入 `Pass`。
- 真机穿透、拖拽、跨屏仍需按 `tools/transparent-spike/README.md` 与 `docs/v4-acceptance.md` 勾选。
