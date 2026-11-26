using Kms.App.Reports;
using Kms.Core.Model;
using Kms.Render.Theming;
using SkiaSharp;

namespace Kms.Golden.Tests;

/// <summary>战报长图：动态高度、能出图、脱敏开关改变内容。</summary>
public sealed class BattlePosterTests
{
    [Fact]
    public void MeasureHeight_GrowsAfterTwelveApps()
    {
        DayRecord day = new() { Date = new DateTime(2026, 11, 22) };
        Assert.Equal(BattlePosterSurface.DefaultPosterHeight, BattlePosterSurface.MeasureHeight(day));

        for (int i = 0; i < 20; i++)
        {
            AppUsage app = new() { ProcessPath = $"C:\\Apps\\app{i}.exe", Title = $"app {i}" };
            day.Apps[app.Id] = app;
        }

        Assert.True(BattlePosterSurface.MeasureHeight(day) > BattlePosterSurface.DefaultPosterHeight);
    }

    [Fact]
    public void Render_ProducesNonBlankPoster_AndDesensitizeChangesPixels()
    {
        StatsData data = GoldenFixture.LoadStats();
        DayRecord day = data.History.Values.OrderByDescending(value => value.Date).First();
        int height = BattlePosterSurface.MeasureHeight(day);

        byte[] normal = GoldenHarness.Render(
            new BattlePosterSurface(day, ThemeManager.Current),
            BattlePosterSurface.PosterWidth,
            height);
        byte[] masked = GoldenHarness.Render(
            new BattlePosterSurface(day, ThemeManager.Current, desensitize: true),
            BattlePosterSurface.PosterWidth,
            height);

        using SKBitmap bitmap = SKBitmap.Decode(normal)!;
        Assert.Equal(BattlePosterSurface.PosterWidth, bitmap.Width);
        Assert.Equal(height, bitmap.Height);

        HashSet<uint> colors = [];
        for (int y = 0; y < bitmap.Height; y += 4)
        {
            for (int x = 0; x < bitmap.Width; x += 4)
            {
                colors.Add((uint)bitmap.GetPixel(x, y));
            }
        }

        Assert.True(colors.Count > 64);
        Assert.NotEqual(Convert.ToHexString(normal), Convert.ToHexString(masked));
    }
}
