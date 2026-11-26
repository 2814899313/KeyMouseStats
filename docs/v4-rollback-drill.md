# v4 → v13 回滚演练（M7 / T7.4）

日期：2026-11-23

## 步骤

1. v4 `Kms.Oracle save` 把 `tests/Fixtures/v13-text-current.txt` 解析后重新导出为 v13 文本。
2. 用 **v3 源码编译的 `OracleDump.exe`** 读取导出文件。
3. 对比关键总量。

## 结果

| 字段 | v3 期望 | v4 导出后 v3 读取 |
|---|---:|---:|
| history | 22 | 22 |
| rules | 30 | 30 |
| archives | 0 | 0 |
| total.keys | 321,890 | 321,890 |
| total.clicks | 175,368 | 175,368 |
| total.wheel | 420,959 | 420,959 |

**结论：关键总量一致，v3 能正常解析 v4 导出的 v13 文本。**

## 注意

- 本次只对拍了 6 个总量字段；逐字段 diff 仍应作为发布前检查。
- v4 导出的 header 版本是 `4.0.0-alpha.1`，v3 解析器忽略注释行，不影响读取。
- 回滚入口：报表窗「导出 v13 文本」，或 CLI `tools\rollback-v4-to-v3.bat [v4 stats] [out v13]`。
- `app_keys_v1` 在 v13 里是**按进程路径**聚合；同一路径多窗口的键组分摊在 v3 读取时可能重新归到该路径的代表行，但路径级总量守恒。逐窗口键组请以 v4 文本/SQLite `window_daily_agg` 为准。
