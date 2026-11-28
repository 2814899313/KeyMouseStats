using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Kms.App.Pages;

namespace Kms.App.Platform;

/// <summary>
/// 应用图标占位与异步替换。真实图标就绪后无条件写回 host，保证首帧徽章不会永久停留。
/// </summary>
internal static class AppIconPresenter
{
    public static void SetFallback(
        ContentControl host,
        string name,
        double size,
        Color tone)
    {
        host.Content = FallbackBadge(name, size, tone);
    }

    public static async Task ApplyAsync(
        ContentControl host,
        string path,
        double iconSize,
        double hostSize,
        Color tone,
        string name,
        AppIconCacheService icons)
    {
        Bitmap? bitmap = icons.TryGet(path);
        if (bitmap is null)
        {
            bitmap = await icons.GetAsync(path);
        }

        // 不检查 Parent：侧栏按钮在调用时尚未挂入视觉树，检查会导致缓存命中也不刷新。
        host.Content = bitmap is null
            ? FallbackBadge(name, hostSize, tone)
            : IconImage(bitmap, iconSize);
    }

    public static Control FallbackBadge(string name, double size, Color tone)
    {
        string initial = string.IsNullOrWhiteSpace(name)
            ? "?"
            : name.Trim()[..1].ToUpperInvariant();
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(Math.Max(5, size * 0.22)),
            Background = PageKit.Brush(PageKit.Alpha(tone, 0.14)),
            BorderBrush = PageKit.Brush(PageKit.Alpha(tone, 0.26)),
            BorderThickness = new Thickness(1),
            Child = PageKit.Label(
                initial,
                Math.Max(8, size * 0.36),
                tone,
                FontWeight.Bold)
        };
    }

    private static Control IconImage(Bitmap bitmap, double size)
    {
        Image image = new()
        {
            Source = bitmap,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.HighQuality);
        return image;
    }
}
