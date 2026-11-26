using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Kms.Core.Model;
using Kms.Render.Primitives;
using Kms.Render.Tokens;
using PrimitiveText = Kms.Render.Primitives.TextBlock;

namespace Kms.App.Reports;

/// <summary>M6 / T6.4 战报长图：1440×2400 DIP，3× 导出。</summary>
internal sealed class BattlePosterSurface : Control
{
    public const int PosterWidth = 1440;
    public const int DefaultPosterHeight = 2400;
    public const int MaxApps = 20;

    private readonly DayRecord? _day;
    private readonly ThemeTokens _theme;
    private readonly bool _desensitize;

    public BattlePosterSurface(DayRecord? day, ThemeTokens theme, bool desensitize = false)
    {
        _day = day;
        _theme = theme;
        _desensitize = desensitize;
    }

    /// <summary>按应用数量计算海报高度；12 个以内保持 2400，超出后向下扩展。</summary>
    public static int MeasureHeight(DayRecord? day)
    {
        int count = Math.Clamp(day?.Apps.Count ?? 0, 0, MaxApps);
        int appsHeight = Math.Max(1100, 62 + count * 82 + 80);
        return Math.Max(DefaultPosterHeight, 1190 + appsHeight + 80);
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
            "键鼠统计 · 战报",
            new Point(80, 64),
            64,
            text,
            PrimitiveText.DefaultFamily,
            FontWeight.SemiBold);
        PrimitiveText.DrawText(
            context,
            _day is null ? "暂无数据" : _day.Date.ToString("yyyy-MM-dd"),
            new Point(80, 150),
            30,
            muted,
            PrimitiveText.NumericFamily);

        DrawStats(context, text, muted);
        DrawHighlights(context, text, muted, accent);
        DrawApps(context, text, muted, accent);
    }

    private void DrawStats(DrawingContext context, IBrush text, IBrush muted)
    {
        (string Label, string Value)[] stats =
        [
            ("击键", FormatCount(_day?.Keys ?? 0)),
            ("点击", FormatCount(_day?.Clicks ?? 0)),
            ("活跃", FormatDuration(_day?.ActiveSeconds ?? 0)),
            ("移动", FormatDistance(_day?.MoveMeters ?? 0))
        ];

        double width = (Bounds.Width - 160 - 24) / 2;
        for (int i = 0; i < stats.Length; i++)
        {
            double x = 80 + (i % 2) * (width + 24);
            double y = 240 + (i / 2) * 220;
            CardGeometry card = Card.Layout(new Rect(x, y, width, 190), _theme, 20);
            Card.Draw(context, card, _theme);
            PrimitiveText.DrawText(context, stats[i].Label, new Point(card.Content.X, card.Content.Y + 8), 22, muted, PrimitiveText.DefaultFamily);
            PrimitiveText.DrawText(context, stats[i].Value, new Point(card.Content.X, card.Content.Y + 44), 58, text, "Segoe UI", FontWeight.SemiBold);
        }
    }

    private void DrawHighlights(DrawingContext context, IBrush text, IBrush muted, IBrush accent)
    {
        CardGeometry card = Card.Layout(new Rect(80, 710, Bounds.Width - 160, 430), _theme, 20);
        Card.Draw(context, card, _theme);
        PrimitiveText.DrawText(context, "今日高光", new Point(card.Content.X, card.Content.Y), 26, text, PrimitiveText.DefaultFamily, FontWeight.SemiBold);

        double longest = _day?.Sessions.Count > 0 ? _day.Sessions.Max(session => session.Seconds) : 0;
        int peakHour = 0;
        if (_day is not null)
        {
            for (int hour = 1; hour < 24; hour++)
            {
                if (_day.HourKeys[hour] > _day.HourKeys[peakHour])
                {
                    peakHour = hour;
                }
            }
        }

        (string Label, string Value)[] rows =
        [
            ("最长专注", FormatDuration(longest)),
            ("峰值 APM", (_day?.PeakApm ?? 0).ToString()),
            ("应用切换", (_day?.AppSwitches ?? 0).ToString()),
            ("最活跃小时", $"{peakHour:00}:00 · {_day?.HourKeys[peakHour] ?? 0} 键"),
            ("滚轮事件", (_day?.Wheel ?? 0).ToString("N0"))
        ];

        for (int i = 0; i < rows.Length; i++)
        {
            double y = card.Content.Y + 58 + i * 56;
            PrimitiveText.DrawText(context, rows[i].Label, new Point(card.Content.X, y), 18, muted, PrimitiveText.DefaultFamily);
            PrimitiveText.DrawText(context, rows[i].Value, new Point(card.Content.X + 300, y), 20, i == 0 ? accent : text, PrimitiveText.DefaultFamily, FontWeight.SemiBold);
        }
    }

    private void DrawApps(DrawingContext context, IBrush text, IBrush muted, IBrush accent)
    {
        int count = Math.Clamp(_day?.Apps.Count ?? 0, 0, MaxApps);
        double cardHeight = Math.Max(1100, Bounds.Height - 1190 - 80);
        CardGeometry card = Card.Layout(new Rect(80, 1190, Bounds.Width - 160, cardHeight), _theme, 20);
        Card.Draw(context, card, _theme);
        PrimitiveText.DrawText(context, $"应用 Top {Math.Max(1, count)}", new Point(card.Content.X, card.Content.Y), 26, text, PrimitiveText.DefaultFamily, FontWeight.SemiBold);

        if (_day is null)
        {
            return;
        }

        int index = 0;
        double max = Math.Max(1, _day.Apps.Values.Max(app => app.ActiveSeconds));
        foreach (AppUsage app in _day.Apps.Values.OrderByDescending(value => value.ActiveSeconds).Take(MaxApps))
        {
            double y = card.Content.Y + 62 + index * 82;
            string name = _desensitize ? $"应用 {index + 1:00}" : app.AppName;
            PrimitiveText.DrawText(context, $"{index + 1}. {name}", new Point(card.Content.X, y), 20, text, PrimitiveText.DefaultFamily, maxWidth: 500);
            PrimitiveText.DrawText(
                context,
                $"{FormatDuration(app.ActiveSeconds)} · {FormatCount(app.Keys)} 键 · {FormatCount(app.Clicks)} 击",
                new Point(card.Content.X + 520, y + 2),
                16,
                muted,
                PrimitiveText.NumericFamily);

            double barWidth = Math.Max(2, 520.0 * app.ActiveSeconds / max);
            context.FillRectangle(accent, new Rect(card.Content.X, y + 32, barWidth, 8), 4);
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
