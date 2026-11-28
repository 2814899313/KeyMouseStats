using Avalonia.Headless;
using Avalonia.Headless.XUnit;

// Avalonia headless/Skia 的渲染后端是进程级的，多个测试类并行初始化会互相踩状态。
// AvaloniaTestFramework 负责把每个测试调度到同一个 Avalonia Dispatcher。
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
[assembly: AvaloniaTestFramework]
[assembly: AvaloniaTestApplication(typeof(Kms.Golden.Tests.TestAppBuilder))]
