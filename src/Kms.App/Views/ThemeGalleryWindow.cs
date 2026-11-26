using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Kms.App.Theming;
using Kms.Render.Theming;
using Kms.Render.Tokens;

namespace Kms.App.Views;

/// <summary>V3 皮肤/主题画廊：内置美术主题 + 已保存的自定义壁纸。</summary>
internal sealed class ThemeGalleryWindow : Window
{
    private readonly Action<int> _applyTheme;
    private readonly Action<string, string> _applyCustom;
    private readonly Func<Task<(string Path, string Name)?>> _import;
    private readonly List<Bitmap> _previews = [];
    private readonly int _currentTheme;
    private readonly string? _customPath;

    public ThemeGalleryWindow(
        int currentTheme,
        string? customPath,
        Action<int> applyTheme,
        Action<string, string> applyCustom,
        Func<Task<(string Path, string Name)?>> import)
    {
        _currentTheme = currentTheme;
        _customPath = customPath;
        _applyTheme = applyTheme;
        _applyCustom = applyCustom;
        _import = import;

        ThemeTokens theme = ThemeManager.Current;
        Title = "外观 · 皮肤与主题";
        Width = 840;
        Height = 660;
        MinWidth = 640;
        MinHeight = 480;
        Background = new SolidColorBrush(theme.Background);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        StackPanel body = new() { Spacing = 12, Margin = new Thickness(22, 18) };
        body.Children.Add(new TextBlock
        {
            Text = "外观",
            FontSize = 26,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(theme.Text)
        });
        body.Children.Add(new TextBlock
        {
            Text = "内置美术主题与 V3 自定义壁纸。选择后立即应用到主面板。",
            FontSize = 11,
            Foreground = new SolidColorBrush(theme.Muted)
        });

        body.Children.Add(Section("内置主题", ThemeCards(theme)));
        body.Children.Add(Section("我的皮肤", CustomCards(theme)));

        Content = new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };

