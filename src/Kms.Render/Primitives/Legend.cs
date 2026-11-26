using Avalonia;
using Avalonia.Media;
using Kms.Render.Tokens;

namespace Kms.Render.Primitives;

public sealed record LegendItem(string Label, Color Color);

public readonly record struct LegendEntry(
    LegendItem Item,
    Rect Bounds,
    Rect Swatch,
    Rect TextBounds);

public sealed record LegendGeometry(
    Rect Bounds,
    IReadOnlyList<LegendEntry> Entries,
    int Columns,
    int Rows,
    double RowHeight);

/// <summary>图例：纯几何只做等宽单元格，文本宽度由 Draw 时的 FormattedText 决定。</summary>
public static class Legend
{
    /// <summary>按通道取色，等价于 v3 的 ChannelScale 图例色带。</summary>
    public static LegendItem ChannelItem(ThemeTokens theme, int channel, string label)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return new LegendItem(label, HeatScale.ChannelAccent(theme, channel));
    }

    public static LegendGeometry Measure(
        Rect bounds,
        IReadOnlyList<LegendItem> items,
        int columns = 0,
        double rowHeight = 16,
        double swatchSize = 8,
        double itemGap = 12,
        double rowGap = 4,
        double swatchGap = 6)
    {
        ArgumentNullException.ThrowIfNull(items);

        Rect normalized = PrimitiveMath.Normalize(bounds);
        if (items.Count == 0)
        {
            return new LegendGeometry(normalized, [], 0, 0, 0);
        }

        int cols = columns <= 0 ? items.Count : Math.Min(columns, items.Count);
        int rows = (items.Count + cols - 1) / cols;
        double height = Math.Max(1, PrimitiveMath.Finite(rowHeight));
        double gap = Math.Max(0, PrimitiveMath.Finite(itemGap));
        double verticalGap = Math.Max(0, PrimitiveMath.Finite(rowGap));
        double swatch = Math.Max(1, PrimitiveMath.Finite(swatchSize));
        double textGap = Math.Max(0, PrimitiveMath.Finite(swatchGap));
        double cellWidth = Math.Max(0, (normalized.Width - gap * (cols - 1)) / cols);

        LegendEntry[] entries = new LegendEntry[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            int row = i / cols;
            int column = i % cols;
            Rect cell = new(
                normalized.X + column * (cellWidth + gap),
                normalized.Y + row * (height + verticalGap),
                cellWidth,
                height);

            double actualSwatch = Math.Min(swatch, Math.Min(cell.Width, cell.Height));
            Rect swatchRect = new(
                cell.X,
                cell.Y + (cell.Height - actualSwatch) / 2,
                actualSwatch,
                actualSwatch);

            double textX = swatchRect.Right + textGap;
            Rect textBounds = new(
                textX,
                cell.Y,
                Math.Max(0, cell.Right - textX),
                cell.Height);

            entries[i] = new LegendEntry(items[i], cell, swatchRect, textBounds);
        }

        return new LegendGeometry(normalized, entries, cols, rows, height);
    }

    public static void Draw(
        DrawingContext context,
        LegendGeometry geometry,
        ThemeTokens theme,
        double fontSize = 11,
        double swatchRadius = 2,
        string fontFamily = TextBlock.DefaultFamily)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(theme);

        double size = Math.Max(1, PrimitiveMath.Finite(fontSize));
        double radius = Math.Max(0, PrimitiveMath.Finite(swatchRadius));
        IBrush labelBrush = new SolidColorBrush(theme.Text);

        for (int i = 0; i < geometry.Entries.Count; i++)
        {
            LegendEntry entry = geometry.Entries[i];
            IBrush brush = new SolidColorBrush(entry.Item.Color);

            if (radius > 0 && entry.Swatch.Width > 0 && entry.Swatch.Height > 0)
            {
                double r = Math.Min(radius, Math.Min(entry.Swatch.Width, entry.Swatch.Height) / 2);
                context.DrawRectangle(brush, null, new RoundedRect(entry.Swatch, r, r), default);
            }
            else
            {
                context.FillRectangle(brush, entry.Swatch, 0);
            }

            if (entry.TextBounds.Width <= 0)
            {
                continue;
            }

            FormattedText text = TextBlock.Create(
                entry.Item.Label,
                size,
                labelBrush,
                fontFamily,
                maxWidth: entry.TextBounds.Width,
                maxLines: 1);

            double y = entry.TextBounds.Y + (entry.TextBounds.Height - text.Height) / 2;
            context.DrawText(text, new Point(entry.TextBounds.X, y));
        }
    }

    /// <summary>登记每个图例项的整格命中区；payload 为 <see cref="LegendHit"/>。</summary>
    public static void RegisterHits(HitLayer layer, LegendGeometry geometry, string idPrefix = "legend")
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(geometry);

        for (int i = 0; i < geometry.Entries.Count; i++)
        {
            LegendEntry entry = geometry.Entries[i];
            layer.Add($"{idPrefix}:{i}", entry.Bounds, new LegendHit(entry.Item.Label, entry.Item.Color));
        }
    }
}

public readonly record struct LegendHit(string Label, Color Color);
