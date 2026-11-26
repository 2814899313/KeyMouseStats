using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace Kms.App.Reports;

/// <summary>把任意 Control 以 DIP 尺寸 × 缩放渲染成 PNG，并走系统文件选择器保存。</summary>
internal static class PosterExporter
{
    /// <summary>把控件按 DIP 尺寸 × 缩放渲染成位图；调用方负责 Dispose。</summary>
    public static RenderTargetBitmap? Render(Control surface, int width, int height, double scale)
    {
        if (surface is null)
        {
            return null;
        }

        surface.Width = width;
        surface.Height = height;
        surface.Measure(new Size(width, height));
        surface.Arrange(new Rect(0, 0, width, height));

        int pixelWidth = (int)Math.Round(width * scale);
        int pixelHeight = (int)Math.Round(height * scale);
        RenderTargetBitmap bitmap = new(
            new PixelSize(pixelWidth, pixelHeight),
            new Vector(96 * scale, 96 * scale));
        bitmap.Render(surface);
        return bitmap;
    }

    public static async Task<string?> SavePngAsync(
        Control surface,
        int width,
        int height,
        double scale,
        string suggestedName,
        TopLevel? owner = null)
    {
        if (surface is null)
        {
            return null;
        }

        TopLevel? topLevel = owner ?? TopLevel.GetTopLevel(surface);
        if (topLevel is null)
        {
            return null;
        }

        using RenderTargetBitmap? bitmap = Render(surface, width, height, scale);
        if (bitmap is null)
        {
            return null;
        }

        IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "导出海报",
                SuggestedFileName = suggestedName,
                DefaultExtension = "png",
                FileTypeChoices =
                [
                    new FilePickerFileType("PNG 图片") { Patterns = ["*.png"] }
                ]
            });

        if (file is null)
        {
            return null;
        }

        await using Stream stream = await file.OpenWriteAsync();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return file.Path.LocalPath;
    }
}
