# transparent-spike（M3 / T3.5）

验证 Avalonia 12 在 Windows 上的：透明背景、无边框、置顶、不抢焦点、点击穿透、透明度调节、PerMonitor DPI。

## 运行

```bat
dotnet run --project tools\transparent-spike
```

快捷键：

| 快捷键 | 作用 |
|---|---|
| `Ctrl + Alt + T` | 切换点击穿透（`WS_EX_TRANSPARENT`） |
| `Ctrl + Alt + ↑ / ↓` | 调整窗口不透明度（0.2–1.0） |

`--seconds 5` 可让窗口 5 秒后自动关闭（只做启动冒烟）。

## 验收清单（R2 的答案）

- [ ] 背景真正透明，不是黑底/白底
- [ ] 圆角外无方形残留
- [ ] 点击穿透开启后，鼠标可点到下面的窗口，且本窗口不再接收点击
- [ ] 点击/拖动本窗口不会把焦点从当前前台应用抢走（`WS_EX_NOACTIVATE`）
- [ ] 置顶生效，切到其他应用后仍在上层
- [ ] 跨 100% / 150% / 200% 屏幕拖动不模糊、不变形
- [ ] 透明度 0.2 / 0.5 / 1.0 三档正常
- [ ] 退出后无残留窗口/句柄

## 已知限制

- 当前 manifest 是 `asInvoker`；若用于提权游戏叠加，需要与主程序一样 `requireAdministrator`。
- 点击穿透后只能靠全局快捷键恢复，这是 spike 的刻意设计；产品版会有托盘菜单/编辑模式。
- 没有做 `SetWindowRgn` 圆角兜底；如果某些 Windows 版本上 Skia 透明层出现方形边缘，再补该分支。
