using Kms.App.Widgets;

namespace Kms.App.Layout;

/// <summary>
/// 编辑器纯函数网格算法：空位查找、移动/交换、尺寸切换。
/// 不依赖 Avalonia 控件，便于独立单元测试；所有结果最后仍会经过 LayoutValidation。
/// </summary>
public static class BentoLayoutMath
{
    public static bool TryFindFirstFit(
        IReadOnlyList<WidgetPlacement> placements,
        WidgetSpan span,
        out int col,
        out int row)
    {
        col = 0;
        row = 0;
        for (int r = 0; r <= LayoutPresets.Rows - span.Rows; r++)
        {
            for (int c = 0; c <= LayoutPresets.Columns - span.Columns; c++)
            {
                if (!Overlaps(placements, c, r, span.Columns, span.Rows, ignoreModuleId: null))
                {
                    col = c;
                    row = r;
                    return true;
                }
            }
        }

        return false;
    }

    public static bool Overlaps(
        IReadOnlyList<WidgetPlacement> placements,
        int col,
        int row,
        int colSpan,
        int rowSpan,
        string? ignoreModuleId)
    {
        foreach (WidgetPlacement placement in placements)
        {
            if (ignoreModuleId is not null
                && string.Equals(placement.ModuleId, ignoreModuleId, StringComparison.Ordinal))
            {
                continue;
            }

            if (col < placement.Col + placement.ColSpan
                && col + colSpan > placement.Col
                && row < placement.Row + placement.RowSpan
                && row + rowSpan > placement.Row)
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryMoveOrSwap(
        IReadOnlyList<WidgetPlacement> source,
        string moduleId,
        int targetCol,
        int targetRow,
        out List<WidgetPlacement> updated,
        out string? error)
    {
        updated = Clone(source);
        error = null;
        WidgetPlacement? moving = updated.FirstOrDefault(
            placement => string.Equals(placement.ModuleId, moduleId, StringComparison.Ordinal));
        if (moving is null)
        {
            error = $"未找到模块：{moduleId}。";
            return false;
        }

        int col = Math.Clamp(targetCol, 0, LayoutPresets.Columns - moving.ColSpan);
        int row = Math.Clamp(targetRow, 0, LayoutPresets.Rows - moving.RowSpan);
        if (col == moving.Col && row == moving.Row)
        {
            return true;
        }

        List<WidgetPlacement> collisions = updated
            .Where(placement => !string.Equals(placement.ModuleId, moduleId, StringComparison.Ordinal))
            .Where(placement => col < placement.Col + placement.ColSpan
                                && col + moving.ColSpan > placement.Col
                                && row < placement.Row + placement.RowSpan
                                && row + moving.RowSpan > placement.Row)
            .ToList();
        if (collisions.Count == 0)
        {
            moving.Col = col;
            moving.Row = row;
            return true;
        }

        if (collisions.Count == 1)
        {
            WidgetPlacement other = collisions[0];
            if (other.ColSpan == moving.ColSpan && other.RowSpan == moving.RowSpan)
            {
                int oldCol = moving.Col;
                int oldRow = moving.Row;
                moving.Col = col;
                moving.Row = row;
                other.Col = oldCol;
                other.Row = oldRow;
                return true;
            }
        }

        error = "目标区域已被不同尺寸的卡片占用；请先移动或缩小它们。";
        return false;
    }

    public static bool TryResize(
        IReadOnlyList<WidgetPlacement> source,
        string moduleId,
        WidgetSpan newSpan,
        bool findAlternativePosition,
        out List<WidgetPlacement> updated,
        out string? error)
    {
        updated = Clone(source);
        error = null;
        WidgetPlacement? target = updated.FirstOrDefault(
            placement => string.Equals(placement.ModuleId, moduleId, StringComparison.Ordinal));
        if (target is null)
        {
            error = $"未找到模块：{moduleId}。";
            return false;
        }

        if (!newSpan.IsWithin(LayoutPresets.Columns, LayoutPresets.Rows))
        {
            error = $"尺寸 {newSpan} 超出 12×6。";
            return false;
        }

        int col = Math.Min(target.Col, LayoutPresets.Columns - newSpan.Columns);
        int row = Math.Min(target.Row, LayoutPresets.Rows - newSpan.Rows);
        if (!Overlaps(updated, col, row, newSpan.Columns, newSpan.Rows, moduleId))
        {
            target.Col = col;
            target.Row = row;
            target.ColSpan = newSpan.Columns;
            target.RowSpan = newSpan.Rows;
            return true;
        }

        if (findAlternativePosition)
        {
            List<WidgetPlacement> without = updated
                .Where(placement => !string.Equals(placement.ModuleId, moduleId, StringComparison.Ordinal))
                .ToList();
            if (TryFindFirstFit(without, newSpan, out int fitCol, out int fitRow))
            {
                target.Col = fitCol;
                target.Row = fitRow;
                target.ColSpan = newSpan.Columns;
                target.RowSpan = newSpan.Rows;
                return true;
            }
        }

        error = $"尺寸 {newSpan} 会与现有卡片重叠，且当前页面没有空位。";
        return false;
    }

    public static List<WidgetPlacement> Clone(IReadOnlyList<WidgetPlacement> source) =>
        source.Select(static placement => placement.Clone()).ToList();
}
