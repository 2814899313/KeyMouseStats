using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Kms.Render.Theming;

namespace Kms.App.Pages;

/// <summary>M4 迁移中的占位页：保留标题与副标题，避免九页签空白。</summary>
public sealed class PlaceholderPage : UserControl
{
    public PlaceholderPage(string title, string subtitle)
    {
        Kms.Render.Tokens.ThemeTokens theme = ThemeManager.Current;

        Content = new StackPanel
        {
            Margin = new Thickness(30, 26, 30, 26),
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    FontSize = 29,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(theme.Text)
                },
                new TextBlock
                {
                    Text = subtitle,
                    FontSize = 13,
                    Foreground = new SolidColorBrush(theme.Muted)
                },
                new Border
                {
                    Margin = new Thickness(0, 18, 0, 0),
                    Padding = new Thickness(24),
                    CornerRadius = new CornerRadius(14),
                    Background = new SolidColorBrush(theme.Card),
                    BorderBrush = new SolidColorBrush(theme.Line),
                    BorderThickness = new Thickness(1),
                    Child = new TextBlock
                    {
                        Text = "该页将在 M4 逐页迁移。当前已完成：主题令牌、12 个图表原语、文本体系、"
                             + "透明窗 spike、Hours 数据口径。",
                        FontSize = 13,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(theme.Muted)
                    }
                }
            }
        };
    }
}
