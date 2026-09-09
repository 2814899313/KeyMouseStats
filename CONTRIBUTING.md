# 参与开发

本项目面向 Windows 桌面环境，使用系统自带的 .NET Framework C# 编译器构建。

1. Fork 仓库并从 `main` 创建分支。
2. 在 Windows 10/11 上运行 `build.bat`。
3. 运行 `test.bat`，确认统计、主题和渲染检查通过。
4. 提交 Pull Request，并说明行为变化和验证方法。

提交中不要包含个人统计数据、测试截图、编译产物或 `.opensquilla` 本地附件。

## 发布

1. 更新 `AssemblyInfo.cs`、`CHANGELOG.md` 与 `docs/releases/v<版本>.md`，提交到 `main`。
2. 打标签并推送：`git tag v1.5.0 && git push origin v1.5.0`。
3. `Release` 工作流会构建完整版与清爽版、校验标签与程序集版本一致、校验清爽版体积与资源，然后自动创建 GitHub Release 并附上两个 EXE。
4. 标签与程序集版本不一致时发布会被中止，不会产生半个版本的产物。

README 的下载链接指向 `releases/latest`，因此发布完成后无需再改文档。

