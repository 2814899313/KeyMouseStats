# 测试固件（fixtures）规范

v4 重写要靠**差分对拍**保证「L1 数据 1:1」（见 `docs/v4-rewrite-plan.md` §10.2）。
对拍的输入就是这里定义的固件。

---

## 1. 两条硬规则

### 规则 1：真实样本必须清洗后才能进 Git

v3 的数据里含**真实窗口标题、进程路径、上屏文字**：

| 位置 | 含什么 |
|---|---|
| `kmstats.db` → `events_window_session.window_title` | 窗口标题原文（明文） |
| `kmstats.db` → `events_window_session.process_path` | 完整 exe 路径（明文） |
| `kmstats.db` → `window_daily_agg` | 标题、路径、`process_key`、分类原因 |
| `kmstats.db` → `app_category_rule.rule_key` | `app:` / `window:` / `title:` 复合键里的路径与标题 |
| `stats.txt`（v13） | 标题、路径、规则键、设备名、脚本路径、Webhook，全部 **base64** 编码 |
| `RimeLog` / 日报 / 主题图片 | 上屏文字、个人图片；**真实日志不属于本目录固件**。只允许入库 `rime-log-sample.csv` 这种可复现的合成样本 |

清洗复用 v3 的 `PrivacyMask` 语义，不另写一套：

- `PrivacyMask.MaskTitle` —— 标题 → `语义类别 · 已隐藏`
- `PrivacyMask.MaskPath` —— 路径 → 只留文件名
- `PrivacyMask.MaskText` —— 逐字替换为全角 `＊`，**保留长度与空白**

> 保留长度很关键：布局与图表宽度依赖文本长度，长度一变，渲染测试就没意义了。

### 规则 2：目录分工

| 目录 | 是否入库 | 用途 |
|---|---|---|
| `_fixtures/`（仓库根） | **否**（`.gitignore`） | 本地真实样本，仅供本机清洗 / 对拍 |
| `tests/Fixtures/` | **是** | 已清洗的样本，CI 用 |
| `tests/Fixtures/expected/` | 是 | v3 Oracle 输出的期望值 JSON（M1 生成） |

---

## 2. 已入库的清洗固件

生成于 2026-11-22，源为本机 v3 真实样本；清洗工具见 §4。

| 文件 | 大小 | 覆盖的坑 |
|---|---:|---|
| `v3-sqlite-normal.db` | 4.6 MB | 正常主库，13 表 `schema_rev=1`；WAL 已合并，含 id 5105/5106 两条 WAL-only 会话与 `open_session_id=5106` |
| `v3-sqlite-epoch3995-before.db` | 4.5 MB | 3995 年时间基准缺陷的**修复前备份**，验证 `EpochFixThresholdMs=6e13` |
| `v13-text-current.txt` | 1.0 MB | 文本后端现行档（format 13，UTF-8 BOM，9700 行） |
| `v13-month-2026-09.txt` | 1.4 MB | 月归档大档（17802 行，测解析性能） |
| `v13-month-2026-10.txt` | 181 KB | 月归档小档（2378 行） |
| `v13-synthetic.txt` | 40 KB | 合成示例档 |
| `rime-log-sample.csv` | 45 KB | TextPage 整页 golden 的合成 Rime 日志（812 条、30 天）；由 `tools/gen-rime-fixture.py` 生成 |
| `CHECKSUMS.txt` | — | 上述 v13 / SQLite 文件的 sha256；任何字节漂移都应让 CI 失败 |

`tests/Fixtures/expected/` 存放 v3 Oracle 为上述文本档生成的规范化 dump，用于 M1 差分对拍；
它们由 `tools/oracle/dump-v3-fixtures.bat` 生成，**不纳入 `CHECKSUMS.txt`**，因为重新清洗后必须重生成。
生成方法与「为什么不能拿 exe 当标准答案」见 `tools/oracle/README.md`。

**本地保留、不入库**的四份样本及原因：

| 文件 | 不入库原因 |
|---|---|
| `v3-sqlite-normal.db-wal` | WAL 页帧无法安全逐帧清洗；已用 Backup API 合并进 `.db`。WAL 运行态改在 M1 用合成库测试 |
| `v13-recovered-corrupt.txt` | 崩溃恢复产物，结构不保证可解析；真实坏档候选含隐私。M1 用**合成坏档**覆盖该路径 |
| `v3-migration.log` | 含真实 `C:\Users\<name>\AppData\...` 路径，且不是对拍必需 |
| `v3-migrator-manifest.txt` | 记录的是原始文件哈希，清洗后必然不匹配，保留只会误导 |

**还缺的固件**（M1 用合成数据补，不要手抄真实数据）：

- [ ] 只写了日总数、缺派生桶的老档（验证 `RepairDerivedCounts` 等比缩放）
- [ ] `.kms` 增量包（含重复导入，验证 `kms_import_log` 幂等仲裁）
- [ ] 跨时区/夏令时边界的档（验证本地切日 vs SQLite `'localtime'`）
- [ ] 空库、零字节文件、被截断的库
- [ ] 两个版本的 `daily_summary.origin` 各一份（`shadow` / `migrated` / `snapshot-bridge` / `native`）
- [ ] WAL 未合并的合成库（验证「拷库必须连 WAL」）
- [ ] 合成坏档（替换本地 `v13-recovered-corrupt.txt`）

