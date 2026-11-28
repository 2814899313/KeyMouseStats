using Avalonia.Media.Imaging;
using Kms.App.Pages;
using Kms.App.Platform;
using Kms.Core.Model;
using Kms.Core.Time;

namespace Kms.Golden.Tests;

public sealed class AppsPageIconRenderTests
{
    [Fact]
    public void AppsPage_WithSystemIcons_RendersFrame()
    {
        GoldenHarness.EnsureAvalonia();
        string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        Assert.True(File.Exists(cmd));

        StatsData data = new();
        DayRecord day = data.GetOrCreate(DateTime.Today);
        day.Keys = 120;
        day.Clicks = 30;
        day.AppObservedSeconds = 180;
        day.Apps["cmd"] = new AppUsage
        {
            ProcessPath = cmd,
            Title = "命令提示符",
            ActiveSeconds = 120,
            Keys = 100,
            Clicks = 20
        };

        bool previous = AppIconLoader.Enabled;
        AppIconLoader.Enabled = true;
        try
        {
            AppsPage page = new(
                data,
                "应用统计",
                "前台窗口、APM 与时长占比",
                TimeScopeRange.Create(TimeScope.Today, DateTime.Now),
                GoldenLayouts.Empty);
            byte[] png = GoldenHarness.RenderHosted(page, 928, 730);
            Assert.True(png.Length > 10_000);
            File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "kms-apps-icons.png"), png);
        }
        finally
        {
            AppIconLoader.Enabled = previous;
            AppIconLoader.Clear();
        }
    }
}
