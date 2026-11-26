using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Kms.App.Overlays;
using Kms.Render.Theming;
using Kms.Render.Tokens;
using AvaloniaText = Avalonia.Controls.TextBlock;

namespace Kms.App.Widgets;

/// <summary>M5 / T5.3 休息提示首版：底部滑出卡片，15 秒自动淡出。</summary>
internal sealed class FocusBreakToast : OverlayWindow
{
    private readonly DispatcherTimer _hideTimer;
    private readonly AvaloniaText _title;
    private readonly AvaloniaText _body;

    public FocusBreakToast()
    {
        Width = 420;
        Height = 132;
        Title = "休息提示";
        ThemeTokens theme = ThemeManager.Current;

        Button later = Button("5 分钟后");
        later.Click += (_, _) => Hide();
        Button ignore = Button("忽略");
        ignore.Click += (_, _) => Hide();
        Button close = Button("关闭");
        close.Click += (_, _) => Hide();

        _title = new AvaloniaText
        {
            Text = "该起来活动一下了",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(theme.Text)
        };
        _body = new AvaloniaText
        {
            Text = "已经连续使用一段时间，站起来走走，看看远处。",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 36,
            Foreground = new SolidColorBrush(theme.Muted)
        };

        Content = new Border
        {
            Background = new SolidColorBrush(theme.Card),
            BorderBrush = new SolidColorBrush(theme.Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(18, 14),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    _title,
                    _body,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children = { later, ignore, close }
                    }
                }
            }
        };

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };
    }

    public void ShowToast(string? title = null, string? body = null)
    {
        _title.Text = string.IsNullOrWhiteSpace(title) ? "该起来活动一下了" : title;
        _body.Text = string.IsNullOrWhiteSpace(body)
            ? "已经连续使用一段时间，站起来走走，看看远处。"
            : body;
        Show();
        if (Screens.Primary is { } screen)
        {
            PixelRect area = screen.WorkingArea;
            Position = new PixelPoint(
                area.X + (area.Width - (int)Width) / 2,
                area.Bottom - (int)Height - 48);
        }

        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private Button Button(string text)
    {
        ThemeTokens theme = ThemeManager.Current;
        return new Button
        {
            Content = text,
            Padding = new Thickness(12, 5),
            CornerRadius = new CornerRadius(7),
            FontSize = 12,
            Background = new SolidColorBrush(theme.Raised),
            Foreground = new SolidColorBrush(theme.Text)
        };
    }
}
