using Avalonia;

namespace Kms.App;

internal static class Program
{
    // 初始化代码不要放在这里；Avalonia 配置完成前不要使用任何 Avalonia、第三方 API。
    // 需要初始化的内容放到 App.Initialize 里。
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Avalonia 设计器需要这个方法。
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
