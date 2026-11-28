using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Kms.Core.Model;
using Kms.App.Theming;
using Kms.Render.Primitives;
using Kms.Render.Theming;
using Kms.Render.Tokens;
using AvaloniaText = Avalonia.Controls.TextBlock;
using PrimitiveText = Kms.Render.Primitives.TextBlock;

namespace Kms.App.Pages;

/// <summary>
/// v4 页面视觉基座：统一的页头、卡片、指标、胶囊、排行条与迷你图。
/// 所有颜色来自当前 <see cref="ThemeTokens"/>，13 套主题切换后由页面重建生效。
/// </summary>
internal static class PageKit
{
    public static ThemeTokens T => ThemeManager.Current;

    /// <summary>美术主题下卡片保留一点透明度，让皮肤原画透上来；纯色主题保持不透明。</summary>
    public static Color CardFill =>
        ThemeArtwork.IsArtTheme(T.Id) || ThemeArtwork.CustomActive ? Alpha(T.Card, 0.88) : T.Card;

    public static IBrush CardBrush => Brush(CardFill);

    public static double R(double min = 10, double max = 18) =>
        Math.Clamp(T.Radius, min, max);

    /// <summary>矮视口：沿用既有紧凑降级。</summary>
    public static bool IsCompact(double height) => height > 0 && height < 720;

    /// <summary>高视口/全屏：增加列表行数并放大主图，避免卡片内大面积空白。</summary>
    public static bool IsTall(double height) => height >= 880;

    public static Color Alpha(Color color, double opacity)
    {
        double a = Math.Clamp(opacity, 0, 1);
        return Color.FromArgb((byte)Math.Round(a * 255), color.R, color.G, color.B);
    }

    public static IBrush Brush(Color color) => new SolidColorBrush(color);

