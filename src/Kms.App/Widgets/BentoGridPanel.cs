using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;

namespace Kms.App.Widgets;

/// <summary>
/// 12×6 Bento 栅格几何。纯函数，便于单元测试；所有边界在这里先夹紧，
/// Panel 只负责把结果交给 Arrange。
/// </summary>
public static class BentoGridMath
{
    public static Size GetCellSize(Size viewport, int columns, int rows, double spacing)
    {
        (int safeColumns, int safeRows, double safeSpacing) = Sanitize(columns, rows, spacing);
        double width = Finite(viewport.Width, 0);
        double height = Finite(viewport.Height, 0);
        double usableWidth = Math.Max(0, width - safeSpacing * (safeColumns - 1));
        double usableHeight = Math.Max(0, height - safeSpacing * (safeRows - 1));
        return new Size(usableWidth / safeColumns, usableHeight / safeRows);
    }

    /// <summary>
    /// 计算一个子项在视口内的像素矩形。越界的 Col/Row/ColSpan/RowSpan 会被夹紧，
    /// 保证结果永远落在 [0, viewport] 内，不会产生纵向溢出。
    /// </summary>
    public static Rect ComputeCell(
        Size viewport,
        int columns,
        int rows,
        double spacing,
        int col,
        int row,
        int colSpan,
        int rowSpan)
    {
        (int safeColumns, int safeRows, double safeSpacing) = Sanitize(columns, rows, spacing);
        double width = Finite(viewport.Width, 0);
        double height = Finite(viewport.Height, 0);
        if (width <= 0 || height <= 0)
        {
            return default;
        }

        int safeCol = Math.Clamp(col, 0, safeColumns - 1);
        int safeRow = Math.Clamp(row, 0, safeRows - 1);
        int safeColSpan = Math.Clamp(colSpan, 1, safeColumns - safeCol);
        int safeRowSpan = Math.Clamp(rowSpan, 1, safeRows - safeRow);

        Size cell = GetCellSize(viewport, safeColumns, safeRows, safeSpacing);
        double x = safeCol * (cell.Width + safeSpacing);
        double y = safeRow * (cell.Height + safeSpacing);
        double cellWidth = safeColSpan * cell.Width + (safeColSpan - 1) * safeSpacing;
        double cellHeight = safeRowSpan * cell.Height + (safeRowSpan - 1) * safeSpacing;

        x = Math.Clamp(x, 0, width);
        y = Math.Clamp(y, 0, height);
        cellWidth = Math.Clamp(cellWidth, 0, Math.Max(0, width - x));
        cellHeight = Math.Clamp(cellHeight, 0, Math.Max(0, height - y));
        return new Rect(x, y, cellWidth, cellHeight);
    }

    public static (int Col, int Row, int ColSpan, int RowSpan) Clamp(
        int columns,
        int rows,
        int col,
        int row,
        int colSpan,
        int rowSpan)
    {
        columns = Math.Max(1, columns);
        rows = Math.Max(1, rows);
        int safeCol = Math.Clamp(col, 0, columns - 1);
        int safeRow = Math.Clamp(row, 0, rows - 1);
        int safeColSpan = Math.Clamp(colSpan, 1, columns - safeCol);
        int safeRowSpan = Math.Clamp(rowSpan, 1, rows - safeRow);
        return (safeCol, safeRow, safeColSpan, safeRowSpan);
    }

    private static (int Columns, int Rows, double Spacing) Sanitize(int columns, int rows, double spacing) =>
        (Math.Max(1, columns), Math.Max(1, rows), Finite(spacing, 0));

    private static double Finite(double value, double fallback) =>
        double.IsFinite(value) ? Math.Max(0, value) : fallback;
}

/// <summary>
/// 12 列 × 6 行零滚动响应式栅格。默认容量与间距由属性控制；
/// 子卡片通过 Col/Row/ColSpan/RowSpan 附加属性声明位置。
/// 布局始终在父视口内完成并裁剪，绝不产生纵向滚动。
/// </summary>
public sealed class BentoGridPanel : Panel
{
    public static readonly StyledProperty<int> ColumnsProperty =
        AvaloniaProperty.Register<BentoGridPanel, int>(nameof(Columns), 12);

