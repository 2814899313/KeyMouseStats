# commit_logger —— Rime 上屏记录器（librime-lua）

把每次**上屏的文字**追加成一行 CSV，供 [键鼠统计](https://github.com/2814899313/KeyMouseStats) 的「字符内容」页读取。

- 只读、不拦键：挂的是 `commit_notifier`，`func` 恒返回 `kNoop`，不改变候选、不影响输入；
- 每个引擎各连各的（每个窗口/输入框互不干扰），切窗口后不会「只有第一个窗口能记」；
- 写盘失败（比如 CSV 正被 Excel 独占）先存内存，下次自动补写；
- 输出 UTF-8 BOM 的 CSV，Excel 双击打开不乱码。

> 同一份文件 Windows / macOS / Linux(fcitx5-rime, ibus-rime) 通用：用户目录用
> `rime_api.get_user_data_dir()` 取，取不到时回退到各平台默认位置。

---

## 一、先装输入法

| 组件 | 说明 | 下载 |
|---|---|---|
| **小狼毫 (Weasel)** | Windows 版 Rime 前端，自带 librime-lua 支持 | <https://github.com/rime/weasel/releases/latest> |
| **雾凇拼音 (rime-ice)** | 开箱即用的拼音方案（本插件默认挂在 `rime_ice` 上） | <https://github.com/iDvel/rime-ice> |

小狼毫装好后，托盘图标右键 → **输入法设定 / 重新部署** 能看到方案列表即可。
雾凇拼音的安装方式见它自己的 README（一般是把 `*.yaml` 与 `cn_dicts/`、`en_dicts/` 复制进 Rime 用户目录再部署）。

> 用户目录位置：Windows `%APPDATA%\Rime`，macOS `~/Library/Rime`，
> fcitx5-rime `~/.local/share/fcitx5/rime`，ibus-rime `~/.config/ibus/rime`。

---

## 二、装插件（三步，缺一不可）

> 也可以直接跑仓库里的 `install-commit-logger.ps1`（只做下面三件事，改文件前先备份）。
> 手动装的话照做：

### 第 1 步：放两个 lua 文件

把本目录 `lua/` 里的两个文件复制到 `<Rime 用户目录>/lua/`（没有 `lua` 目录就新建）：

```
lua/commit_logger.lua          记录器本体
lua/commit_logger_filter.lua   候选流观察器（补「候选序」列）
```

### 第 2 步：在 `rime.lua` 里登记

打开 `<Rime 用户目录>/rime.lua`，在**末尾追加**两行：

```lua
commit_logger        = require("commit_logger")
commit_logger_filter = require("commit_logger_filter")
```

> ⚠️ **追加，不要覆盖**。雾凇拼音用户这个文件里本来就有十几行
> （`date_translator = require("date_translator")` 之类），整个替换掉会让日期、农历、
> 以词定字、纠错、Emoji 提示等全部失效。

### 第 3 步：挂进方案（处理器 + 滤镜）

在 `<Rime 用户目录>/rime_ice.custom.yaml`（双拼/九宫格就换成对应方案的 `.custom.yaml`）里：

```yaml
patch:
  "engine/processors/+":
    - lua_processor@*commit_logger
  "engine/filters/+":
    - lua_filter@*commit_logger_filter
```

> ⚠️ 必须是 `/+`（追加到末尾）。写成 `@last` 是「替换最后一项」，会把 `express_editor`
> 顶掉 —— 表现为空格不能上屏、退格删不掉、Esc 失效、只有数字键能选字。

### 第 4 步：重新部署

小狼毫：托盘图标右键 → **重新部署**；鼠须管：菜单栏图标 → 重新部署；
fcitx5-rime：`fcitx5-remote -r`。

---

## 三、验证

随手打几个字上屏，然后看用户目录里的 `commit_log.csv`：

```
时间点,字符数,明文,程序,方案,码长,候选序
2026-10-28 21:15:41,2,我想,wechat.exe,rime_ice,6,0
2026-10-28 21:15:43,2,这个,code.exe,rime_ice,6,2
```

- 有表头、有数据行 → 装好了，打开键鼠统计的「字符内容」页即可看到统计；
- 只有表头 / 没文件 → 看下一节；
- 出现 `commit_log.err.txt` → 里面写了具体原因。

---

## 四、排错

| 现象 | 原因与处理 |
|---|---|
| 空格不能上屏、退格删不掉、只有数字键有用 | 方案补丁写成了 `@last`，顶掉了 `express_editor`。改成 `/+`。 |
| 部署后没有 CSV | `rime.lua` 里没加登记行；或改的是安装目录里的 `rime.lua`（只读的共享副本，不是用户目录那个）。 |
| 前几分钟能记，切窗口后不再记 | 旧版 bug（全局只保留一个连接）。本目录是修复版，每个引擎各连各的。 |
| `commit_log.err.txt` 里是 `open for append failed` | CSV 正被 Excel/WPS 独占打开。关掉即可，期间的上屏会先存内存、关掉后补写。 |
| 一次上屏记了两行 | 方案补丁里同一个 `lua_processor` 写了两遍，删掉重复行。 |
| 候选序一直是 `-1` | 只装了 `commit_logger.lua`，没装 `commit_logger_filter.lua` 或没在 `engine/filters/+` 里登记。 |
| 想知道哪一环断了 | 把 `commit_logger.lua` 顶部 `CONFIG.debug` 改成 `true`，重新部署，生成的 `commit_log.debug.txt` 里会有 `module loaded` / `engine connected` / `commit: ...` 三条线索。 |

---

## 五、列契约（下游按此解析）

```
时间点,字符数,明文,程序,方案,码长,候选序
```

| 列 | 含义 |
|---|---|
| 时间点 | 本地时间，`%Y-%m-%d %H:%M:%S` |
| 字符数 | 上屏明文的 UTF-8 码点数（汉字/字母/标点的分类由下游推导） |
| 明文 | 原始上屏文本，按 CSV 规则转义（含 `,` `"` CR LF 时加引号、内部引号双写） |
| 程序 | `client_app`，前台进程名，取不到写空 |
| 方案 | `schema_id`（如 `rime_ice`） |
| 码长 | 输入串去掉分隔空格后的字符数；纯标点/英文直出无编码时为 `0` |
| 候选序 | `0` 首选；`1..N` 第 N+1 个候选；`-1` 非候选上屏（取不到 Menu 时的兜底，**绝不写空**） |

键鼠统计只接受完整的七列记录；旧的三列/空候选/缺程序或码长的行会被归档后跳过，页脚显示跳过的条数。

---

## 六、可调参数

`lua/commit_logger.lua` 顶部的 `CONFIG`：

```lua
file_name = "commit_log.csv"   -- 文件名
header    = { "时间点", "字符数", "明文", "程序", "方案", "码长", "候选序" }
with_bom  = true               -- Excel 友好;给程序读可改 false
min_chars = 1                  -- 小于该字数的上屏不记录(0 = 全记)
time_fmt  = "%Y-%m-%d %H:%M:%S"
debug     = false              -- true 时生成 commit_log.debug.txt
```

---

## 七、卸载

删掉 `lua/` 里那两个文件、`rime.lua` 里那两行、方案补丁里那四行，重新部署即可。
已记录的 `commit_log.csv` 不会被删除。

---

## 八、兼容性

- 需要前端自带 **librime-lua**：小狼毫 0.15+、鼠须管、ibus-rime、fcitx5-rime 都自带；
- 本插件**不改键位**。如果你想连搜狗键位一起给，注意 librime 版本：1.13 只认
  `left_by_char`，1.14+ 才有 `left_by_char_no_loop`，写错会在日志里报 `invalid action` 并静默失效。

## 许可

与 [键鼠统计](https://github.com/2814899313/KeyMouseStats) 同一许可，随仓库一同发布。
