using Avalonia;
using Avalonia.Controls;
using Kms.App;
using Kms.App.Theming;
using Kms.App.Views;
using Kms.Render.Theming;

namespace Kms.Golden.Tests;

/// <summary>完整外壳（侧栏 + 总览）的整窗基线。</summary>
[Collection("page-golden")]
public sealed class ShellGoldenTests
{
    [Fact]
    public void Shell_Overview_MatchesBaseline()
    {
        Capture("shell-overview", data => data.Settings["theme_id"] = "0");
    }

    [Fact]
    public void MainWindow_MatchesBaseline()
    {
        Capture("shell-window", data => data.Settings["theme_id"] = "0", mainWindow: true);
    }

    [Fact]
    public void Shell_CustomTheme_MatchesBaseline()
    {
        Capture("shell-custom", data =>
        {
            data.Settings["theme_id"] = "0";
            data.Settings["custom_theme_path"] = System.IO.Path.Combine(
                GoldenFixture.RepoRoot(), "assets", "ori-overview.jpg");
            data.Settings["custom_theme_name"] = "自定义皮肤";
        });
    }

    [Fact]
    public void Shell_ArtTheme_MatchesBaseline()
    {
        Capture("shell-gujian", data => data.Settings["theme_id"] = "12");
    }

    private static void Capture(string name, Action<Kms.Core.Model.StatsData> configure, bool mainWindow = false)
    {
        GoldenHarness.EnsureAvalonia();
        Application app = Application.Current
            ?? throw new InvalidOperationException("Headless Avalonia 未初始化。");
        int previousTheme = ThemeManager.Current.Id;
        bool previousCustom = ThemeArtwork.CustomActive;

        try
        {
            Kms.Core.Model.StatsData data = GoldenFixture.LoadStats();
            configure(data);
            LoadedStats stats = new(data, "v4 文本 · golden", "实时采集中");
            Window window = mainWindow
                ? new MainWindow(stats)
                : new Window
                {
                    Width = 1180,
                    Height = 760,
                    WindowDecorations = WindowDecorations.None,
                    Content = new ShellView(stats)
                };
            byte[] actual = GoldenHarness.CaptureWindow(window);
            GoldenHarness.AssertPng(name, actual);
        }
        finally
        {
            ThemeManager.Apply(app, previousTheme);
            ThemeArtwork.CustomActive = previousCustom;
        }
    }
}
