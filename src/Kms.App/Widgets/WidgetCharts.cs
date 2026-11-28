using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Kms.App.Pages;
using Kms.Render.Tokens;

namespace Kms.App.Widgets;

internal readonly record struct TreemapItem(string Label, double Value, Color Color);

/// <summary>
/// 矩形树图：按占比递归二分矩形，圆角块 + 名称/占比标签。
/// 纯矢量绘制，不依赖 XAML/反射；空间不足时自动省略标签。
/// </summary>
internal sealed class Treemap : Control
{
    private IReadOnlyList<TreemapItem> _items = [];

    public void SetData(IReadOnlyList<TreemapItem> items)
    {
        _items = items ?? [];
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Rect bounds = new(0, 0, Bounds.Width, Bounds.Height);
        if (bounds.Width <= 2 || bounds.Height <= 2)
        {
            return;
        }

        List<TreemapItem> items = _items
            .Where(static item => item.Value > 0)
            .OrderByDescending(static item => item.Value)
            .ToList();
        if (items.Count == 0)
        {
            Kms.Render.Primitives.TextBlock.DrawCentered(
                context, "暂无分布数据", bounds, 10, PageKit.Brush(PageKit.T.Muted));
            return;
        }

        double total = items.Sum(static item => item.Value);
        List<(TreemapItem Item, Rect Rect)> rects = [];
        Layout(items, bounds, rects);
        foreach ((TreemapItem item, Rect rect) in rects)
        {
            if (rect.Width <= 2 || rect.Height <= 2)
            {
                continue;
            }

            double share = total <= 0 ? 0 : item.Value / total;
            Color fill = PageKit.Alpha(item.Color, 0.18 + 0.28 * Math.Clamp(share * 3, 0, 1));
            context.FillRectangle(PageKit.Brush(fill), rect, 6);
            context.DrawRectangle(null,
                new Pen(PageKit.Brush(PageKit.Alpha(item.Color, 0.5)), 1),
                rect, 6);

            if (rect.Width < 42 || rect.Height < 28)
            {
                continue;
            }

            double titleSize = rect.Width < 72 || rect.Height < 42 ? 8.5 : 10;
            Kms.Render.Primitives.TextBlock.DrawText(
                context,
                item.Label,
                new Point(rect.Left + 6, rect.Top + 5),
                titleSize,
                PageKit.Brush(PageKit.T.Text),
                Kms.Render.Primitives.TextBlock.DefaultFamily,
                FontWeight.SemiBold,
                Math.Max(1, rect.Width - 12));
            Kms.Render.Primitives.TextBlock.DrawText(
                context,
                $"{share * 100:0.#}%",
                new Point(rect.Left + 6, rect.Bottom - 18),
                8.5,
                PageKit.Brush(PageKit.Alpha(PageKit.T.Text, 0.82)),
                Kms.Render.Primitives.TextBlock.NumericFamily,
                maxWidth: Math.Max(1, rect.Width - 12));
        }
    }

    private static void Layout(
        List<TreemapItem> items,
        Rect rect,
        List<(TreemapItem Item, Rect Rect)> result)
    {
        if (items.Count == 0 || rect.Width <= 2 || rect.Height <= 2)
        {
            return;
        }

        if (items.Count == 1)
        {
            result.Add((items[0], rect));
            return;
        }

        double total = items.Sum(static item => item.Value);
        if (total <= 0)
        {
            return;
        }

        double cumulative = 0;
        int split = 0;
        for (; split < items.Count - 1; split++)
        {
            cumulative += items[split].Value;
            if (cumulative >= total * 0.5)
            {
                split++;
                break;
            }
        }

        if (split <= 0)
        {
            split = 1;
        }
        else if (split >= items.Count)
        {
            split = items.Count - 1;
        }

        double firstSum = 0;
        for (int i = 0; i < split; i++)
        {
            firstSum += items[i].Value;
        }

        double ratio = Math.Clamp(firstSum / total, 0.08, 0.92);
        List<TreemapItem> first = items.GetRange(0, split);
        List<TreemapItem> second = items.GetRange(split, items.Count - split);
        if (rect.Width >= rect.Height)
        {
            double width = Math.Max(2, rect.Width * ratio);
            Layout(first, new Rect(rect.X, rect.Y, width - 2, rect.Height), result);
            Layout(second, new Rect(rect.X + width, rect.Y, Math.Max(2, rect.Right - rect.X - width), rect.Height), result);
        }
        else
        {
            double height = Math.Max(2, rect.Height * ratio);
            Layout(first, new Rect(rect.X, rect.Y, rect.Width, height - 2), result);
            Layout(second, new Rect(rect.X, rect.Y + height, rect.Width, Math.Max(2, rect.Bottom - rect.Y - height)), result);
        }
    }
}

