using Avalonia;
using Kms.App;
using Kms.App.Views;
using Kms.Render.Theming;

namespace Kms.Golden.Tests;

[Collection("page-golden")]
public sealed class ThemeSelectionTests
{
    [Fact]
    public void Shell_FallsBackToV3ArtTheme()
    {
        GoldenHarness.EnsureAvalonia();
        Application app = Application.Current
            ?? throw new InvalidOperationException("Headless Avalonia 未初始化。");
        int previous = ThemeManager.Current.Id;
        try
        {
            Kms.Core.Model.StatsData data = GoldenFixture.LoadStats();
            data.Settings.Remove("theme_id");
            data.Settings["art_theme"] = "12";
            LoadedStats stats = new(data, "v4 文本 · golden", "实时采集中");
            _ = new ShellView(stats);
            Assert.Equal(12, ThemeManager.Current.Id);
        }
        finally
        {
            ThemeManager.Apply(app, previous);
        }
    }
}
