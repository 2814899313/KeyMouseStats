using Kms.App.Reports;
using Kms.Core.Model;
using Kms.Render.Tokens;

namespace Kms.Golden.Tests;

public sealed class DailyPosterRenderTests
{
    [Fact]
    public void DailyPoster_RendersAtReducedSize()
    {
        DayRecord day = new()
        {
            Date = new DateTime(2026, 11, 22),
            Keys = 12_345,
            Clicks = 2_345,
            MoveMeters = 320.5,
            ActiveSeconds = 7200
        };
        day.HourKeys[9] = 1200;
        day.HourKeys[14] = 2200;
        day.Apps["code"] = new AppUsage
        {
            ProcessPath = "Code.exe",
            Title = "main.cs",
            Keys = 8000,
            Clicks = 900,
            ActiveSeconds = 4000
        };

        DailyPosterSurface poster = new(day, ThemeCatalog.Get(0));
        byte[] png = GoldenHarness.Render(
            poster,
            DailyPosterSurface.PosterWidth,
            DailyPosterSurface.PosterHeight);

        byte[] gray = GoldenHarness.Grayscale(png, out int width, out int height);
        Assert.Equal(DailyPosterSurface.PosterWidth, width);
        Assert.Equal(DailyPosterSurface.PosterHeight, height);
        Assert.True(gray.Distinct().Count() > 8);
    }
}
