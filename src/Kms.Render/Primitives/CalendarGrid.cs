using Avalonia;
using Avalonia.Media;
using Kms.Render.Tokens;

namespace Kms.Render.Primitives;

public readonly record struct CalendarCell(int Day, int Row, int Column, Rect Bounds);

public sealed record CalendarGridGeometry(
    Rect Bounds,
    int Year,
    int Month,
    int FirstDayOffset,
    int DaysInMonth,
    int Rows,
    int Columns,
    DayOfWeek WeekStart,
    IReadOnlyList<CalendarCell> Cells)
{
    public CalendarCell? Cell(int day)
    {
        if (day < 1 || day > DaysInMonth)
        {
            return null;
        }

        return Cells[day - 1];
    }

    public CalendarCell? HitTest(Point point)
    {
        for (int i = 0; i < Cells.Count; i++)
        {
            if (Cells[i].Bounds.Contains(point))
            {
                return Cells[i];
            }
        }

        return null;
    }
}

/// <summary>月历 7×N 网格：首日偏移从 WeekStart 起算，前导空白不生成格子。</summary>
public static class CalendarGrid
{
    public static CalendarGridGeometry MeasureMonth(
        Rect bounds,
        int year,
        int month,
        DayOfWeek weekStart = DayOfWeek.Monday,
        double gap = 2)
    {
        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month), month, "month 必须在 1..12。");
        }

        int days = DateTime.DaysInMonth(year, month);
        int offset = ((int)new DateTime(year, month, 1).DayOfWeek - (int)weekStart + 7) % 7;
        CalendarGridGeometry core = Measure(bounds, days, offset, gap);

        return core with
        {
            Year = year,
            Month = month,
            WeekStart = weekStart
        };
    }

    /// <summary>无年份/月份的纯网格，firstDayOffset = 本月 1 号之前的空白格数（0..6）。</summary>
    public static CalendarGridGeometry Measure(
        Rect bounds,
        int daysInMonth,
        int firstDayOffset,
        double gap = 2)
    {
        Rect normalized = PrimitiveMath.Normalize(bounds);
        int days = Math.Clamp(daysInMonth, 1, 31);
        int offset = ((firstDayOffset % 7) + 7) % 7;
        int rows = Math.Max(1, (offset + days + 6) / 7);
        double spacing = Math.Max(0, PrimitiveMath.Finite(gap));
        double cellWidth = Math.Max(0, (normalized.Width - spacing * 6) / 7);
        double cellHeight = Math.Max(0, (normalized.Height - spacing * (rows - 1)) / rows);

        CalendarCell[] cells = new CalendarCell[days];
        for (int day = 1; day <= days; day++)
        {
            int slot = offset + day - 1;
            int row = slot / 7;
            int column = slot % 7;
            cells[day - 1] = new CalendarCell(
                day,
                row,
                column,
                new Rect(
                    normalized.X + column * (cellWidth + spacing),
                    normalized.Y + row * (cellHeight + spacing),
                    cellWidth,
                    cellHeight));
        }

        return new CalendarGridGeometry(
            normalized,
            0,
            0,
            offset,
            days,
            rows,
            7,
            DayOfWeek.Monday,
            cells);
    }

    public static void Draw(
        DrawingContext context,
        CalendarGridGeometry geometry,
        ThemeTokens theme,
        int? selectedDay = null,
        double fontSize = 12)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(theme);

        IBrush fill = new SolidColorBrush(theme.Raised);
        IBrush selectedFill = new SolidColorBrush(ColorMath.Mix(theme.Card, theme.Accent, 0.28));
        IPen border = new Pen(new SolidColorBrush(theme.Line), 0.8);
        IPen selectedBorder = new Pen(new SolidColorBrush(theme.Accent), 1.2);
        IBrush text = new SolidColorBrush(theme.Text);
        double size = Math.Max(1, PrimitiveMath.Finite(fontSize));

        for (int i = 0; i < geometry.Cells.Count; i++)
        {
            CalendarCell cell = geometry.Cells[i];
            if (cell.Bounds.Width <= 0 || cell.Bounds.Height <= 0)
            {
                continue;
            }

            bool selected = selectedDay == cell.Day;
            double radius = Math.Min(Math.Min(theme.Radius, 6), Math.Min(cell.Bounds.Width, cell.Bounds.Height) / 2);
            context.DrawRectangle(
                selected ? selectedFill : fill,
                selected ? selectedBorder : border,
                new RoundedRect(cell.Bounds, radius, radius),
                default);

            using (context.PushClip(cell.Bounds))
            {
                TextBlock.DrawCentered(
                    context,
                    cell.Day.ToString(),
                    cell.Bounds,
                    size,
                    text,
                    TextBlock.NumericFamily,
                    maxWidth: Math.Max(0, cell.Bounds.Width - 2));
            }
        }
    }

    /// <summary>登记每个日期格；payload 为 <see cref="CalendarDayHit"/>。</summary>
    public static void RegisterHits(
        HitLayer layer,
        CalendarGridGeometry geometry,
        string idPrefix = "day")
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(geometry);

        for (int i = 0; i < geometry.Cells.Count; i++)
        {
            CalendarCell cell = geometry.Cells[i];
            layer.Add(
                $"{idPrefix}:{cell.Day}",
                cell.Bounds,
                new CalendarDayHit(cell.Day, cell.Row, cell.Column));
        }
    }
}

public readonly record struct CalendarDayHit(int Day, int Row, int Column);
