using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Kms.App.Overlays;
using Kms.App.Pages;
using Kms.Core.Model;
using Kms.Render.Primitives;
using Kms.Render.Theming;
using Kms.Render.Tokens;
using PrimitiveText = Kms.Render.Primitives.TextBlock;

namespace Kms.App.Widgets;

/// <summary>M5 / T5.2 专注胶囊首版：常态/展开，两态都置顶且不抢焦点。</summary>
internal sealed class FocusPillWindow : OverlayWindow
{
    private readonly LoadedStats _stats;
    private readonly PillSurface _surface;
    private readonly DispatcherTimer _animTimer;
    private readonly FrameScheduler _motion;
    private double _pulsePhase;
    private bool _expanded;
    private bool _frozen;
    private DateTime _animStart;
    private double _fromWidth;
    private double _fromHeight;
    private double _toWidth;
    private double _toHeight;

    public FocusPillWindow(LoadedStats stats)
    {
        _stats = stats;
        Width = 220;
        Height = 48;
        Title = "专注胶囊";
        ClipToBounds = true;
        _surface = new PillSurface();
        Content = _surface;
        PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                Toggle();
                e.Handled = true;
            }
        };

        DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            if (IsVisible)
            {
                _surface.SetExpanded(_expanded, Today());
            }
        };
        timer.Start();

        _motion = new FrameScheduler();
        _motion.Tick += delta =>
        {
            _pulsePhase += delta * 4;
            _surface.SetPulse((Math.Sin(_pulsePhase) + 1) * 0.5);
        };

        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _animTimer.Tick += (_, _) => TickAnimation();
    }

    public void Toggle()
    {
        _expanded = !_expanded;
        _surface.SetExpanded(_expanded, Today());
        _fromWidth = double.IsNaN(Width) ? 220 : Width;
        _fromHeight = double.IsNaN(Height) ? 48 : Height;
        _toWidth = _expanded ? 320 : 220;
        _toHeight = _expanded ? 124 : 48;
        _animStart = DateTime.UtcNow;
        _animTimer.Start();
    }

    private void TickAnimation()
    {
        if (!IsVisible)
        {
            _animTimer.Stop();
            return;
        }

        double progress = Math.Clamp((DateTime.UtcNow - _animStart).TotalMilliseconds / 180.0, 0, 1);
        double eased = 1 - Math.Pow(1 - progress, 3);
        Width = _fromWidth + (_toWidth - _fromWidth) * eased;
        Height = _fromHeight + (_toHeight - _fromHeight) * eased;
        if (progress >= 1)
        {
            _animTimer.Stop();
        }
    }

    /// <summary>golden / 诊断：冻结呼吸与展开动画，渲染固定帧。</summary>
    internal void FreezeForGolden(bool expanded)
    {
        _frozen = true;
        _expanded = expanded;
        Width = expanded ? 320 : 220;
        Height = expanded ? 124 : 48;
        _surface.SetExpanded(expanded, Today());
        _surface.SetPulse(0.5);
        _motion.Stop();
        _animTimer.Stop();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _surface.SetExpanded(_expanded, Today());
        if (!_frozen)
        {
            _motion.Start();
        }
        if (Screens.Primary is { } screen)
        {
            PixelRect area = screen.WorkingArea;
            Position = new PixelPoint(area.X + 24, area.Y + 24);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _motion.Dispose();
        _animTimer.Stop();
        base.OnClosed(e);
    }

    private DayRecord? Today()
    {
        string key = DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return _stats.Data.History.TryGetValue(key, out DayRecord? day)
            ? day
            : _stats.Data.History.Values.OrderByDescending(value => value.Date).FirstOrDefault();
    }

    private sealed class PillSurface : Control
    {
        private bool _expanded;
        private DayRecord? _day;
        private double _pulse;

        public void SetExpanded(bool expanded, DayRecord? day)
        {
            _expanded = expanded;
            _day = day;
            InvalidateVisual();
        }

        public void SetPulse(double pulse)
        {
            _pulse = Math.Clamp(pulse, 0, 1);
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            ThemeTokens theme = ThemeManager.Current;
            Rect bounds = new(0, 0, Bounds.Width, Bounds.Height);
            CardGeometry card = Card.Layout(bounds, theme, padding: 10);
            Card.Draw(context, card, theme);

            IBrush text = new SolidColorBrush(theme.Text);
            IBrush muted = new SolidColorBrush(theme.Muted);
            IBrush accent = new SolidColorBrush(theme.Accent);

            context.DrawEllipse(
                new SolidColorBrush(PageKit.Alpha(theme.Accent, 0.20 + 0.55 * _pulse)),
                null,
                new Point(card.Content.X + 6, card.Content.Y + 11),
                3.5 + 2.0 * _pulse,
                3.5 + 2.0 * _pulse);

            PrimitiveText.DrawText(
                context,
                "专注中",
                new Point(card.Content.X + 15, card.Content.Y + 4),
                13,
                accent,
                PrimitiveText.DefaultFamily);

            if (!_expanded)
            {
                PrimitiveText.DrawText(
                    context,
                    FormatActive(_day),
                    new Point(card.Content.X + 78, card.Content.Y + 2),
                    16,
                    text,
                    "Segoe UI",
                    FontWeight.SemiBold);
                return;
            }

            PrimitiveText.DrawText(
                context,
                $"活跃 {FormatActive(_day)}",
                new Point(card.Content.X, card.Content.Y + 28),
                18,
                text,
                PrimitiveText.DefaultFamily);
            PrimitiveText.DrawText(
                context,
                $"击键 {_day?.Keys ?? 0} · 点击 {_day?.Clicks ?? 0}",
                new Point(card.Content.X, card.Content.Y + 56),
                12,
                muted,
                PrimitiveText.DefaultFamily);
            PrimitiveText.DrawText(
                context,
                "点击胶囊收起",
                new Point(card.Content.X, card.Content.Bottom - 14),
                10,
                muted,
                PrimitiveText.DefaultFamily);
        }

        private static string FormatActive(DayRecord? day)
        {
            double seconds = day?.ActiveSeconds ?? 0;
            if (seconds < 60)
            {
                return $"{seconds:0}s";
            }

            if (seconds < 3600)
            {
                return $"{seconds / 60:0} 分";
            }

            return $"{seconds / 3600:0.#} 小时";
        }
    }
}
