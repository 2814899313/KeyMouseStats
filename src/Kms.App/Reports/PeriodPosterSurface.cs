using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Kms.Core.Model;
using Kms.Render.Primitives;
using Kms.Render.Tokens;
using PrimitiveText = Kms.Render.Primitives.TextBlock;

namespace Kms.App.Reports;

/// <summary>周报/月报海报：1440×1800 DIP，导出 3×。</summary>
internal sealed class PeriodPosterSurface : Control
{
    public const int PosterWidth = 1440;
    public const int PosterHeight = 1800;

    private readonly List<DayRecord> _days;
    private readonly DateTime _from;
    private readonly DateTime _to;
    private readonly string _title;
    private readonly ThemeTokens _theme;

    public PeriodPosterSurface(
        IEnumerable<DayRecord> days,
        DateTime from,
        DateTime to,
        string title,
        ThemeTokens theme)
    {
        _days = days.Where(day => day.Date >= from && day.Date <= to).OrderBy(day => day.Date).ToList();
        _from = from;
        _to = to;
        _title = title;
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
            _title,
            new Point(80, 64),
            64,
            text,
            PrimitiveText.DefaultFamily,
            FontWeight.SemiBold);
        PrimitiveText.DrawText(
            context,
            $"{_from:yyyy-MM-dd} — {_to:yyyy-MM-dd} · {_days.Count} 天",
            new Point(80, 146),
            28,
            muted,
            PrimitiveText.NumericFamily);

        long keys = _days.Sum(day => day.Keys);
        long clicks = _days.Sum(day => day.Clicks);
        double meters = _days.Sum(day => day.MoveMeters);
        double active = _days.Sum(day => day.ActiveSeconds);
        DrawStats(context, text, muted, keys, clicks, meters, active);
        DrawDaily(context, text, muted);
        DrawApps(context, text, muted);
    }

    private void DrawStats(
        DrawingContext context,
        IBrush text,
        IBrush muted,
        long keys,
        long clicks,
        double meters,
        double active)
    {
        (string Label, string Value)[] stats =
        [
            ("击键", FormatCount(keys)),
            ("点击", FormatCount(clicks)),
            ("移动", FormatDistance(meters)),
            ("活跃", FormatDuration(active))
        ];

        double width = (Bounds.Width - 160 - 24) / 2;
        for (int i = 0; i < stats.Length; i++)
        {
            double x = 80 + (i % 2) * (width + 24);
            double y = 230 + (i / 2) * 220;
            CardGeometry card = Card.Layout(new Rect(x, y, width, 190), _theme, 20);
            Card.Draw(context, card, _theme);
            PrimitiveText.DrawText(context, stats[i].Label, new Point(card.Content.X, card.Content.Y + 8), 22, muted, PrimitiveText.DefaultFamily);
            PrimitiveText.DrawText(context, stats[i].Value, new Point(card.Content.X, card.Content.Y + 44), 58, text, "Segoe UI", FontWeight.SemiBold);
        }
    }

    private void DrawDaily(DrawingContext context, IBrush text, IBrush muted)
    {
        CardGeometry card = Card.Layout(new Rect(80, 690, Bounds.Width - 160, 470), _theme, 20);
        Card.Draw(context, card, _theme);
        PrimitiveText.DrawText(context, "每日击键", new Point(card.Content.X, card.Content.Y), 24, text, PrimitiveText.DefaultFamily, FontWeight.SemiBold);

        List<BarDatum> bars = new(_days.Count);
        foreach (DayRecord day in _days)
        {
            bars.Add(new BarDatum(day.Keys, day.Date.ToString("MM-dd"), _theme.Accent));
        }

        Rect plot = new(card.Content.X, card.Content.Y + 48, card.Content.Width, card.Content.Height - 70);
        BarChartGeometry geometry = BarChart.Measure(
            plot,
            bars,
            new BarChartOptions(Padding: 0, GapRatio: 0.2, IncludeZero: true, YTickCount: 3));
        BarChart.Draw(context, geometry, _theme, showBaseline: true, cornerRadius: 1);

        for (int i = 0; i < geometry.Bars.Count; i++)
        {
            if (_days.Count > 14 && i % 5 != 0)
            {
                continue;
            }

            BarGeometry bar = geometry.Bars[i];
            PrimitiveText.DrawText(
                context,
                _days[i].Date.ToString("MM-dd"),
                new Point(bar.Slot.Center.X - 16, plot.Bottom + 8),
                12,
                muted,
                PrimitiveText.NumericFamily,
                maxWidth: 44);
        }
    }

    private void DrawApps(DrawingContext context, IBrush text, IBrush muted)
    {
        CardGeometry card = Card.Layout(new Rect(80, 1210, Bounds.Width - 160, 470), _theme, 20);
        Card.Draw(context, card, _theme);
        PrimitiveText.DrawText(context, "应用时长 Top 8", new Point(card.Content.X, card.Content.Y), 24, text, PrimitiveText.DefaultFamily, FontWeight.SemiBold);

        Dictionary<string, double> apps = new(StringComparer.OrdinalIgnoreCase);
        foreach (DayRecord day in _days)
        {
            foreach (AppUsage app in day.Apps.Values)
            {
                string key = string.IsNullOrEmpty(app.ProcessPath) ? app.Title : app.ProcessPath;
                apps[key] = apps.GetValueOrDefault(key) + app.ActiveSeconds;
            }
        }

        int index = 0;
        foreach ((string path, double seconds) in apps.OrderByDescending(pair => pair.Value).Take(8))
        {
            double y = card.Content.Y + 52 + index * 46;
            PrimitiveText.DrawText(
                context,
                $"{index + 1}. {Path.GetFileName(path)}",
                new Point(card.Content.X, y),
                18,
                text,
                PrimitiveText.DefaultFamily,
                maxWidth: 780);
            PrimitiveText.DrawText(
                context,
                FormatDuration(seconds),
                new Point(card.Content.Right - 220, y),
                16,
                muted,
                PrimitiveText.NumericFamily,
                alignment: TextAlignment.Right,
                maxWidth: 220);
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
