using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Kms.App.Platform;

/// <summary>后台提取出的原始 BGRA 图标像素；Avalonia 位图由 UI 线程创建。</summary>
internal readonly record struct AppIconPixels(
    int Width,
    int Height,
    int Stride,
    byte[] Pixels);

/// <summary>
/// exe 关联图标读取器。同步 <see cref="Get"/> 只在页面构建阶段调用；
/// <see cref="ExtractPixelsUncached"/> 供后台提取原始 BGRA 像素，
/// <see cref="CreateBitmap"/> 必须在 UI 线程把像素转换为 Avalonia 位图。
/// Render 热路径不读盘，取不到时返回 null，由页面回退到首字母色块。
/// </summary>
internal static class AppIconLoader
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;
    private const uint DibRgbColors = 0;
    private const int BiRgb = 0;

    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheGate = new();

    /// <summary>测试/无桌面环境可关闭，生产默认开启。</summary>
    public static bool Enabled { get; set; } = true;

    public static Bitmap? Get(string? path)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string trimmed = path.Trim().Trim('"');
        if (trimmed.Length == 0)
        {
            return null;
        }

        lock (CacheGate)
        {
            if (Cache.TryGetValue(trimmed, out Bitmap? cached))
            {
                return cached;
            }
        }

        AppIconPixels? pixels = ExtractPixelsUncached(trimmed);
        Bitmap? icon = pixels is { } value ? CreateBitmap(value) : null;
        lock (CacheGate)
        {
            Cache[trimmed] = icon;
        }

        return icon;
    }

    /// <summary>
    /// 绕过静态缓存提取原始像素。可在后台 Task 中调用，不创建 Avalonia 位图。
    /// </summary>
    internal static AppIconPixels? ExtractPixelsUncached(string? path)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string trimmed = path.Trim().Trim('"');
        if (trimmed.Length == 0)
        {
            return null;
        }

        try
        {
            string? resolved = ResolvePath(trimmed);
            return resolved is null ? null : ExtractPixels(resolved);
        }
        catch (Exception error)
        {
            Debug.WriteLine($"AppIconLoader: 提取图标失败 {trimmed}: {error.Message}");
            return null;
        }
    }

    /// <summary>必须在 UI 线程调用：把后台提取的 BGRA 像素转换为 Avalonia 位图。</summary>
    internal static Bitmap? CreateBitmap(AppIconPixels pixels)
    {
        if (pixels.Pixels.Length < pixels.Stride * pixels.Height)
        {
            return null;
        }

        try
        {
            WriteableBitmap result = new(
                new PixelSize(pixels.Width, pixels.Height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Unpremul);
            using (ILockedFramebuffer frame = result.Lock())
            {
                for (int y = 0; y < pixels.Height; y++)
                {
                    Marshal.Copy(
                        pixels.Pixels,
                        y * pixels.Stride,
                        IntPtr.Add(frame.Address, y * frame.RowBytes),
                        pixels.Stride);
                }
            }

            return result;
        }
        catch (Exception error)
        {
            Debug.WriteLine($"AppIconLoader: 创建位图失败 {pixels.Width}x{pixels.Height}: {error.Message}");
            return null;
        }
    }

    public static void Clear()
    {
        lock (CacheGate)
        {
            foreach (Bitmap? bitmap in Cache.Values)
            {
                bitmap?.Dispose();
            }

            Cache.Clear();
        }
    }

    private static string? ResolvePath(string path)
    {
        if (File.Exists(path))
        {
            return Path.GetFullPath(path);
        }

        // 旧数据/Rime 日志有时只保存进程名。优先系统目录，再尝试同名运行进程。
        if (path.Contains('\\') || path.Contains('/'))
        {
            return null;
        }

        string name = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        string system = Path.Combine(Environment.SystemDirectory, name + ".exe");
        if (File.Exists(system))
        {
            return system;
        }

        try
        {
            foreach (Process process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try
                    {
                        string? file = process.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(file) && File.Exists(file))
                        {
                            return file;
                        }
                    }
                    catch
                    {
                        // 权限不足或进程已退出：继续找下一个。
                    }
                }
            }
        }
        catch
        {
            // 进程列表不可用时只回退字母块。
        }

        return null;
    }

    private static AppIconPixels? ExtractPixels(string path)
    {
        ShFileInfo info = default;
        IntPtr shellIcon = SHGetFileInfo(
            path,
            0,
            ref info,
            (uint)Marshal.SizeOf<ShFileInfo>(),
            ShgfiIcon | ShgfiLargeIcon);
        IntPtr icon = info.hIcon;
        if (shellIcon == IntPtr.Zero || icon == IntPtr.Zero)
        {
            // 极少数 shell 扩展异常时回退到 Win32 图标索引。
            uint count = ExtractIconEx(path, 0, out IntPtr largeIcon, out IntPtr smallIcon, 1);
            if (count == 0)
            {
                return null;
            }

            if (largeIcon != IntPtr.Zero)
            {
                icon = largeIcon;
                if (smallIcon != IntPtr.Zero)
                {
                    DestroyIcon(smallIcon);
                }
            }
            else if (smallIcon != IntPtr.Zero)
            {
                icon = smallIcon;
            }
            else
            {
                return null;
            }
        }

        IntPtr colorBitmap = IntPtr.Zero;
        IntPtr maskBitmap = IntPtr.Zero;
        IntPtr dc = IntPtr.Zero;
        try
        {
            if (!GetIconInfo(icon, out IconInfo iconInfo))
            {
                return null;
            }

            colorBitmap = iconInfo.hbmColor;
            maskBitmap = iconInfo.hbmMask;
            if (colorBitmap == IntPtr.Zero)
            {
                return null;
            }

            if (GetObject(colorBitmap, Marshal.SizeOf<NativeBitmap>(), out NativeBitmap bitmap) == 0
                || bitmap.bmWidth <= 0
                || bitmap.bmHeight <= 0
                || bitmap.bmWidth > 256
                || bitmap.bmHeight > 256)
            {
                return null;
            }

            int width = bitmap.bmWidth;
            int height = bitmap.bmHeight;
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];

            dc = GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero)
            {
                return null;
            }

            BitmapInfo bitmapInfo = CreateBitmapInfo(width, height, 32);
            if (GetDIBits(dc, colorBitmap, 0, (uint)height, pixels, ref bitmapInfo, DibRgbColors) == 0)
            {
                return null;
            }

            bool hasAlpha = false;
            for (int i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] != 0)
                {
                    hasAlpha = true;
                    break;
                }
            }

            if (!hasAlpha)
            {
                ApplyMaskAlpha(dc, maskBitmap, pixels, width, height);
            }

            return new AppIconPixels(width, height, stride, pixels);
        }
        finally
        {
            if (maskBitmap != IntPtr.Zero)
            {
                DeleteObject(maskBitmap);
            }

            if (colorBitmap != IntPtr.Zero)
            {
                DeleteObject(colorBitmap);
            }

            if (dc != IntPtr.Zero)
            {
                ReleaseDC(IntPtr.Zero, dc);
            }

            if (icon != IntPtr.Zero)
            {
                DestroyIcon(icon);
            }
        }
    }

    private static void ApplyMaskAlpha(IntPtr dc, IntPtr maskBitmap, byte[] pixels, int width, int height)
    {
        if (maskBitmap == IntPtr.Zero)
        {
            for (int i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 255;
            }

            return;
        }

        int maskStride = ((width + 31) / 32) * 4;
        byte[] mask = new byte[maskStride * height];
        BitmapInfo maskInfo = CreateBitmapInfo(width, height, 1);
        if (GetDIBits(dc, maskBitmap, 0, (uint)height, mask, ref maskInfo, DibRgbColors) == 0)
        {
            for (int i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 255;
            }

            return;
        }

        for (int y = 0; y < height; y++)
        {
            int maskRow = y * maskStride;
            int pixelRow = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                bool transparent = (mask[maskRow + x / 8] & (0x80 >> (x & 7))) != 0;
                pixels[pixelRow + x * 4 + 3] = transparent ? (byte)0 : (byte)255;
            }
        }
    }

    private static BitmapInfo CreateBitmapInfo(int width, int height, ushort bitCount) => new()
    {
        bmiHeader = new BitmapInfoHeader
        {
            biSize = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
            biWidth = width,
            biHeight = -height,
            biPlanes = 1,
            biBitCount = bitCount,
            biCompression = BiRgb
        }
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string? szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string? szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool fIcon;

        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmap
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader bmiHeader;
        public uint bmiColors;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref ShFileInfo psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(
        string lpszFile,
        int nIconIndex,
        out IntPtr phiconLarge,
        out IntPtr phiconSmall,
        uint nIcons);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIconInfo(IntPtr hIcon, out IconInfo piconinfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll", EntryPoint = "GetObjectW", CharSet = CharSet.Unicode)]
    private static extern int GetObject(IntPtr hgdiobj, int cbBuffer, out NativeBitmap lpvObject);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(
        IntPtr hdc,
        IntPtr hbm,
        uint start,
        uint cLines,
        byte[] lpvBits,
        ref BitmapInfo lpbmi,
        uint usage);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);
}
