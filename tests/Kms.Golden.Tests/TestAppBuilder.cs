using Avalonia;

namespace Kms.Golden.Tests;

/// <summary>
/// Avalonia Headless 测试入口。全局框架会把所有测试调度到同一个
/// Avalonia Dispatcher，避免跨线程访问 Composition/Window 导致的假失败。
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => GoldenHarness.BuildAppBuilder();
}
