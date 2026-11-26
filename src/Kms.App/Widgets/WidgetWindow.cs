using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Kms.App.Overlays;
using Kms.Core.Model;
using Kms.Render.Primitives;
using Kms.Render.Theming;
using Kms.Render.Tokens;
using PrimitiveText = Kms.Render.Primitives.TextBlock;

namespace Kms.App.Widgets;

/// <summary>M5 / T5.1 悬浮窗首版：四模式、拖动、右键切模式、置顶不抢焦点。</summary>
internal sealed class WidgetWindow : OverlayWindow
{
    private readonly WidgetSurface _surface;
    private int _mode;

    public WidgetWindow(LoadedStats stats)
    {
        Width = 248;
        Height = 216;
        Title = "键鼠统计悬浮窗";

        _surface = new WidgetSurface(stats, ThemeManager.Current);
        Content = _surface;
        PointerEntered += (_, _) => _surface.SetHover(true);
        PointerExited += (_, _) => _surface.SetHover(false);

        PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            {
                CycleMode();
                e.Handled = true;
                return;
            }

            EnableDrag(e);
        };
        PointerWheelChanged += (_, e) =>
        {
            CycleMode();
            e.Handled = true;
        };

        DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => _surface.InvalidateVisual();
        timer.Start();
    }

    public int Mode => _mode;

    public void CycleMode()
    {
        _mode = (_mode + 1) % 4;
        _surface.SetMode(_mode);
    }

    public void SetMode(int mode)
    {
        _mode = Math.Clamp(mode, 0, 3);
        _surface.SetMode(_mode);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (Screens.Primary is { } screen)
        {
            PixelRect area = screen.WorkingArea;
            Position = new PixelPoint(
                Math.Max(area.X, area.Right - 280),
                Math.Max(area.Y, area.Bottom - 300));
        }
    }

    private sealed class WidgetSurface : Control
    {
        private readonly LoadedStats _stats;
        private readonly ThemeTokens _theme;
        private int _mode;
        private bool _hover;

        public WidgetSurface(LoadedStats stats, ThemeTokens theme)
        {
            _stats = stats;
            _theme = theme;
        }

        public void SetMode(int mode)
        {
            _mode = mode;
            InvalidateVisual();
        }

        public void SetHover(bool hover)
        {
            if (_hover == hover)
            {
                return;
            }

            _hover = hover;
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            Rect bounds = new(0, 0, Bounds.Width, Bounds.Height);
            CardGeometry card = Card.Layout(bounds, _theme, padding: 16);
            Card.Draw(context, card, _theme);

            IBrush text = new SolidColorBrush(_theme.Text);
            IBrush muted = new SolidColorBrush(_theme.Muted);
            IBrush accent = new SolidColorBrush(_theme.Accent);

            string modeName = _mode switch
            {
                1 => "目标",
                2 => "累计",
                3 => "存储",
                _ => "今日"
            };
            PrimitiveText.DrawText(
                context,
                $"{modeName} · {DateTime.Today:MM-dd}",
                new Point(card.Content.X, card.Content.Y),
                12,
                muted,
                PrimitiveText.DefaultFamily);

            DayRecord? day = ResolveDay(_stats.Data);
            if (_mode == 1)
            {
                DrawTargets(context, day, card, text, muted, accent);
                PrimitiveText.DrawText(
                    context,
                    "右键 / 滚轮切换模式",
                    new Point(card.Content.X, card.Content.Bottom - 18),
                    10,
                    muted,
                    PrimitiveText.DefaultFamily);
                return;
            }

            long keys = _mode == 2 ? _stats.Data.Totals.Keys : day?.Keys ?? 0;
            long clicks = _mode == 2 ? _stats.Data.Totals.Clicks : day?.Clicks ?? 0;

            if (_mode == 3)
            {
                PrimitiveText.DrawText(
                    context,
                    _stats.Source,
                    new Point(card.Content.X, card.Content.Y + 38),
                    20,
                    text,
                    PrimitiveText.DefaultFamily,
                    maxWidth: card.Content.Width);
                PrimitiveText.DrawText(
                    context,
                    _stats.Status,
                    new Point(card.Content.X, card.Content.Y + 74),
                    12,
                    muted,
                    PrimitiveText.DefaultFamily,
                    maxWidth: card.Content.Width,
                    maxLines: 3);
                PrimitiveText.DrawText(
                    context,
                    "右键 / 滚轮切换模式",
                    new Point(card.Content.X, card.Content.Bottom - 18),
                    10,
                    muted,
                    PrimitiveText.DefaultFamily);
                return;
            }

            DrawBig(context, keys, "击键", card.Content.X, card.Content.Y + 26, text, accent);
            DrawBig(context, clicks, "点击", card.Content.X + card.Content.Width / 2, card.Content.Y + 26, text, accent);

            PrimitiveText.DrawText(
                context,
                $"滚轮 {day?.Wheel ?? 0} · 移动 {(day?.MoveMeters ?? 0):0.#} m",
                new Point(card.Content.X, card.Content.Y + 116),
                11,
                muted,
                PrimitiveText.DefaultFamily,
                maxWidth: card.Content.Width);

            if (_hover)
            {
                PrimitiveText.DrawText(
                    context,
                    $"APM {day?.PeakApm ?? 0} · 活跃 {FormatDuration(day?.ActiveSeconds ?? 0)} · {day?.Sessions.Count ?? 0} 段",
                    new Point(card.Content.X, card.Content.Y + 142),
                    10,
                    accent,
                    PrimitiveText.DefaultFamily,
                    maxWidth: card.Content.Width);
            }

            PrimitiveText.DrawText(
                context,
                "右键 / 滚轮切换模式",
                new Point(card.Content.X, card.Content.Bottom - 18),
                10,
                muted,
                PrimitiveText.DefaultFamily);
        }

        private void DrawTargets(
            DrawingContext context,
            DayRecord? day,
            CardGeometry card,
            IBrush text,
            IBrush muted,
            IBrush accent)
        {
            long keys = day?.Keys ?? 0;
            long clicks = day?.Clicks ?? 0;
            (long averageKeys, long averageClicks) = SevenDayAverage();
            long targetKeys = ReadTarget("widget_target_keys", averageKeys > 0 ? averageKeys : 10_000);
            long targetClicks = ReadTarget("widget_target_clicks", averageClicks > 0 ? averageClicks : 3_000);

            DrawTargetRow(context, card, "击键", keys, targetKeys,
                card.Content.Y + 36, accent, text);
            DrawTargetRow(context, card, "点击", clicks, targetClicks,
                card.Content.Y + 94, new SolidColorBrush(_theme.Green), text);

            PrimitiveText.DrawText(
                context,
                $"目标 {FormatCompact(targetKeys)} 键 / {FormatCompact(targetClicks)} 击 · 7 日均值",
                new Point(card.Content.X, card.Content.Y + 150),
                9,
                muted,
                PrimitiveText.DefaultFamily,
                maxWidth: card.Content.Width);
        }

        private void DrawTargetRow(
            DrawingContext context,
            CardGeometry card,
            string label,
            long value,
            long target,
            double y,
            IBrush tone,
            IBrush text)
        {
            double ratio = target <= 0 ? 0 : Math.Clamp(value / (double)target, 0, 1);
            PrimitiveText.DrawText(
                context,
                $"{label} {value:N0}",
                new Point(card.Content.X, y),
                11,
                text,
                PrimitiveText.DefaultFamily);
            PrimitiveText.DrawText(
                context,
                $"{ratio * 100:0}%",
                new Point(card.Content.X, y),
                11,
                tone,
                PrimitiveText.DefaultFamily,
                maxWidth: card.Content.Width,
                alignment: TextAlignment.Right);

            context.FillRectangle(
                new SolidColorBrush(_theme.Raised),
                new Rect(card.Content.X, y + 19, card.Content.Width, 7),
                3.5f);
            double width = Math.Max(0, card.Content.Width * ratio);
            if (width > 0)
            {
                context.FillRectangle(tone, new Rect(card.Content.X, y + 19, width, 7), 3.5f);
            }
        }

        private long ReadTarget(string key, long fallback)
        {
            string? raw = _stats.Data.Settings.GetValueOrDefault(key);
            return long.TryParse(raw, out long value) && value > 0 ? value : fallback;
        }

        private (long Keys, long Clicks) SevenDayAverage()
        {
            DateTime today = DateTime.Today;
            List<DayRecord> recent = _stats.Data.History.Values
                .Where(day => day.Date < today)
                .OrderByDescending(day => day.Date)
                .Take(7)
                .ToList();
            if (recent.Count == 0)
            {
                return (0, 0);
            }

            return ((long)recent.Average(day => day.Keys), (long)recent.Average(day => day.Clicks));
        }

        private static string FormatCompact(long value) =>
            value >= 1_000_000 ? $"{value / 1_000_000.0:0.#}m"
            : value >= 1_000 ? $"{value / 1_000.0:0.#}k"
            : value.ToString();

        private static string FormatDuration(double seconds) =>
            seconds < 60 ? $"{seconds:0}s"
            : seconds < 3600 ? $"{seconds / 60:0.#} 分"
            : $"{seconds / 3600:0.##} 小时";

        private static void DrawBig(
            DrawingContext context,
            long value,
            string label,
            double x,
            double y,
            IBrush text,
            IBrush accent)
        {
            PrimitiveText.DrawText(
                context,
                value >= 1000 ? (value / 1000.0).ToString("0.#") + "k" : value.ToString(),
                new Point(x, y),
                34,
                text,
                "Segoe UI",
                FontWeight.SemiBold);
            PrimitiveText.DrawText(
                context,
                label,
                new Point(x, y + 44),
                11,
                accent,
                PrimitiveText.DefaultFamily);
        }

        private static DayRecord? ResolveDay(StatsData data)
        {
            DateTime today = DateTime.Today;
            string key = today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            if (data.History.TryGetValue(key, out DayRecord? todayRecord))
            {
                return todayRecord;
            }

            DayRecord? latest = null;
            foreach (DayRecord day in data.History.Values)
            {
                if (latest is null || day.Date > latest.Date)
                {
                    latest = day;
                }
            }

            return latest;
        }
    }
}
