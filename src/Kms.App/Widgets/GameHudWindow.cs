using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Kms.App.Overlays;
using Kms.Core.Model;
using Kms.Render.Primitives;
using Kms.Render.Theming;
using Kms.Render.Tokens;
using PrimitiveText = Kms.Render.Primitives.TextBlock;

namespace Kms.App.Widgets;

/// <summary>M5 / T5.4 GameHud 首版：330×92 点击穿透 HUD。</summary>
internal sealed class GameHudWindow : OverlayWindow
{
    private readonly LoadedStats _stats;
    private readonly HudSurface _surface;
    private bool _editing;

    public GameHudWindow(LoadedStats stats, Func<long>? liveApm = null)
    {
        _stats = stats;
        Width = 330;
        Height = 92;
        Title = "游戏 HUD";
        _surface = new HudSurface(stats, liveApm);
        Content = _surface;
        PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            {
                ToggleEdit();
                e.Handled = true;
                return;
            }

            if (_editing)
            {
                EnableDrag(e);
            }
        };
        PointerReleased += (_, _) => SavePosition();

        DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => _surface.InvalidateVisual();
        timer.Start();
    }

    public void ToggleEdit()
    {
        _editing = !_editing;
        SetClickThrough(!_editing);
        _surface.SetEditing(_editing);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        SetClickThrough(true);
        if (Screens.Primary is { } screen)
        {
            PixelRect area = screen.WorkingArea;
            int x = ReadSetting("hud_x", area.X + 24);
            int y = ReadSetting("hud_y", area.Y + 24);
            Position = new PixelPoint(
                Math.Clamp(x, area.X, Math.Max(area.X, area.Right - (int)Width)),
                Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - (int)Height)));
        }
    }

    private int ReadSetting(string key, int fallback) =>
        int.TryParse(_stats.Data.Settings.GetValueOrDefault(key), out int value) ? value : fallback;

    private void SavePosition()
    {
        if (!_editing)
        {
            return;
        }

        _stats.Data.Settings["hud_x"] = Position.X.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _stats.Data.Settings["hud_y"] = Position.Y.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed class HudSurface : Control
    {
        private readonly LoadedStats _stats;
        private readonly Func<long>? _liveApm;
        private bool _editing;

        public HudSurface(LoadedStats stats, Func<long>? liveApm)
        {
            _stats = stats;
            _liveApm = liveApm;
        }

        public void SetEditing(bool editing)
        {
            _editing = editing;
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            ThemeTokens theme = ThemeManager.Current;
            DayRecord? day = Latest();
            Rect bounds = new(0, 0, Bounds.Width, Bounds.Height);
            CardGeometry card = Card.Layout(bounds, theme, padding: 12, radius: 10);
            Card.Draw(context, card, theme);

            IBrush text = new SolidColorBrush(theme.Text);
            IBrush muted = new SolidColorBrush(theme.Muted);
            IBrush accent = new SolidColorBrush(theme.Accent);

            PrimitiveText.DrawText(
                context,
                "键鼠统计 · HUD",
                new Point(card.Content.X, card.Content.Y),
                10,
                muted,
                PrimitiveText.DefaultFamily);

            DrawMetric(context, "击键", day?.Keys ?? 0, card.Content.X, card.Content.Y + 22, text, accent);
            DrawMetric(context, "点击", day?.Clicks ?? 0, card.Content.X + 105, card.Content.Y + 22, text, accent);
            DrawMetric(context, "APM", _liveApm?.Invoke() ?? day?.PeakApm ?? 0, card.Content.X + 210, card.Content.Y + 22, text, accent);

            if (_editing)
            {
                PrimitiveText.DrawText(
                    context,
                    "编辑中 · 拖动 / 右键退出",
                    new Point(card.Content.X, card.Content.Bottom - 12),
                    8,
                    accent,
                    PrimitiveText.DefaultFamily);
            }
        }

        private static void DrawMetric(
            DrawingContext context,
            string label,
            long value,
            double x,
            double y,
            IBrush text,
            IBrush accent)
        {
            PrimitiveText.DrawText(
                context,
                value >= 1000 ? (value / 1000.0).ToString("0.#") + "k" : value.ToString(),
                new Point(x, y),
                24,
                text,
                "Segoe UI",
                FontWeight.SemiBold);
            PrimitiveText.DrawText(
                context,
                label,
                new Point(x, y + 30),
                9,
                accent,
                PrimitiveText.DefaultFamily);
        }

        private DayRecord? Latest()
        {
            string key = DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            return _stats.Data.History.TryGetValue(key, out DayRecord? day)
                ? day
                : _stats.Data.History.Values.OrderByDescending(value => value.Date).FirstOrDefault();
        }
    }
}
