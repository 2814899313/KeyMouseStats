using Avalonia;
using Kms.App;
using Kms.App.Widgets;
using Kms.Render.Theming;
using Kms.Render.Tokens;

namespace Kms.Golden.Tests;

/// <summary>悬浮窗 4 模式 × 3 主题的整窗基线（默认/浅色/末尾深色主题）。</summary>
[Collection("page-golden")]
public sealed class WidgetGoldenTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 4)]
    [InlineData(0, 12)]
    [InlineData(1, 0)]
    [InlineData(1, 4)]
    [InlineData(1, 12)]
    [InlineData(2, 0)]
    [InlineData(2, 4)]
    [InlineData(2, 12)]
    [InlineData(3, 0)]
    [InlineData(3, 4)]
    [InlineData(3, 12)]
    public void Widget_MatchesBaseline(int mode, int themeId)
    {
        GoldenHarness.EnsureAvalonia();
        Application app = Application.Current
            ?? throw new InvalidOperationException("Headless Avalonia 未初始化。");
        ThemeManager.Apply(app, themeId);

        try
        {
            LoadedStats stats = new(GoldenFixture.LoadStats(), "v4 文本 · golden", "实时采集中");
            WidgetWindow widget = new(stats);
            widget.SetMode(mode);
            byte[] actual = GoldenHarness.CaptureWindow(widget);
            GoldenHarness.AssertPng($"widget-m{mode}-t{themeId}", actual);
        }
        finally
        {
            ThemeManager.Apply(app, 0);
        }
    }
}
