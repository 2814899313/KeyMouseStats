# input-probe（M2 并行对拍）

真实安装低级钩子 + Raw Input，把事件写入 `DbWriter` 的 SQLite 库，用于和 v3 并行计数对拍。

## 运行

```bat
:: 一直运行，Ctrl+C 退出
dotnet run --project tools\input-probe

:: 固定运行 60 秒（冒烟）
dotnet run --project tools\input-probe -- --seconds 60
```

输出每秒打印：

```
keys=… clicks=… wheel=… move=… rawMouse=… rawKb=… active=…s
```

库文件默认在仓库根 `.tmp-input-probe\kmstats.db`（已被 `.gitignore` 的 `/.tmp-*/` 覆盖）。

## 24 小时对拍

1. 启动 v3 `KeyMouseStats.exe`（标准版，保持托盘运行）。
2. 启动本 probe，运行 24 小时（不加 `--seconds`，或 `--seconds 86400`）。
3. 退出后对比两边 `daily_summary`：

```bat
python tools\input-probe\compare.py
```

默认比较：

- v3：`%LOCALAPPDATA%\KeyMouseStats\kmstats.db`
- v4：`.tmp-input-probe\kmstats.db`

口径只比较 `keys / clicks / wheel`；验收要求误差 < 0.1%，且 probe 的 `ringDropped=0`、无崩溃。

## 注意

- 必须在**有消息循环的线程**安装钩子；probe 的主线程负责 `GetMessage`，Raw Input 自己起后台线程。
- 需要和 v3 一样以管理员运行，才能捕获提权游戏的输入。
- 本工具不参与 CI。
