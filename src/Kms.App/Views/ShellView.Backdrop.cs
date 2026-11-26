using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Kms.App.Theming;
using Kms.Render.Theming;
using Kms.Render.Tokens;

namespace Kms.App.Views;

public sealed partial class ShellView
{
    private void UpdateBackdrop(int page)
    {
        Bitmap? next = ThemeArtwork.LoadCustom(_customThemePath);
        bool custom = next is not null;
        ThemeArtwork.CustomActive = custom;
        if (next is null)
        {
            next = ThemeArtwork.Load(ThemeManager.Current.Id, page);
        }

        Bitmap? previous = _backdrop;
        _backdrop = next;
        ThemeBackdrop.Source = next;
        ThemeBackdrop.IsVisible = next is not null;
        ThemeScrim.IsVisible = next is not null;

        ThemeTokens theme = ThemeManager.Current;
        Color shade = theme.Background;
        ThemeScrim.Background = new LinearGradientBrush
        {
            StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
            EndPoint = new Avalonia.RelativePoint(0, 1, Avalonia.RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(custom ? (byte)170 : (byte)190, shade.R, shade.G, shade.B), 0),
                new GradientStop(Color.FromArgb(custom ? (byte)205 : (byte)215, shade.R, shade.G, shade.B), 0.58),
                new GradientStop(Color.FromArgb(custom ? (byte)232 : (byte)240, shade.R, shade.G, shade.B), 1)
            }
        };

        if (previous is not null)
        {
            previous.Dispose();
        }
    }

    private void OpenThemeGallery()
    {
        ThemeGalleryWindow gallery = new(
            ThemeManager.Current.Id,
            _customThemePath,
            ApplyTheme,
            ApplyCustomTheme,
            ImportCustomThemeAsync);
        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            gallery.Show(owner);
        }
        else
        {
            gallery.Show();
        }
    }

    private void ApplyCustomTheme(string path, string name)
    {
        _customThemePath = path;
        _customThemeName = string.IsNullOrWhiteSpace(name) ? "自定义皮肤" : name;
        _loaded.Data.Settings["custom_theme_path"] = path;
        _loaded.Data.Settings["custom_theme_name"] = _customThemeName;
        ApplyCustomPalette();
        BuildThemeSwatches();
        ShowPage(_current);
        UpdateStatus();
    }

    private void ApplyCustomPalette()
    {
        if (string.IsNullOrWhiteSpace(_customThemePath)
            || Application.Current is not { } application)
        {
            return;
        }

        ThemeArtwork.SkinInfo? skin = ThemeArtwork.LoadV3Skins()
            .FirstOrDefault(item => string.Equals(
                item.ImagePath,
                _customThemePath,
                StringComparison.OrdinalIgnoreCase));
        if (skin is null)
        {
            return;
        }

        ThemeTokens tokens = ThemePaletteBuilder.Build(
            _customThemeName,
            skin.Background,
            skin.Accent,
            skin.Text,
            ThemeManager.Current);
        ThemeManager.ApplyTokens(application, tokens);
    }

    private async Task<(string Path, string Name)?> ImportCustomThemeAsync()
    {
        TopLevel? top = TopLevel.GetTopLevel(this);
        if (top is null)
        {
            return null;
        }

        IReadOnlyList<IStorageFile> files = await top.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "选择自定义皮肤壁纸",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("图片")
                    {
                        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp"]
                    }
                ]
            });
        IStorageFile? file = files.FirstOrDefault();
        if (file is null)
        {
            return null;
        }

        try
        {
            string directory = System.IO.Path.Combine(DataPaths.V4Root, "themes");
            System.IO.Directory.CreateDirectory(directory);
            string extension = System.IO.Path.GetExtension(file.Name);
            if (string.IsNullOrEmpty(extension))
            {
                extension = ".jpg";
            }

            string target = System.IO.Path.Combine(directory, $"custom-{DateTime.Now:yyyyMMddHHmmss}{extension}");
            await using (System.IO.Stream source = await file.OpenReadAsync())
            await using (System.IO.FileStream destination = System.IO.File.Create(target))
            {
                await source.CopyToAsync(destination);
            }

            return (target, System.IO.Path.GetFileNameWithoutExtension(file.Name));
        }
        catch
        {
            return null;
        }
    }
}