internal readonly record struct KeycapItem(string Label, long Value, double Fraction, Color Color);

/// <summary>
/// 机械键帽矩阵：按可用宽高计算行列，绘制仿真键帽、按键名、次数与占比。
/// 纯 Render 绘制，不创建子控件，天然不会产生滚动或布局溢出。
/// </summary>
internal sealed class KeycapMatrix : Control
{
    private IReadOnlyList<KeycapItem> _items = [];

    public KeycapMatrix()
    {
        ClipToBounds = true;
    }

    public void SetData(IReadOnlyList<KeycapItem> items)
    {
        _items = items ?? [];
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Rect bounds = new(0, 0, Bounds.Width, Bounds.Height);
        if (bounds.Width <= 8 || bounds.Height <= 8)
        {
            return;
        }

        const double gap = 7;
        int columns = Math.Max(1, (int)((bounds.Width + gap) / 78));
        int rows = Math.Max(1, (int)((bounds.Height + gap) / 58));
        int capacity = columns * rows;
        if (_items.Count == 0)
        {
            Kms.Render.Primitives.TextBlock.DrawCentered(
                context, "暂无按键记录", bounds, 10, PageKit.Brush(PageKit.T.Muted));
            return;
        }

        double cardWidth = Math.Max(42, (bounds.Width - gap * (columns - 1)) / columns);
        double cardHeight = Math.Max(36, (bounds.Height - gap * (rows - 1)) / rows);
        int count = Math.Min(capacity, _items.Count);
        for (int i = 0; i < count; i++)
        {
            KeycapItem item = _items[i];
            double x = bounds.X + (i % columns) * (cardWidth + gap);
            double y = bounds.Y + (i / columns) * (cardHeight + gap);
            Rect card = new(x, y, cardWidth, cardHeight);
            DrawCap(context, card, item);
        }
    }

    private static void DrawCap(DrawingContext context, Rect card, KeycapItem item)
    {
        ThemeTokens theme = PageKit.T;
        Rect inner = card.Deflate(2);
        context.FillRectangle(
            PageKit.Brush(PageKit.Alpha(theme.Raised, 0.92)),
            card,
            8);
        context.FillRectangle(
            PageKit.Gradient(PageKit.Alpha(item.Color, 0.24), PageKit.Alpha(theme.Card, 0.92), 90),
            inner,
            7);
        context.DrawRectangle(
            new Pen(PageKit.Brush(PageKit.Alpha(item.Color, 0.55)), 1),
            card,
            8);
        context.DrawLine(
            new Pen(PageKit.Brush(PageKit.Alpha(theme.Text, 0.22)), 1),
            new Point(card.Left + 6, card.Top + 2),
            new Point(card.Right - 6, card.Top + 2));

        double labelSize = card.Width < 66 ? 8.5 : 9.5;
        double valueSize = card.Width < 66 ? 12 : 15;
        Kms.Render.Primitives.TextBlock.DrawCentered(
            context,
            item.Label,
            new Rect(card.Left + 4, card.Top + 5, card.Width - 8, card.Height * 0.34),
            labelSize,
            PageKit.Brush(PageKit.Alpha(theme.Muted, 0.95)),
            Kms.Render.Primitives.TextBlock.DefaultFamily,
            FontWeight.Medium,
            maxWidth: card.Width - 8);
        Kms.Render.Primitives.TextBlock.DrawCentered(
            context,
            PageKit.FormatCount(item.Value),
            new Rect(card.Left + 4, card.Top + card.Height * 0.32, card.Width - 8, card.Height * 0.4),
            valueSize,
            PageKit.Brush(theme.Text),
            Kms.Render.Primitives.TextBlock.NumericFamily,
            FontWeight.SemiBold,
            maxWidth: card.Width - 8);
        Kms.Render.Primitives.TextBlock.DrawCentered(
            context,
            $"{Math.Clamp(item.Fraction, 0, 1) * 100:0.#}%",
            new Rect(card.Left + 4, card.Bottom - card.Height * 0.26, card.Width - 8, card.Height * 0.22),
            8,
            PageKit.Brush(item.Color),
            Kms.Render.Primitives.TextBlock.NumericFamily,
            FontWeight.SemiBold,
            maxWidth: card.Width - 8);
    }
}
