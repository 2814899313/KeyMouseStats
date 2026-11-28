using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Kms.App.Platform;

namespace Kms.Golden.Tests;

[Collection("page-golden")]
public sealed class AppIconCacheServiceTests
{
    [Fact]
    public async Task GetAsync_UsesBoundedLruAndMemoryOnlyReads()
    {
        GoldenHarness.EnsureAvalonia();
        using WriteableBitmap bitmap = new(
            new PixelSize(2, 2),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        AppIconPixels pixels = new(2, 2, 8, new byte[16]);
        AppIconCacheService cache = new(
            _ => pixels,
            _ => bitmap,
            marshalToUiThread: false);

        Assert.Null(cache.TryGet("app0.exe"));
        for (int i = 0; i < AppIconCacheService.Capacity + 2; i++)
        {
            Bitmap? loaded = await cache.GetAsync(
                $"app{i}.exe",
                TestContext.Current.CancellationToken);
            Assert.Same(bitmap, loaded);
        }

        Assert.Equal(AppIconCacheService.Capacity, cache.Count);
        Assert.Null(cache.TryGet("app0.exe"));
        Assert.Null(cache.TryGet("app1.exe"));
        Assert.Same(bitmap, cache.TryGet("app129.exe"));
    }

    [Fact]
    public async Task Presenter_ReplacesFallbackAfterAsyncIconReady()
    {
        GoldenHarness.EnsureAvalonia();
        using WriteableBitmap bitmap = new(
            new PixelSize(2, 2),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        AppIconPixels pixels = new(2, 2, 8, new byte[16]);
        AppIconCacheService cache = new(
            _ => pixels,
            _ => bitmap,
            marshalToUiThread: false);
        ContentControl host = new() { Width = 32, Height = 32 };
        Color tone = Colors.MediumPurple;

        AppIconPresenter.SetFallback(host, "ChatGPT.exe", 32, tone);
        Assert.IsType<Border>(host.Content);

        await AppIconPresenter.ApplyAsync(
            host,
            "chatgpt.exe",
            20,
            32,
            tone,
            "ChatGPT.exe",
            cache);

        Image image = Assert.IsType<Image>(host.Content);
        Assert.Same(bitmap, image.Source);
    }
}