---

## 3. 清洗后必须保持什么

### 3.1 v13 文本

- **逐行处理，绝不 parse→serialize**：行数、行序、key、字段数、数字、Ticks、`O` 时间串全部原样。
- 只替换这些字段：
  `app=` 的 path/title、`app_rule=` 的规则键、`archive_v1` 第 10 段的 appList、
  `app_hours_v1` / `session_app_v1` / `app_keys_v1` 的 path、
  `hardware_v1` 名称、`keyboard_v1` / `mouse_v1` 的名称与设备标识、
  `wellbeing_v1` 的 ScriptPath/WebhookUrl、`click_screen_v1` 的设备名。
- 路径与标题做全局唯一化：同名 exe / 同类标题追加 `~2`、`~3`，避免 `Apps` 字典合并丢数。
- 哨兵标题（空串、`无前台窗口`、`无法识别的前台窗口`、`窗口切换中 / 无法归因` 等）原样保留。
- 二次清洗必须逐字节幂等。

### 3.2 SQLite

- 13 张表全部保留，**每张表行数不变**。
- `meta`（含 `epoch_fix_v1`、`open_session_id`）、`sqlite_sequence`、所有 ID、所有计数列、
  `daily_summary.origin` 四值、时间戳全部不动。
- `events_window_session` / `events_input_burst`：只改路径与标题。
- `window_daily_agg`：重建表，保持 11,655 行；路径改为文件名，标题改为类别占位；
  主键冲突的标题追加 `~2`、`~3`（**不按行号**，保证二次清洗稳定）。
- `app_category_rule`：拆分 `app:` / `window:` / `title:` 前缀分别清洗；重复键追加 `~2`，保持 30 行。
- `migration_log.content_sha256` 保留原始哈希（不可逆、不是迁移门禁；门禁只看 `meta`）。
- 清洗后执行 `secure_delete` + `journal_mode=DELETE` + `VACUUM`，不留旧页与 `-wal`。
  因此入库的库是**自包含、无 WAL** 的；需要 WAL 行为的测试请用合成库。

### 3.3 隐私验收

`verify` 会做三层检查：

1. `CHECKSUMS.txt` 的 sha256；
2. 原始文本 + SQLite 所有 TEXT 列里的盘符 / UNC / 用户目录；
3. v13 中 base64 解码后的路径 / 标题 / 设备名 / 规则键，以及 SQLite 的原始字节里是否残留 `C:\Users\`、`/home/`、`http(s)://`。

---

## 4. 清洗工具

位置：`tools/scrub-fixtures/`（.NET 10 控制台）。

```bat
:: 读 _fixtures/v3 → 清洗 → 写 tests/Fixtures → 重新生成 CHECKSUMS.txt
dotnet run --project tools/scrub-fixtures -- scrub

:: 目标目录已有固件时必须显式 --force
dotnet run --project tools/scrub-fixtures -- scrub --force

:: 自定义源 / 目标
dotnet run --project tools/scrub-fixtures -- scrub --source <dir> --dest <dir>

:: 校验（CI 用；只读）
dotnet run --project tools/scrub-fixtures -- verify
```

规则：

1. 清洗前先关闭 `KeyMouseStats.exe`，否则 v3 会周期性重写 `stats.txt`。
2. 工具只处理 `v13-*.txt` 与 `*.db`；`v3-migration.log` / `v3-migrator-manifest.txt` 不匹配。
3. 任何 base64 / 字段数 / 行数异常都**中止**，不写半成品。
4. 目标目录里的 `README.md` 不会被删除；其余文件在 `--force` 时清空后重写。

---

## 5. 对拍流程（M1 实现）

```
tests/Fixtures/*  ──┬──> v3 Oracle (Kms.Oracle 引 kms_test_core.dll) ──> expected/*.json
                    └──> v4 Kms.Storage                                   ──> actual/*.json
                                                                               │
                                                                       逐字段比对 1e-9
```

要点：

1. **参考实现在 v3。** `test.bat:39` 已经把所有产品源码编成 `%TEMP%\kms_test_cache\kms_test_core.dll`，
   Oracle 直接引用它，无需改 v3 代码。
2. **参考值必须当次从源码编译**，不能拿仓库里的 `KeyMouseStats.exe` 当标准答案
   （本仓库发生过 exe 3.2.2 / 源码 3.4.1 的情况）。固件本身来自旧版 exe，`meta.app_version=3.0.0` 是历史值，不影响对拍。
3. **比对的是数字，不是文本。** `expected/*.json` 只存可计算的量（keys/clicks/时长/距离/磨损/报表值），
   不存排版与像素。
4. **任何一处不一致都视为失败**，不允许「加容差」蒙混。浮点只允许相对误差 `1e-9`。
