using Kms.App.Widgets;

namespace Kms.App.Layout;

/// <summary>
/// 布局校验与归一化。自定义 layouts.json 允许省略尺寸/变体，
/// 但模块必须存在、最终矩形必须在 12×6 内且互不重叠；
/// 任何不合法页面都会整体回退到内置预设，避免半坏布局。
/// </summary>
public static class LayoutValidation
{
    public static bool TryNormalize(
        PageLayoutConfig? source,
        out PageLayoutConfig normalized,
        out string? error)
    {
        normalized = new PageLayoutConfig();
        error = null;
        if (source is null)
        {
            error = "布局为空。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(source.PageId))
        {
            error = "PageId 不能为空。";
            return false;
        }

        if (source.Placements is null || source.Placements.Count == 0)
        {
            error = "页面至少需要一个模块。";
            return false;
        }

        List<WidgetPlacement> placements = new(source.Placements.Count);
        HashSet<string> moduleIds = new(StringComparer.Ordinal);
        bool[,] occupied = new bool[LayoutPresets.Columns, LayoutPresets.Rows];

        foreach (WidgetPlacement? placement in source.Placements)
        {
            if (placement is null || string.IsNullOrWhiteSpace(placement.ModuleId))
            {
                error = "存在空模块或 ModuleId 为空的布局项。";
                return false;
            }

            if (!WidgetRegistry.TryGet(placement.ModuleId, out IStatWidgetModule module))
            {
                error = $"未注册模块：{placement.ModuleId}。";
                return false;
            }

            if (!moduleIds.Add(placement.ModuleId))
            {
                error = $"模块重复：{placement.ModuleId}。";
                return false;
            }

            WidgetDescriptor descriptor = module.Descriptor;
            WidgetSpan span = placement.ColSpan > 0 && placement.RowSpan > 0
                ? new WidgetSpan(placement.ColSpan, placement.RowSpan)
                : descriptor.DefaultSpan;
            if (!descriptor.SupportedSpans.Contains(span))
            {
                error = $"模块 {placement.ModuleId} 不支持尺寸 {span}。";
                return false;
            }

            string variant = WidgetRegistry.ResolveVariant(descriptor, placement.VariantId);

            int colSpan = span.Columns;
            int rowSpan = span.Rows;
            if (placement.Col < 0
                || placement.Row < 0
                || placement.Col + colSpan > LayoutPresets.Columns
                || placement.Row + rowSpan > LayoutPresets.Rows)
            {
                error = $"模块 {placement.ModuleId} 超出 12×6 栅格。";
                return false;
            }

            for (int x = placement.Col; x < placement.Col + colSpan; x++)
            {
                for (int y = placement.Row; y < placement.Row + rowSpan; y++)
                {
                    if (occupied[x, y])
                    {
                        error = $"模块 {placement.ModuleId} 与其他模块重叠于 ({x},{y})。";
                        return false;
                    }

                    occupied[x, y] = true;
                }
            }

            placements.Add(new WidgetPlacement
            {
                ModuleId = placement.ModuleId,
                VariantId = variant,
                Col = placement.Col,
                Row = placement.Row,
                ColSpan = colSpan,
                RowSpan = rowSpan
            });
        }

        normalized = new PageLayoutConfig
        {
            PageId = source.PageId,
            Placements = placements
        };
        return true;
    }

    public static bool HasOverlap(IReadOnlyList<WidgetPlacement> placements)
    {
        bool[,] occupied = new bool[LayoutPresets.Columns, LayoutPresets.Rows];
        foreach (WidgetPlacement placement in placements)
        {
            int col = Math.Clamp(placement.Col, 0, LayoutPresets.Columns - 1);
            int row = Math.Clamp(placement.Row, 0, LayoutPresets.Rows - 1);
            int colSpan = Math.Clamp(placement.ColSpan, 1, LayoutPresets.Columns - col);
            int rowSpan = Math.Clamp(placement.RowSpan, 1, LayoutPresets.Rows - row);
            for (int x = col; x < col + colSpan; x++)
            {
                for (int y = row; y < row + rowSpan; y++)
                {
                    if (occupied[x, y])
                    {
                        return true;
                    }

                    occupied[x, y] = true;
                }
            }
        }

        return false;
    }
}
