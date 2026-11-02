# 参与开发

本项目面向 Windows 桌面环境，使用系统自带的 .NET Framework C# 编译器构建。

1. Fork 仓库并从 `main` 创建分支。
2. 在 Windows 10/11 上运行 `build.bat`。
3. 运行 `test.bat`，确认统计、主题和渲染检查通过。
4. 提交 Pull Request，并说明行为变化和验证方法。

提交中不要包含个人统计数据、测试截图、编译产物或 `.opensquilla` 本地附件。

## 版本号

版本号只有一个来源:`Version.cs` 里的 `BuildInfo.Version` 与 `BuildInfo.FileVersion`。**每次交付都要动它**,不要攒着一起改:

| 改动 | 升哪一位 | 例子 |
|---|---|---|
| 新统计项、修 bug、调界面 | 补丁位 | 2.0.0 → 2.0.1 |
| 架构或使用方式变了 | 次版本位 | 2.0 → 2.1 |
| 不兼容的大改 | 主版本位 | 2.x → 3.0 |

同时要补两处:**`CHANGELOG.md` 顶部新增一节**(编号与版本号一致)、**`docs/releases/` 下新建同版本号说明**。三处不一致时,发布工作流会因为 tag 与 `FileVersion` 对不上而中止。

## 发布

1. 按上面的规则升 `Version.cs`,补 `CHANGELOG.md` 与 `docs/releases/v<版本>.md`,提交到 `main`。
2. 打标签并推送:`git tag v2.0.1 && git push origin v2.0.1`。
3. `Release` 工作流会构建完整版与清爽版、校验标签与程序集版本一致、校验清爽版体积与资源,然后自动创建 GitHub Release 并附上两个 EXE。
4. 标签与程序集版本不一致时发布会被中止,不会产生半个版本的产物。

README 的下载链接指向 `releases/latest`，因此发布完成后无需再改文档。

