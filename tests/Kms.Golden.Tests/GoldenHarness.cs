using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Kms.Render.Golden;
using Kms.Render.Theming;
using SkiaSharp;

namespace Kms.Golden.Tests;

/// <summary>
/// Headless + Skia 的金标准管线：把任意 Control 离屏渲染成 PNG，
/// 再用 SSIM 与基线比较。基线变更必须走 PR review。
/// </summary>
public static class GoldenHarness
{
    private static readonly object Gate = new();
    private static bool _initialized;

    public static void EnsureAvalonia()
    {
        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            AppBuilder.Configure<HeadlessApplication>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
            _initialized = true;
            WarmupFonts();
        }
    }

    /// <summary>
    /// 预加载 CJK 回退字体与常用字号/字重。
    /// 不做这一步时，进程内第一张页面图会使用冷启动时的合成字重，
    /// 与后续页面图的字形不一致，整页基线会随机失败。
    /// </summary>
    private static void WarmupFonts()
    {
        StackPanel panel = new() { Spacing = 2 };
        string[] samples = ["今日概览", "键鼠统计", "输入构成", "3.85k", "TODAY / LIVE DECK", "应用 Top 6"];
        foreach (double size in new[] { 9d, 10, 11, 12, 13, 18, 26 })
        {
            foreach (FontWeight weight in new[] { FontWeight.Normal, FontWeight.SemiBold, FontWeight.Bold })
            {
                panel.Children.Add(new TextBlock
                {
                    Text = string.Join(" ", samples),
                    FontSize = size,
                    FontWeight = weight,
                    Foreground = Brushes.White
                });
            }
        }

        Window warmup = new()
        {
            Width = 760,
            Height = 900,
            WindowDecorations = WindowDecorations.None,
            Background = new SolidColorBrush(Colors.Black),
            Content = panel
        };
        TextOptions.SetTextRenderingMode(warmup, TextRenderingMode.Antialias);
        warmup.Show();
        Dispatcher.UIThread.RunJobs();
        warmup.CaptureRenderedFrame();
        warmup.Content = null;
        warmup.Close();
    }

    public static byte[] Render(Control control, int width, int height, double dpi = 96)
    {
        EnsureAvalonia();

        control.Measure(new Size(width, height));
        control.Arrange(new Rect(0, 0, width, height));

        int pixelWidth = Math.Max(1, (int)Math.Round(width * dpi / 96.0));
        int pixelHeight = Math.Max(1, (int)Math.Round(height * dpi / 96.0));
        RenderTargetBitmap bitmap = new(new PixelSize(pixelWidth, pixelHeight), new Vector(dpi, dpi));
        bitmap.Render(control);

        using MemoryStream stream = new();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    /// <summary>
    /// 把控件挂进真实的 Headless <see cref="Window"/> 再渲染。
    /// UserControl / ScrollViewer / Button 等模板控件只有在 TopLevel 里才会应用模板；
    /// 直接 Measure/Arrange 会得到一张只有背景的空图。
    /// </summary>
    public static byte[] RenderHosted(Control control, int width, int height, double dpi = 96)
    {
        EnsureAvalonia();

        double scaling = Math.Max(0.1, dpi / 96.0);
        Window window = new()
        {
            Width = width,
            Height = height,
            CanResize = false,
            WindowDecorations = WindowDecorations.None,
            Background = new SolidColorBrush(ThemeManager.Current.Background),
            Content = control
        };

        // 金标准固定用灰度 AA：ClearType 次像素彩边会随进程/机器抖动，
        // 让整页基线的 SSIM 变成假失败；真实窗口仍按系统设置渲染。
        TextOptions.SetTextRenderingMode(window, TextRenderingMode.Antialias);

        try
        {
            window.SetRenderScaling(scaling);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return CaptureFrame(window);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    /// <summary>截取已经设置好尺寸的顶层窗口（悬浮窗等），并固定灰度 AA。</summary>
    public static byte[] CaptureWindow(Window window, double dpi = 96)
    {
        EnsureAvalonia();
        TextOptions.SetTextRenderingMode(window, TextRenderingMode.Antialias);
        window.SetRenderScaling(Math.Max(0.1, dpi / 96.0));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        byte[] png = CaptureFrame(window);
        window.Close();
        return png;
    }

    private static byte[] CaptureFrame(Window window)
    {
        Bitmap frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("Headless 渲染未产生帧。");
        using MemoryStream stream = new();
        frame.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    /// <summary>把 PNG 转成灰度（BT.601），供 SSIM 使用。</summary>
    public static byte[] Grayscale(byte[] png, out int width, out int height)
    {
        using SKBitmap bitmap = SKBitmap.Decode(png)
            ?? throw new InvalidOperationException("无法解码 PNG。");

        width = bitmap.Width;
        height = bitmap.Height;
        byte[] gray = new byte[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                SKColor color = bitmap.GetPixel(x, y);
                gray[y * width + x] = (byte)((color.Red * 299 + color.Green * 587 + color.Blue * 114) / 1000);
            }
        }

        return gray;
    }

    public static double Ssim(byte[] expectedPng, byte[] actualPng)
    {
        byte[] expected = Grayscale(expectedPng, out int w1, out int h1);
        byte[] actual = Grayscale(actualPng, out int w2, out int h2);
        if (w1 != w2 || h1 != h2)
        {
            return 0;
        }

        return ImageMetrics.Ssim(expected, actual, w1, h1);
    }

    public static string BaselineDirectory
    {
        get
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "KeyMouseStats.slnx")))
                {
                    return Path.Combine(directory.FullName, "tests", "Baselines");
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("找不到仓库根目录。");
        }
    }

    /// <summary>
    /// 与 <c>tests/Baselines/&lt;name&gt;.png</c> 比较；基线缺失时生成并失败，
    /// 强制走一次人工 review，防止悄悄改外观。
    /// </summary>
    public static void AssertMatches(
        string name,
        Control control,
        int width,
        int height,
        double dpi = 96,
        double minSsim = 0.98,
        bool hostInWindow = false)
    {
        byte[] actual = hostInWindow
            ? RenderHosted(control, width, height, dpi)
            : Render(control, width, height, dpi);
        AssertPng(name, actual, minSsim);
    }

    /// <summary>直接比较已捕获的 PNG（悬浮窗等自己管理 Window 的场景）。</summary>
    public static void AssertPng(string name, byte[] actual, double minSsim = 0.98)
    {
        if (string.Equals(Environment.GetEnvironmentVariable("KMS_DUMP_GOLDEN"), "1", StringComparison.Ordinal))
        {
            File.WriteAllBytes(Path.Combine(Path.GetTempPath(), name + "-actual.png"), actual);
        }
        Directory.CreateDirectory(BaselineDirectory);
        string path = Path.Combine(BaselineDirectory, name + ".png");
        bool update = string.Equals(
            Environment.GetEnvironmentVariable("KMS_UPDATE_GOLDEN"),
            "1",
            StringComparison.Ordinal);

        if (!File.Exists(path) || update)
        {
            File.WriteAllBytes(path, actual);
            if (update)
            {
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"基线不存在，已生成 {path}。请人工确认外观后提交基线，再重跑本测试。");
        }

        byte[] expected = File.ReadAllBytes(path);
        double ssim = Ssim(expected, actual);
        if (ssim < minSsim)
        {
            throw new Xunit.Sdk.XunitException(
                $"golden 不匹配：{name} SSIM={ssim:0.0000} < {minSsim:0.00}。");
        }
    }

    private sealed class HeadlessApplication : Application
    {
        public override void Initialize()
        {
            Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
        }
    }
}