    public static IBrush Gradient(Color from, Color to, double angle = 135)
    {
        (double sx, double sy, double ex, double ey) = angle switch
        {
            <= 45 => (0d, 0d, 1d, 0d),
            <= 135 => (0d, 0d, 1d, 1d),
            <= 225 => (1d, 0d, 0d, 1d),
            <= 315 => (1d, 1d, 0d, 0d),
            _ => (0d, 1d, 1d, 0d)
        };

        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(sx, sy, RelativeUnit.Relative),
            EndPoint = new RelativePoint(ex, ey, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(from, 0),
                new GradientStop(to, 1)
            }
        };
    }

    public static BoxShadows Shadow(double blur = 30, double y = 12, double alpha = 0.22) =>
        new(new BoxShadow
        {
            OffsetX = 0,
            OffsetY = y,
            Blur = blur,
            Color = Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), 0, 0, 0)
        });

    public static AvaloniaText Label(
        string text,
        double size = 13,
        Color? color = null,
        FontWeight? weight = null,
        double? maxWidth = null,
        TextTrimming? trimming = null) => new()
        {
            Text = text,
            FontSize = size,
            FontWeight = weight ?? FontWeight.Normal,
            Foreground = Brush(color ?? T.Text),
            TextWrapping = TextWrapping.NoWrap,
            MaxWidth = maxWidth ?? double.PositiveInfinity,
            TextTrimming = trimming ?? TextTrimming.None
        };

    public static AvaloniaText Muted(string text, double size = 11) =>
        Label(text, size, T.Muted);

    public static Control PageHeader(
        string kicker,
        string title,
        string subtitle,
        string? meta = null,
        Control? actions = null)
    {
        _ = kicker;
        StackPanel left = new() { Spacing = 2 };
        left.Children.Add(Label($"工作空间  /  {title}", 9.5, T.Muted));
        left.Children.Add(Label(title, 30, T.Text, FontWeight.Bold));
        left.Children.Add(Label(subtitle, 11, T.Muted));

        Grid grid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            VerticalAlignment = VerticalAlignment.Top
        };
        grid.Children.Add(left);

        StackPanel right = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (!string.IsNullOrEmpty(meta))
        {
            right.Children.Add(Badge(meta, T.Accent));
        }

        if (actions is not null)
        {
            right.Children.Add(actions);
        }

        if (right.Children.Count > 0)
        {
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
        }

        if (ThemeArtwork.IsArtTheme(T.Id) || ThemeArtwork.CustomActive)
        {
            return new Border
            {
                Background = Brush(Alpha(T.Card, 0.76)),
                BorderBrush = Brush(Alpha(T.Line, 0.45)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(R(10, 14)),
                Padding = new Thickness(12, 8),
                Child = grid
            };
        }

        return grid;
    }

    public static Control DateNav(DateTime date, DateTime min, DateTime max, Action<DateTime> onChange)
    {
        Button Nav(string text, DateTime target, bool enabled)
        {
            Button button = new()
            {
                Content = text,
                FontSize = 12,
                Padding = new Thickness(9, 5),
                CornerRadius = new CornerRadius(8),
                Background = Brush(Alpha(T.Raised, 0.8)),
                Foreground = Brush(T.Muted),
                BorderBrush = Brush(Alpha(T.Line, 0.6)),
                BorderThickness = new Thickness(1),
                IsEnabled = enabled
            };
            button.Click += (_, _) => onChange(target);
            return button;
        }

        DateTime day = date.Date;
        StackPanel stack = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        stack.Children.Add(Nav("‹", day.AddDays(-1), day > min.Date));
        stack.Children.Add(new Border
        {
            Background = Brush(Alpha(T.Raised, 0.8)),
            BorderBrush = Brush(Alpha(T.Accent, 0.25)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 5),
            Child = Label(day.ToString("yyyy-MM-dd"), 11, T.Text, FontWeight.SemiBold)
        });
        stack.Children.Add(Nav("›", day.AddDays(1), day < max.Date));
        if (day != DateTime.Today)
        {
            stack.Children.Add(Nav("今天", DateTime.Today, true));
        }

        return stack;
    }

    public static Border Badge(string text, Color color, bool strong = false) => new()
    {
        Background = Brush(Alpha(color, strong ? 0.20 : 0.10)),
        BorderBrush = Brush(Alpha(color, strong ? 0.45 : 0.22)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(20),
        Padding = new Thickness(10, 4),
        HorizontalAlignment = HorizontalAlignment.Left,
        Child = Label(text, 10, color, FontWeight.SemiBold)
    };

    public static Border Card(Control child, double padding = 16, double? radius = null, bool hoverGlow = false)
    {
        Border border = new()
        {
            Background = CardBrush,
            BorderBrush = Brush(Alpha(T.Line, 0.9)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(radius ?? R(10, 14)),
            Padding = new Thickness(padding),
            BoxShadow = Shadow(16, 5, 0.12),
            Child = child
        };

        if (hoverGlow)
        {
            border.PointerEntered += (_, _) =>
            {
                border.BorderBrush = Brush(Alpha(T.Accent, 0.55));
            };
            border.PointerExited += (_, _) =>
            {
                border.BorderBrush = Brush(Alpha(T.Line, 0.9));
            };
        }

        return border;
    }

    public static Border Section(
        string title,
        string? hint,
        Control content,
        Control? trailing = null,
        Color? accent = null,
        double padding = 16)
    {
        StackPanel body = new() { Spacing = 10 };
        Grid header = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

        StackPanel titles = new() { Spacing = 2 };
        titles.Children.Add(Label(title, 16, T.Text, FontWeight.SemiBold));
        if (!string.IsNullOrEmpty(hint))
        {
            titles.Children.Add(Label(hint, 9.5, T.Muted));
        }

        header.Children.Add(titles);
        if (trailing is not null)
        {
            Grid.SetColumn(trailing, 1);
            header.Children.Add(trailing);
        }

        body.Children.Add(header);
        body.Children.Add(content);

        return Card(body, padding);
    }

    public static Control StatStrip(params (string Label, string Value, Color Color)[] items) =>
        Card(StatStripBody(items), 12, R(9, 14));

    /// <summary>指标条内容（不含外框），供 BentoCardFrame 等统一外框复用。</summary>
    public static Control StatStripBody(params (string Label, string Value, Color Color)[] items)
    {
        Grid grid = new() { ColumnSpacing = 0 };
        for (int i = 0; i < items.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            StackPanel cell = new()
            {
                Spacing = 1,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(i == 0 ? 0 : 12, 2, i == items.Length - 1 ? 0 : 12, 2)
            };
            cell.Children.Add(Label(items[i].Label, 9, T.Muted));
            cell.Children.Add(Label(items[i].Value, 18, items[i].Color, FontWeight.SemiBold, null,
                TextTrimming.CharacterEllipsis));
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);

            if (i > 0)
            {
                Border line = new()
                {
                    Width = 1,
                    Background = Brush(Alpha(T.Line, 0.55)),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 2, 0, 2)
                };
                Grid.SetColumn(line, i);
                grid.Children.Add(line);
            }
        }

        return grid;
    }

    public static Control Metric(
        string kicker,
        string value,
        string unit,
        string note,
        Color accent,
        IReadOnlyList<double>? trend = null,
        string? badge = null)
    {
        StackPanel stack = new() { Spacing = 4 };

        Grid top = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        top.Children.Add(Label(kicker, 10, T.Muted, FontWeight.SemiBold));
        if (!string.IsNullOrEmpty(badge))
        {
            AvaloniaText badgeText = Label(badge, 9, accent, FontWeight.SemiBold);
            Grid.SetColumn(badgeText, 1);
            top.Children.Add(badgeText);
        }

        stack.Children.Add(top);

        StackPanel valueRow = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        valueRow.Children.Add(Label(value, 26, T.Text, FontWeight.SemiBold, null, TextTrimming.CharacterEllipsis));
        if (!string.IsNullOrEmpty(unit))
        {
            valueRow.Children.Add(new Border
            {
                Background = Brush(Alpha(accent, 0.16)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(5, 1),
                Margin = new Thickness(0, 0, 0, 5),
                Child = Label(unit, 8, accent, FontWeight.SemiBold)
            });
        }
        stack.Children.Add(valueRow);
        stack.Children.Add(Label(note, 9, T.Muted));

        if (trend is { Count: > 1 })
        {
            stack.Children.Add(new Spark(trend, accent)
            {
                Height = 24,
                Margin = new Thickness(0, 4, 0, 0)
            });
        }
        else
        {
            stack.Children.Add(new Border
            {
                Height = 3,
                Margin = new Thickness(0, 7, 0, 0),
                CornerRadius = new CornerRadius(2),
                Background = Brush(Alpha(accent, 0.45))
            });
        }

        return Card(stack, 12, R(9, 14), hoverGlow: true);
    }

    public static Control MetricGrid(params Control[] cards)
    {
        Grid grid = new();
        for (int i = 0; i < cards.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            Grid.SetColumn(cards[i], i);
            grid.Children.Add(cards[i]);
        }

        grid.ColumnSpacing = 12;
        return grid;
    }

    public static Control StatRow(string label, string value, Color? tone = null) =>
        new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                Label(label, 11, T.Muted),
                AlignRight(Label(value, 12, tone ?? T.Text, FontWeight.SemiBold))
            }
        };

    private static Control AlignRight(Control control)
    {
        Grid.SetColumn(control, 1);
        return control;
    }

    public static Control RankRow(
        int rank,
        string name,
        string value,
        double fraction,
        Color color,
        string? sub = null,
        bool keycap = false)
    {
        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("28,Auto,*,Auto"),
            Margin = new Thickness(0, 2)
        };

        Border badge = new()
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(6),
            Background = Brush(rank <= 3 ? Alpha(color, 0.20) : Alpha(T.Line, 0.18)),
            VerticalAlignment = VerticalAlignment.Center,
            Child = Label(rank.ToString("00", CultureInfo.InvariantCulture), 9,
                rank <= 3 ? color : T.Muted, FontWeight.SemiBold)
        };
        badge.Child = new AvaloniaText
        {
            Text = rank.ToString("00", CultureInfo.InvariantCulture),
            FontSize = 9,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush(rank <= 3 ? color : T.Muted),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(badge);

        Control nameControl;
        if (keycap)
        {
            nameControl = new Border
            {
                Background = Brush(Alpha(T.Raised, 0.9)),
                BorderBrush = Brush(Alpha(T.Line, 0.9)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(7, 2),
                MinWidth = 32,
                Child = Label(name, 11, T.Text, FontWeight.SemiBold)
            };
        }
        else
        {
            nameControl = Label(name, 12, T.Text, maxWidth: 190, trimming: TextTrimming.CharacterEllipsis);
        }

        Grid.SetColumn(nameControl, 1);
        row.Children.Add(nameControl);

        Grid barArea = new()
        {
            Margin = new Thickness(10, 8, 10, 8),
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = true
        };
        barArea.Children.Add(new Border
        {
            Height = 7,
            CornerRadius = new CornerRadius(4),
            Background = Brush(Alpha(T.Line, 0.22))
        });
        Border fill = new()
        {
            Height = 7,
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 2,
            Background = Gradient(Alpha(color, 0.55), color, 0),
            BoxShadow = Shadow(10, 0, 0.10)
        };
        barArea.Children.Add(fill);
        barArea.SizeChanged += (_, _) =>
        {
            fill.Width = Math.Max(2, barArea.Bounds.Width * Math.Clamp(fraction, 0, 1));
        };
        Grid.SetColumn(barArea, 2);
        row.Children.Add(barArea);

        StackPanel right = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        right.Children.Add(Label(value, 11, T.Text, FontWeight.SemiBold));
        if (!string.IsNullOrEmpty(sub))
        {
            right.Children.Add(Label(sub, 9, T.Muted));
        }

        Grid.SetColumn(right, 3);
        row.Children.Add(right);
        return row;
    }

    public static Control Divider() => new Border
    {
        Height = 1,
        Background = Brush(Alpha(T.Line, 0.55))
    };

    public static Control LegendDot(Color color, string text, string? value = null)
    {
        Grid grid = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        grid.Children.Add(new Border
        {
            Width = 7,
            Height = 7,
            CornerRadius = new CornerRadius(4),
            Background = Brush(color),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 1, 6, 0)
        });
        Control label = Label(text, 10, T.Muted);
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        if (!string.IsNullOrEmpty(value))
        {
            Control amount = Label(value, 10, T.Text, FontWeight.SemiBold);
            Grid.SetColumn(amount, 2);
            grid.Children.Add(amount);
        }

        return grid;
    }

    public static Control Empty(string text) => new StackPanel
    {
        Spacing = 4,
        Children =
        {
            Label("∅", 24, Alpha(T.Muted, 0.55), FontWeight.Light),
            Muted(text, 11)
        }
    };

    public static Control Legend(params (Color Color, string Text)[] items)
    {
        WrapPanel panel = new() { Orientation = Orientation.Horizontal };
        foreach ((Color color, string text) in items)
        {
            Border chip = new()
            {
                Margin = new Thickness(0, 0, 10, 4),
                Padding = new Thickness(7, 3),
                CornerRadius = new CornerRadius(20),
                Background = Brush(Alpha(color, 0.10)),
                Child = LegendDot(color, text)
            };
            panel.Children.Add(chip);
        }

        return panel;
    }

    public static Control Filter(string text, bool active, Action onClick, Color? accent = null)
    {
        Color tone = accent ?? T.Accent;
        Button button = new()
        {
            Content = text,
            FontSize = 11,
            Padding = new Thickness(12, 6),
            CornerRadius = new CornerRadius(20),
            Background = Brush(active ? Alpha(tone, 0.18) : Alpha(T.Raised, 0.7)),
            Foreground = Brush(active ? tone : T.Muted),
            BorderBrush = Brush(active ? Alpha(tone, 0.55) : Alpha(T.Line, 0.6)),
            BorderThickness = new Thickness(1)
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    public static Control Progress(string label, double fraction, Color color)
    {
        StackPanel stack = new() { Spacing = 4 };
        stack.Children.Add(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                Label(label, 9, T.Muted),
                AlignRight(Label($"{Math.Clamp(fraction, 0, 1) * 100:0}%", 9, color, FontWeight.SemiBold))
            }
        });
        stack.Children.Add(new Proportion(fraction, color) { Height = 8 });
        return stack;
    }

    public static Control KeyValue(string key, string value, Color? accent = null)
    {
        Grid row = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        row.Children.Add(new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(7),
            Background = Brush(Alpha(accent ?? T.Accent, 0.12)),
            Margin = new Thickness(0, 0, 9, 0),
            Child = Label(key, 10, accent ?? T.Accent, FontWeight.SemiBold)
        });
        Grid.SetColumn(row.Children[0], 0);
        Control text = Label(value, 11, T.Text);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    /// <summary>把按窗口标题拆开的 AppUsage 合并成进程级，避免同一应用重复占榜。</summary>
    public static IReadOnlyList<AppUsage> AggregateApps(IEnumerable<AppUsage> apps)
    {
        Dictionary<string, AppUsage> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (AppUsage app in apps)
        {
            string key = app.ProcessPath.Length > 0 ? app.ProcessPath : app.AppName;
            if (!result.TryGetValue(key, out AppUsage? target))
            {
                target = new AppUsage
                {
                    ProcessPath = app.ProcessPath,
                    Title = app.Title,
                    HasKeyGroups = app.HasKeyGroups
                };
                result[key] = target;
            }

            target.Keys += app.Keys;
            target.Clicks += app.Clicks;
            target.Wheel += app.Wheel;
            target.ActiveSeconds += app.ActiveSeconds;
            for (int i = 0; i < 6 && i < app.KeyGroups.Length; i++)
            {
                target.KeyGroups[i] += app.KeyGroups[i];
            }

            target.HasKeyGroups |= app.HasKeyGroups;
        }

        return result.Values.OrderByDescending(app => app.ActiveSeconds).ToList();
    }

    public static string FormatCount(long value)
    {
        if (value < 10_000)
        {
            return value.ToString("N0", CultureInfo.InvariantCulture);
        }

        double v = value;
        return v < 1e8
            ? (v / 1e4).ToString("0.#", CultureInfo.InvariantCulture) + "万"
            : (v / 1e8).ToString("0.##", CultureInfo.InvariantCulture) + "亿";
    }

    public static string FormatDuration(double seconds)
    {
        if (seconds < 60)
        {
            return $"{seconds:0}s";
        }

        if (seconds < 3600)
        {
            return $"{seconds / 60:0.#} 分";
        }

        return $"{seconds / 3600:0.##} 小时";
    }

    public static string FormatMeters(double meters) =>
        meters >= 1000
            ? (meters / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " km"
            : meters.ToString("0.#", CultureInfo.InvariantCulture) + " m";

    /// <summary>迷你面积折线：用于指标卡底部。</summary>
    public sealed class Spark : Control
    {
        private readonly IReadOnlyList<double> _values;
        private readonly Color _color;

        public Spark(IReadOnlyList<double> values, Color color)
        {
            _values = values;
            _color = color;
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            if (_values.Count == 0)
            {
                return;
            }

            Rect plot = new(1, 4, Math.Max(1, Bounds.Width - 2), Math.Max(1, Bounds.Height - 6));
            SparklineGeometry geometry = Sparkline.Measure(plot, _values, 1, includeZero: true);
            if (geometry.Points.Count > 1)
            {
                StreamGeometry area = new();
                using (StreamGeometryContext path = area.Open())
                {
                    path.BeginFigure(new Point(geometry.Points[0].X, plot.Bottom), true);
                    foreach (Point point in geometry.Points)
                    {
                        path.LineTo(point);
                    }

                    path.LineTo(new Point(geometry.Points[^1].X, plot.Bottom));
                    path.EndFigure(true);
                }

                context.DrawGeometry(Gradient(Alpha(_color, 0.30), Alpha(_color, 0.02), 90), null, area);
            }

            Sparkline.Draw(context, geometry, _color, 1.5);
            context.DrawEllipse(Brush(_color), null, geometry.Points[^1], 2.2, 2.2);
        }
    }

    /// <summary>环图 / 仪表盘。</summary>
    public sealed class Ring : Control
    {
        private readonly IReadOnlyList<DonutSlice> _slices;
        private readonly string? _center;
        private readonly string? _sub;

        public Ring(IReadOnlyList<DonutSlice> slices, string? center = null, string? sub = null)
        {
            _slices = slices;
            _center = center;
            _sub = sub;
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            Rect bounds = new(0, 0, Bounds.Width, Bounds.Height);
            DonutGeometry geometry = Donut.Measure(bounds, _slices, thicknessRatio: 0.24);
            Donut.Draw(context, geometry, T, _center, _sub, 22, 9);
        }
    }

    /// <summary>24 小时活动带：用 24 个圆角块展示一天节奏。</summary>
    public sealed class HourStrip : Control
    {
        private readonly double[] _values;
        private readonly Color _color;

        public HourStrip(double[] values, Color color)
        {
            _values = values;
            _color = color;
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            if (_values.Length == 0)
            {
                return;
            }

            double gap = 3;
            double width = Math.Max(1, (Bounds.Width - gap * (_values.Length - 1)) / _values.Length);
            double max = Math.Max(1, _values.Max());
            for (int i = 0; i < _values.Length; i++)
            {
                double height = Math.Max(3, Bounds.Height * Math.Clamp(_values[i] / max, 0, 1));
                double x = i * (width + gap);
                double alpha = 0.16 + 0.84 * Math.Clamp(_values[i] / max, 0, 1);
                context.FillRectangle(Brush(Alpha(_color, alpha)),
                    new Rect(x, Bounds.Height - height, width, height), 3);
            }
        }
    }

    /// <summary>纵向比例条，用于应用/按键排行。</summary>
    public sealed class Proportion : Control
    {
        private readonly double _fraction;
        private readonly Color _color;

        public Proportion(double fraction, Color color)
        {
            _fraction = Math.Clamp(fraction, 0, 1);
            _color = color;
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            context.FillRectangle(Brush(Alpha(T.Line, 0.22)), new Rect(0, 0, Bounds.Width, Bounds.Height), 3);
            context.FillRectangle(Gradient(Alpha(_color, 0.55), _color, 0),
                new Rect(0, 0, Bounds.Width * _fraction, Bounds.Height), 3);
        }
    }

    /// <summary>紧凑分段选择器（V3 胶囊组）。</summary>
    public static Control Segmented(string[] labels, int selected, Action<int> onSelect, double fontSize = 10.5)
    {
        StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        for (int i = 0; i < labels.Length; i++)
        {
            int index = i;
            Button button = new()
            {
                Content = labels[i],
                FontSize = fontSize,
                Padding = new Thickness(11, 5),
                CornerRadius = new CornerRadius(7),
                Background = i == selected ? Brush(T.Accent) : Brush(T.Raised),
                Foreground = i == selected ? Brush(T.OnAccent) : Brush(T.Muted),
                BorderBrush = Brush(Alpha(T.Line, 0.7)),
                BorderThickness = new Thickness(1)
            };
            button.Click += (_, _) => onSelect(index);
            row.Children.Add(button);
        }

        return row;
    }

    /// <summary>卡片内局部分页：只显示 pageSize 条，上一页/下一页。</summary>
    public sealed class PagedList : UserControl
    {
        private readonly StackPanel _body;
        private readonly StackPanel _pager;
        private readonly List<Control> _items = [];
        private int _pageSize;
        private int _page;

        public PagedList(int pageSize = 6, double spacing = 5)
        {
            _pageSize = Math.Max(1, pageSize);
            _body = new StackPanel { Spacing = spacing };
            _pager = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                HorizontalAlignment = HorizontalAlignment.Right,
                Height = 22
            };

            StackPanel root = new() { Spacing = 4 };
            root.Children.Add(_body);
            root.Children.Add(_pager);
            Content = root;
        }

        public void SetPageSize(int pageSize)
        {
            _pageSize = Math.Max(1, pageSize);
            _page = Math.Min(_page, Math.Max(0, PageCount - 1));
            RenderPage();
        }

        public void SetItems(IEnumerable<Control> items)
        {
            _items.Clear();
            _items.AddRange(items);
            _page = 0;
            RenderPage();
        }

        private int PageCount => Math.Max(1, (_items.Count + _pageSize - 1) / _pageSize);

        private void RenderPage()
        {
            _body.Children.Clear();
            int start = _page * _pageSize;
            for (int i = start; i < Math.Min(_items.Count, start + _pageSize); i++)
            {
                _body.Children.Add(_items[i]);
            }

            _pager.Children.Clear();
            if (_items.Count <= _pageSize && _pager.Children.Count == 0)
            {
                return;
            }

            Button prev = PagerButton("‹");
            prev.Opacity = _page > 0 ? 1 : 0.25;
            prev.Click += (_, _) =>
            {
                if (_page > 0)
                {
                    _page--;
                    RenderPage();
                }
            };
            Button next = PagerButton("›");
            next.Opacity = _page < PageCount - 1 ? 1 : 0.25;
            next.Click += (_, _) =>
            {
                if (_page < PageCount - 1)
                {
                    _page++;
                    RenderPage();
                }
            };
            _pager.Children.Add(prev);
            _pager.Children.Add(Label($"{_page + 1} / {PageCount}", 9.5, T.Muted));
            _pager.Children.Add(next);
        }

        private static Button PagerButton(string text) => new()
        {
            Content = text,
            Width = 24,
            Height = 20,
            Padding = new Thickness(0),
            FontSize = 13,
            CornerRadius = new CornerRadius(5),
            Background = Brush(Colors.Transparent),
            Foreground = Brush(T.Muted),
            BorderThickness = new Thickness(0)
        };
    }
}
