using System.Diagnostics;
using Kms.App.Reports;
using Kms.Core.Model;
using Kms.Render.Theming;

namespace Kms.Golden.Tests;

public sealed class PosterExportTimingTests(ITestOutputHelper output)
{
    [Fact]
    public void BattlePoster_ThreeX_RendersWithinBudget()
    {
        GoldenHarness.EnsureAvalonia();
        StatsData data = GoldenFixture.LoadStats();
        DayRecord day = data.History.Values.OrderByDescending(value => value.Date).First();
        int height = BattlePosterSurface.MeasureHeight(day);

        Stopwatch watch = Stopwatch.StartNew();
        using Avalonia.Media.Imaging.RenderTargetBitmap? bitmap = PosterExporter.Render(
            new BattlePosterSurface(day, ThemeManager.Current),
            BattlePosterSurface.PosterWidth,
            height,
            3);
        watch.Stop();

        output.WriteLine($"3x battle poster: {bitmap?.PixelSize.Width}x{bitmap?.PixelSize.Height}, {watch.ElapsedMilliseconds} ms");
        Assert.NotNull(bitmap);
        Assert.True(watch.Elapsed.TotalSeconds < 5, $"导出耗时 {watch.Elapsed.TotalSeconds:0.00}s");
    }
}
