# 字符内容统计

「字符内容」页让键鼠统计第一次能回答「打出来的是什么」——但数据不是程序抓的，而是输入法自己写的上屏日志。

## 启用条件

「字符内容」页**不是通用功能**，只适用于特定输入法组合。必须同时满足：

1. 使用 Windows 版 **小狼毫输入法（Weasel）** —— <https://github.com/rime/weasel/releases/latest>
2. 输入方案为 **雾凇拼音（rime-ice）** —— <https://github.com/iDvel/rime-ice>
3. 安装并启用配套的 **上屏记录插件（commit_logger）** —— 本仓库 [`rime/commit_logger/`](../rime/commit_logger/)（开源，随仓库发布）

三个条件缺任意一个，字符页都会显示「未找到 commit_log.csv」，其余键鼠统计不受影响。微软拼音、搜狗拼音、QQ 拼音等其他输入法不支持。

## 安装记录插件

插件源码与完整说明在 [`rime/commit_logger/README.md`](../rime/commit_logger/README.md)，三种装法：

**A. 一条命令（推荐）** —— 下载本仓库，进入 `rime/commit_logger/`，然后：

```powershell
powershell -ExecutionPolicy Bypass -File .\install-commit-logger.ps1
```

脚本只做三件事：复制两个 lua 到 `<用户目录>/lua/`、在 `rime.lua` 末尾追加两行登记、在方案补丁里追加处理器与滤镜；改文件前会把原文件备份到 `<用户目录>/_backup_commit_logger_<时间戳>/`。重复执行是安全的（已装的会跳过），已有配置不会被覆盖。

**B. 手动三步**

1. 把 `lua/commit_logger.lua`、`lua/commit_logger_filter.lua` 复制进 `<Rime 用户目录>/lua/`
2. 在 `<Rime 用户目录>/rime.lua` **末尾追加**两行：
   ```lua
   commit_logger        = require("commit_logger")
   commit_logger_filter = require("commit_logger_filter")
   ```
3. 在 `<Rime 用户目录>/rime_ice.custom.yaml` 里合并：
   ```yaml
   patch:
     "engine/processors/+":
       - lua_processor@*commit_logger
     "engine/filters/+":
       - lua_filter@*commit_logger_filter
   ```
4. 小狼毫托盘图标右键 → **重新部署**

> ⚠️ 两个必踩的坑：`rime.lua` 是**追加**不是覆盖（覆盖会让日期、农历、以词定字、纠错全失效）；
> 方案补丁必须写 `/+`，写 `@last` 会顶掉 `express_editor`，表现为空格不能上屏、退格删不掉。

Windows 用户目录是 `%APPDATA%\Rime`；macOS 是 `~/Library/Rime`；fcitx5-rime 是 `~/.local/share/fcitx5/rime`。

## 自检与刷新

打开 `%APPDATA%\Rime\commit_log.csv`，如果文件存在、且在你每次输入后持续增长，就说明条件已满足。页面里的「重新读取」会立即重新读取；自动刷新间隔可在设置中调整（默认 15 秒）。

页面底部在没有数据时会直接给出三个入口：**下载小狼毫 / 雾凇拼音 / 记录插件**。

## 数据格式

程序只接受完整的七列记录：

```
时间点,字符数,明文,程序,方案,码长,候选序
```

- 旧三列、空候选、缺程序或码长的记录会归档后自动跳过，页脚会显示已跳过的条数；
- 候选序为空记为未知；显式 `-1` 表示非候选上屏（没装滤镜时的兜底）；
- 每次上屏额外计算真实总字符数、纯汉字数、输入类型（`normal / raw_ascii / punct / mixed / other`）与原始码长。

列的写入端定义见 [`rime/commit_logger/README.md`](../rime/commit_logger/README.md#五列契约下游按此解析)。

## 页内视图

- **概览**：区间字数、日均 / 每分钟上屏、高频单字与词组（可仅看汉字或含标点）、最近上屏隐私模糊、词长与字符构成、每日 / 时段分布。
- **效率**：首选率、翻页率、候选覆盖率、平均码长（键 / 汉字），无候选样本时显示 `—`。
- **应用**：按前台程序聚合输出字数、上屏次数与每应用高频字，应用名使用友好名映射（如 `msedge.exe` → Microsoft Edge）。
- 支持按区间导出 CSV。

## 隐私

- 程序只**读取** `commit_log.csv`，不挂钩子、不读剪贴板、不写入该文件、不监听输入法；
- 字符数据不进入 `stats.txt`，不参与导入合并与键鼠导出；
- 日志中的明文只在本机内存与页面里展示，不会上传到任何服务器；
- 页面提供「最近上屏模糊」开关，方便在录屏或截图时隐藏内容。