    public static readonly StyledProperty<int> RowsProperty =
        AvaloniaProperty.Register<BentoGridPanel, int>(nameof(Rows), 6);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<BentoGridPanel, double>(nameof(Spacing), 12);

    public static readonly AttachedProperty<int> ColProperty =
        AvaloniaProperty.RegisterAttached<BentoGridPanel, Control, int>("Col", 0);

    public static readonly AttachedProperty<int> RowProperty =
        AvaloniaProperty.RegisterAttached<BentoGridPanel, Control, int>("Row", 0);

    public static readonly AttachedProperty<int> ColSpanProperty =
        AvaloniaProperty.RegisterAttached<BentoGridPanel, Control, int>("ColSpan", 1);

    public static readonly AttachedProperty<int> RowSpanProperty =
        AvaloniaProperty.RegisterAttached<BentoGridPanel, Control, int>("RowSpan", 1);

    private static readonly AvaloniaProperty[] PlacementProperties =
    [
        ColProperty,
        RowProperty,
        ColSpanProperty,
        RowSpanProperty
    ];

    private readonly HashSet<Control> _watched = [];

    static BentoGridPanel()
    {
        AffectsMeasure<BentoGridPanel>(ColumnsProperty, RowsProperty, SpacingProperty);
    }

    public BentoGridPanel()
    {
        ClipToBounds = true;
        Children.CollectionChanged += OnChildrenChanged;
    }

    public int Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public int Rows
    {
        get => GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public static int GetCol(Control control) => control.GetValue(ColProperty);

    public static void SetCol(Control control, int value) => control.SetValue(ColProperty, value);

    public static int GetRow(Control control) => control.GetValue(RowProperty);

    public static void SetRow(Control control, int value) => control.SetValue(RowProperty, value);

    public static int GetColSpan(Control control) => control.GetValue(ColSpanProperty);

    public static void SetColSpan(Control control, int value) => control.SetValue(ColSpanProperty, value);

    public static int GetRowSpan(Control control) => control.GetValue(RowSpanProperty);

    public static void SetRowSpan(Control control, int value) => control.SetValue(RowSpanProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        Size viewport = ResolveViewport(availableSize);
        for (int i = 0; i < Children.Count; i++)
        {
            Control child = Children[i];
            Rect cell = CellFor(viewport, child);
            child.Measure(cell.Size);
        }

        return viewport;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Size viewport = ResolveViewport(finalSize);
        for (int i = 0; i < Children.Count; i++)
        {
            Control child = Children[i];
            Rect cell = CellFor(viewport, child);
            double width = Math.Max(0, Math.Min(cell.Width, finalSize.Width - cell.X));
            double height = Math.Max(0, Math.Min(cell.Height, finalSize.Height - cell.Y));
            child.Arrange(new Rect(cell.X, cell.Y, width, height));
        }

        return finalSize;
    }

    private Rect CellFor(Size viewport, Control child)
    {
        (int col, int row, int colSpan, int rowSpan) = BentoGridMath.Clamp(
            Columns,
            Rows,
            GetCol(child),
            GetRow(child),
            GetColSpan(child),
            GetRowSpan(child));

        return BentoGridMath.ComputeCell(viewport, Columns, Rows, Spacing, col, row, colSpan, rowSpan);
    }

    private Size ResolveViewport(Size size)
    {
        double width = double.IsFinite(size.Width) ? Math.Max(0, size.Width) : Math.Max(1, Columns) * 96.0;
        double height = double.IsFinite(size.Height) ? Math.Max(0, size.Height) : Math.Max(1, Rows) * 72.0;
        return new Size(width, height);
    }

    private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (Control child in e.OldItems.OfType<Control>())
            {
                if (_watched.Remove(child))
                {
                    child.PropertyChanged -= OnChildPropertyChanged;
                }
            }
        }

        if (e.NewItems is not null)
        {
            foreach (Control child in e.NewItems.OfType<Control>())
            {
                if (_watched.Add(child))
                {
                    child.PropertyChanged += OnChildPropertyChanged;
                }
            }
        }

        InvalidateMeasure();
    }

    private void OnChildPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (PlacementProperties.Contains(e.Property))
        {
            InvalidateMeasure();
        }
    }
}
