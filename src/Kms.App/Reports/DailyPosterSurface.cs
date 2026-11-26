using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Kms.Core.Model;
using Kms.Render.Primitives;
using Kms.Render.Tokens;
using PrimitiveText = Kms.Render.Primitives.TextBlock;

namespace Kms.App.Reports;

/// <summary>日报海报：1440×1800 DIP，导出时按 3× 渲染为 4320×5400。</summary>
public sealed class DailyPosterSurface : Control
{
    public const int PosterWidth = 1440;
    public const int PosterHeight = 1800;

    private readonly DayRecord? _day;
    private readonly ThemeTokens _theme;

    public DailyPosterSurface(DayRecord? day, ThemeTokens theme)
    {
        _day = day;
        _theme = theme;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(new SolidColorBrush(_theme.Background), new Rect(0, 0, Bounds.Width, Bounds.Height));

        IBrush text = new SolidColorBrush(_theme.Text);
        IBrush muted = new SolidColorBrush(_theme.Muted);
        IBrush accent = new SolidColorBrush(_theme.Accent);

        PrimitiveText.DrawText(
            context,
            "键鼠统计 · 日报",
            new Point(80, 64),
            64,
            text,
            PrimitiveText.DefaultFamily,
            FontWeight.SemiBold);
        PrimitiveText.DrawText(
            context,
            _day is null ? "暂无数据" : _day.Date.ToString("yyyy-MM-dd"),
            new Point(80, 146),
            30,
            muted,
            PrimitiveText.NumericFamily);

        DrawStats(context, text, muted);
        DrawHourly(context, text, muted);
        DrawApps(context, text, muted);
        PrimitiveText.DrawText(
            context,
            "由键鼠统计 v4 生成",
            new Point(80, Bounds.Height - 52),
            18,
            muted,
            PrimitiveText.DefaultFamily);
    }

    private void DrawStats(DrawingContext context, IBrush text, IBrush muted)
    {
        (string Label, string Value)[] stats =
        [
            ("击键", FormatCount(_day?.Keys ?? 0)),
            ("点击", FormatCount(_day?.Clicks ?? 0)),
            ("移动", FormatDistance(_day?.MoveMeters ?? 0)),
            ("活跃", FormatDuration(_day?.ActiveSeconds ?? 0))
        ];

        double width = (Bounds.Width - 80 * 2 - 24) / 2;
        for (int i = 0; i < stats.Length; i++)
        {
            double x = 80 + (i % 2) * (width + 24);
            double y = 230 + (i / 2) * 220;
            CardGeometry card = Card.Layout(new Rect(x, y, width, 190), _theme, 20);
            Card.Draw(context, card, _theme);
            PrimitiveText.DrawText(
                context,
                stats[i].Label,
                new Point(card.Content.X, card.Content.Y + 8),
                22,
                muted,
                PrimitiveText.DefaultFamily);
            PrimitiveText.DrawText(
                context,
                stats[i].Value,
                new Point(card.Content.X, card.Content.Y + 44),
                62,
                text,
                "Segoe UI",
                FontWeight.SemiBold);
        }
    }

    private void DrawHourly(DrawingContext context, IBrush text, IBrush muted)
    {
        CardGeometry card = Card.Layout(new Rect(80, 690, Bounds.Width - 160, 470), _theme, 20);
        Card.Draw(context, card, _theme);
        PrimitiveText.DrawText(
            context,
            "24 小时击键",
            new Point(card.Content.X, card.Content.Y),
            24,
            text,
            PrimitiveText.DefaultFamily,
            FontWeight.SemiBold);

        List<BarDatum> bars = new(24);
        for (int hour = 0; hour < 24; hour++)
        {
            bars.Add(new BarDatum(_day?.HourKeys[hour] ?? 0, hour.ToString("00"), _theme.Accent));
        }

        Rect plot = new(
            card.Content.X,
            card.Content.Y + 48,
            card.Content.Width,
            card.Content.Height - 70);
        BarChartGeometry geometry = BarChart.Measure(
            plot,
            bars,
            new BarChartOptions(Padding: 0, GapRatio: 0.24, IncludeZero: true, YTickCount: 3));
        BarChart.Draw(context, geometry, _theme, showBaseline: true, cornerRadius: 2);

        for (int hour = 0; hour < 24; hour += 3)
        {
            BarGeometry bar = geometry.Bars[hour];
            PrimitiveText.DrawText(
                context,
                hour.ToString("00"),
                new Point(bar.Slot.Center.X - 10, plot.Bottom + 8),
                14,
                muted,
                PrimitiveText.NumericFamily);
        }
    }

    private void DrawApps(DrawingContext context, IBrush text, IBrush muted)
    {
        CardGeometry card = Card.Layout(new Rect(80, 1210, Bounds.Width - 160, 470), _theme, 20);
        Card.Draw(context, card, _theme);
        PrimitiveText.DrawText(
            context,
            "应用 Top 8",
            new Point(card.Content.X, card.Content.Y),
            24,
            text,
            PrimitiveText.DefaultFamily,
            FontWeight.SemiBold);

        if (_day is null)
        {
            return;
        }

        int index = 0;
        foreach (AppUsage app in _day.Apps.Values.OrderByDescending(value => value.ActiveSeconds).Take(8))
        {
            double y = card.Content.Y + 52 + index * 46;
            PrimitiveText.DrawText(
                context,
                $"{index + 1}. {app.AppName}",
                new Point(card.Content.X, y),
                18,
                text,
                PrimitiveText.DefaultFamily,
                maxWidth: 780);
            PrimitiveText.DrawText(
                context,
                $"{FormatDuration(app.ActiveSeconds)} · {FormatCount(app.Keys)} 键",
                new Point(card.Content.Right - 260, y),
                16,
                muted,
                PrimitiveText.NumericFamily,
                alignment: TextAlignment.Right,
                maxWidth: 260);
            index++;
        }
    }

    private static string FormatCount(long value) =>
        value >= 1_000_000 ? (value / 1_000_000.0).ToString("0.##") + "M"
        : value >= 1000 ? (value / 1000.0).ToString("0.##") + "k"
        : value.ToString();

    private static string FormatDistance(double meters) =>
        meters >= 1000 ? (meters / 1000.0).ToString("0.#") + " km" : meters.ToString("0.#") + " m";

    private static string FormatDuration(double seconds) =>
        seconds < 60 ? $"{seconds:0}s"
        : seconds < 3600 ? $"{seconds / 60:0.#} 分"
        : $"{seconds / 3600:0.##} 小时";
}
