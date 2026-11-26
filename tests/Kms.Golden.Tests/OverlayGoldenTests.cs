using Avalonia;
using Avalonia.Controls;
using Kms.App;
using Kms.App.Widgets;
using Kms.Render.Theming;
using Kms.Render.Tokens;

namespace Kms.Golden.Tests;

/// <summary>HUD 与休息提示的整窗基线（默认/浅色）。</summary>
[Collection("page-golden")]
public sealed class OverlayGoldenTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 4)]
    [InlineData(1, 0)]
    [InlineData(1, 4)]
    [InlineData(2, 0)]
    [InlineData(2, 4)]
    [InlineData(3, 0)]
    [InlineData(3, 4)]
    public void Overlay_MatchesBaseline(int kind, int themeId)
    {
        GoldenHarness.EnsureAvalonia();
        Application app = Application.Current
            ?? throw new InvalidOperationException("Headless Avalonia 未初始化。");
        ThemeManager.Apply(app, themeId);

        try
        {
            LoadedStats stats = new(GoldenFixture.LoadStats(), "v4 文本 · golden", "实时采集中");
            Window window;
            if (kind == 0)
            {
                window = new GameHudWindow(stats, () => 219);
            }
            else if (kind == 1)
            {
                FocusBreakToast toast = new();
                toast.ShowToast("休息一下", "已经连续输入一段时间，起来活动一下。");
                window = toast;
            }
            else
            {
                FocusPillWindow pill = new(stats);
                pill.FreezeForGolden(kind == 3);
                window = pill;
            }

            string name = kind switch
            {
                0 => "hud",
                1 => "toast",
                2 => "pill",
                _ => "pill-expanded"
            };
            byte[] actual = GoldenHarness.CaptureWindow(window);
            GoldenHarness.AssertPng($"overlay-{name}-t{themeId}", actual);
        }
        finally
        {
            ThemeManager.Apply(app, 0);
        }
    }
}
