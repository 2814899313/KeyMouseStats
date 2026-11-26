# v3 Oracle（差分对拍标准答案）

`tests/OracleDump.cs` 用 **v3 当前源码** 的 `Store.ParseText` 解析 v13 文本，输出规范化 dump。
v4 `Kms.Oracle` 用 `TextStatsCodec.Load` 解析同一份文件，输出同样格式，逐行 diff。

## 生成

```bat
:: 1. 编译 v3 core + Oracle（会自动跑一遍 v3 逻辑测试，约 2 分钟）
tools\oracle\build-v3-oracle.bat

:: 2. 为 tests/Fixtures 下所有 v13-*.txt 重生成 expected/*.dump
tools\oracle\dump-v3-fixtures.bat
```

`build-v3-oracle.bat` 依赖：

- `test.bat` 生成的 `%TEMP%\kms_test_cache\kms_test_core.dll`
- `test.bat` 的 `InternalsVisibleTo` 生成逻辑（`tests\*.cs` 会包含 `OracleDump`）
- .NET Framework 自带 csc（脚本只用 C# 5 语法，不能升级 OracleDump 到新语法）

## 注意

- **不能拿仓库里的 `KeyMouseStats.exe` 当标准答案**：本仓库发生过 exe 落后源码两个版本。
- `expected/` 不纳入 `CHECKSUMS.txt`；重新清洗固件后必须重新生成。
- Oracle 只导出可计算量，不导出排版/像素。
- dump 使用 `G17` 输出 double；v4 对拍按相对误差 `1e-9` 比较，因为 .NET Framework 与 .NET 10 的
  `"R"` 最短表示在最后一位可能不同（底层 double 相同）。v13 文本的 `Save` 仍使用 `"R"`，同样允许该差异。