        Closed += (_, _) =>
        {
            foreach (Bitmap preview in _previews)
            {
                preview.Dispose();
            }

            _previews.Clear();
        };
    }

    private Control Section(string title, Control content)
    {
        ThemeTokens theme = ThemeManager.Current;
        StackPanel stack = new() { Spacing = 8 };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(theme.Text)
        });
        stack.Children.Add(content);
        return stack;
    }

    private Control ThemeCards(ThemeTokens theme)
    {
        WrapPanel wrap = new() { Orientation = Orientation.Horizontal };
        foreach (ThemeTokens candidate in ThemeCatalog.All)
        {
            Bitmap? preview = ThemeArtwork.IsArtTheme(candidate.Id)
                ? ThemeArtwork.Load(candidate.Id, 0)
                : null;
            if (preview is not null)
            {
                _previews.Add(preview);
            }
            Border card = CardFrame(candidate.Id == _currentTheme, ThemeArtwork.IsArtTheme(candidate.Id)
                ? "美术主题"
                : "颜色主题");

            Grid visual = new();
            if (preview is not null)
            {
                visual.Children.Add(new Image
                {
                    Source = preview,
                    Stretch = Stretch.UniformToFill
                });
            }
            else
            {
                visual.Children.Add(new Border
                {
                    Background = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                        GradientStops =
                        {
                            new GradientStop(candidate.Background, 0),
                            new GradientStop(candidate.Accent, 1)
                        }
                    }
                });
            }

            visual.Children.Add(new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(0, 0, 0, 0), 0),
                        new GradientStop(Color.FromArgb(210, 0, 0, 0), 1)
                    }
                },
                Padding = new Thickness(10, 16, 10, 8),
                Child = new StackPanel
                {
                    Spacing = 1,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = ShortName(candidate),
                            FontSize = 12,
                            FontWeight = FontWeight.SemiBold,
                            Foreground = Brushes.White,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        },
                        new TextBlock
                        {
                            Text = candidate.Name,
                            FontSize = 9,
                            Foreground = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)),
                            TextTrimming = TextTrimming.CharacterEllipsis
                        }
                    }
                }
            });

            card.Child = visual;
            Button button = CardButton(card);
            int id = candidate.Id;
            button.Click += (_, _) =>
            {
                _applyTheme(id);
                Close();
            };
            ToolTip.SetTip(button, candidate.Name);
            wrap.Children.Add(button);
        }

        return wrap;
    }

    private Control CustomCards(ThemeTokens theme)
    {
        WrapPanel wrap = new() { Orientation = Orientation.Horizontal };
        List<ThemeArtwork.SkinInfo> skins = ThemeArtwork.LoadV3Skins();
        foreach (ThemeArtwork.SkinInfo skin in skins)
        {
            Bitmap? preview = LoadPreview(skin.ThumbPath, skin.ImagePath);
            if (preview is not null)
            {
                _previews.Add(preview);
            }
            Border card = CardFrame(
                !string.IsNullOrEmpty(_customPath)
                && string.Equals(_customPath, skin.ImagePath, StringComparison.OrdinalIgnoreCase),
                "自定义壁纸");
            Grid visual = new();
            if (preview is not null)
            {
                visual.Children.Add(new Image { Source = preview, Stretch = Stretch.UniformToFill });
            }
            else
            {
                visual.Children.Add(new Border { Background = new SolidColorBrush(theme.Raised) });
            }

            visual.Children.Add(new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(0, 0, 0, 0), 0),
                        new GradientStop(Color.FromArgb(210, 0, 0, 0), 1)
                    }
                },
                Padding = new Thickness(10, 16, 10, 8),
                Child = new TextBlock
                {
                    Text = skin.Name,
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brushes.White,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            });

            card.Child = visual;
            Button button = CardButton(card);
            button.Click += (_, _) =>
            {
                _applyCustom(skin.ImagePath, skin.Name);
                Close();
            };
            ToolTip.SetTip(button, skin.ImagePath);
            wrap.Children.Add(button);
        }

        Border import = CardFrame(false, "导入");
        import.Child = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 5,
            Children =
            {
                new TextBlock
                {
                    Text = "+",
                    FontSize = 28,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = new SolidColorBrush(theme.Accent)
                },
                new TextBlock
                {
                    Text = "导入壁纸…",
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = new SolidColorBrush(theme.Muted)
                }
            }
        };
        Button importButton = CardButton(import);
        importButton.Click += async (_, _) =>
        {
            (string Path, string Name)? result = await _import();
            if (result is { } selected)
            {
                _applyCustom(selected.Path, selected.Name);
                Close();
            }
        };
        wrap.Children.Add(importButton);
        return wrap;
    }

    private static Bitmap? LoadPreview(string thumbPath, string imagePath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(thumbPath) && File.Exists(thumbPath))
            {
                return new Bitmap(thumbPath);
            }

            if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
            {
                return new Bitmap(imagePath);
            }
        }
        catch
        {
        }

        return null;
    }

    private static Border CardFrame(bool selected, string badge)
    {
        ThemeTokens theme = ThemeManager.Current;
        return new Border
        {
            Width = 156,
            Height = 108,
            Margin = new Thickness(0, 0, 10, 10),
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            BorderBrush = new SolidColorBrush(selected
                ? theme.Accent
                : Color.FromArgb(90, theme.Line.R, theme.Line.G, theme.Line.B)),
            BorderThickness = new Thickness(selected ? 2 : 1),
            Background = new SolidColorBrush(theme.Card)
        };
    }

    private static Button CardButton(Control content) => new()
    {
        Content = content,
        Padding = new Thickness(0),
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch,
        CornerRadius = new CornerRadius(10)
    };

    private static string ShortName(ThemeTokens theme) => theme.Id switch
    {
        2 => "灵光森林",
        5 => "星愿织梦",
        6 => "战术终端",
        7 => "档案",
        8 => "方块世界",
        9 => "幻彩牌桌",
        10 => "碧血丹心",
        11 => "长途之路",
        12 => "琴心剑魄",
        _ => theme.Name
    };
}
