using Avalonia;
using Avalonia.Media;
using Kms.Render.Tokens;

namespace Kms.Render.Primitives;

public readonly record struct HeatCell(
    int Row,
    int Column,
    long Count,
    Rect Bounds,
    Color Fill);

public sealed record HeatGridGeometry(
    Rect Bounds,
    IReadOnlyList<HeatCell> Cells,
    int Rows,
    int Columns,
    long Max,
    double CellWidth,
    double CellHeight,
    double Gap);

/// <summary>
/// 热力网格。颜色一律走 <see cref="HeatScale.For"/>（log(1+n)/log(1+max) 再取 0.85 gamma），
/// 不在原语内另造归一化。
/// </summary>
public static class HeatGrid
{
    public static HeatGridGeometry Measure(
        Rect bounds,
        long[,] counts,
        ThemeTokens theme,
        int channel = HeatScale.All,
        double gap = 2)
    {
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentNullException.ThrowIfNull(theme);

        Rect normalized = PrimitiveMath.Normalize(bounds);
        int rows = counts.GetLength(0);
        int columns = counts.GetLength(1);
        double spacing = Math.Max(0, PrimitiveMath.Finite(gap));

        if (rows <= 0 || columns <= 0)
        {
            return new HeatGridGeometry(normalized, [], rows, columns, 0, 0, 0, spacing);
        }

        long max = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                max = Math.Max(max, Math.Max(0, counts[r, c]));
            }
        }

        double cellWidth = Math.Max(0, (normalized.Width - spacing * (columns - 1)) / columns);
        double cellHeight = Math.Max(0, (normalized.Height - spacing * (rows - 1)) / rows);
        HeatCell[] cells = new HeatCell[rows * columns];
        int index = 0;

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                long count = Math.Max(0, counts[r, c]);
                Rect cellBounds = new(
                    normalized.X + c * (cellWidth + spacing),
                    normalized.Y + r * (cellHeight + spacing),
                    cellWidth,
                    cellHeight);

                cells[index++] = new HeatCell(
                    r,
                    c,
                    count,
                    cellBounds,
                    HeatScale.For(theme, channel, count, max));
            }
        }

        return new HeatGridGeometry(
            normalized,
            cells,
            rows,
            columns,
            max,
            cellWidth,
            cellHeight,
            spacing);
    }

    public static Color ColorFor(ThemeTokens theme, int channel, long count, long max)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return HeatScale.For(theme, channel, Math.Max(0, count), Math.Max(0, max));
    }

    public static void Draw(DrawingContext context, HeatGridGeometry geometry, double cornerRadius = 2)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(geometry);

        float radius = (float)Math.Max(0, PrimitiveMath.Finite(cornerRadius));

        for (int i = 0; i < geometry.Cells.Count; i++)
        {
            HeatCell cell = geometry.Cells[i];
            if (cell.Bounds.Width <= 0 || cell.Bounds.Height <= 0)
            {
                continue;
            }

            IBrush brush = new SolidColorBrush(cell.Fill);
            if (radius > 0)
            {
                double r = Math.Min(radius, Math.Min(cell.Bounds.Width, cell.Bounds.Height) / 2);
                context.DrawRectangle(brush, null, new RoundedRect(cell.Bounds, r, r), default);
            }
            else
            {
                context.FillRectangle(brush, cell.Bounds, 0);
            }
        }
    }

    /// <summary>每个格子登记命中区；payload 为 <see cref="HeatCellHit"/>。</summary>
    public static void RegisterHits(HitLayer layer, HeatGridGeometry geometry, string idPrefix = "heat")
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(geometry);

        for (int i = 0; i < geometry.Cells.Count; i++)
        {
            HeatCell cell = geometry.Cells[i];
            layer.Add(
                $"{idPrefix}:{cell.Row}:{cell.Column}",
                cell.Bounds,
                new HeatCellHit(cell.Row, cell.Column, cell.Count));
        }
    }
}

public readonly record struct HeatCellHit(int Row, int Column, long Count);
