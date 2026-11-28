using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Kms.App.Apps;
using Kms.App.Pages;
using Kms.App.Platform;
using Kms.Core.Model;
using Kms.Core.Stats;
using Kms.Core.Time;
using Kms.Input.Counting;
using Kms.Render.Primitives;
using Kms.Render.Tokens;

namespace Kms.App.Widgets;

internal sealed class HeatWidgetModel : WidgetPageModel
{
    public HeatWidgetModel(
        StatsData data,
        TimeScopeRange scope,
        AppPageContext? appContext = null)
    {
        AppContext = appContext ?? AppPageContext.All;
        Data = AppContext.FilterStats(data);
        Scope = scope;
        DayRecord rangeTotals = StatsRange.Build(Data, scope).Totals;
        Totals = AppContext.IsAll
            ? rangeTotals
            : AppScopeData.BuildProfileTotals(AppContext.Profile, rangeTotals);
        Monitors = DisplayLayout.GetMonitors();
        Matrix = HeatMatrix.Build(Totals, Channel, Monitors, FocusedScreen);
    }

    public AppPageContext AppContext { get; }

    public StatsData Data { get; }

    public TimeScopeRange Scope { get; }

    public DayRecord Totals { get; }

    public IReadOnlyList<DisplayRect> Monitors { get; }

    public int Channel { get; private set; }

    public int FocusedScreen { get; private set; } = -1;

    public bool Cloud { get; private set; }

    public HeatMatrix Matrix { get; private set; }

    public void SetChannel(int channel)
    {
        Channel = channel;
        Rebuild();
    }

    public void SetFocusedScreen(int screen)
    {
        FocusedScreen = screen >= 0 && screen < Monitors.Count ? screen : -1;
        Rebuild();
    }

    public void ToggleCloud()
    {
        Cloud = !Cloud;
        Rebuild();
    }

    private void Rebuild() => Matrix = HeatMatrix.Build(Totals, Channel, Monitors, FocusedScreen);
}

internal static class HeatWidgets
{
    private static HeatWidgetModel Resolve(WidgetContext context) =>
        context.ResolveModel("heat", () => new HeatWidgetModel(
            context.Data,
            context.Scope,
            context.AppPage ?? AppPageContext.Create(context.AppFilter, context.Profile)));

    public static WidgetView CreateKpiClicks(WidgetContext context)
    {
        HeatMatrix matrix = Resolve(context).Matrix;
        return MicroKpi.Card(
            "总点击量",
            matrix.Total.ToString("N0", CultureInfo.InvariantCulture),
            $"{matrix.Cells:N0} 个采样格",
            PageKit.T.Cyan);
    }

    public static WidgetView CreateKpiChannel(WidgetContext context)
    {
        HeatMatrix matrix = Resolve(context).Matrix;
        long[] channels = matrix.ChannelTotals();
        int top = 1;
        for (int channel = 2; channel < channels.Length; channel++)
        {
            if (channels[channel] > channels[top])
            {
                top = channel;
            }
        }

        long total = Math.Max(1, channels.Sum());
        return MicroKpi.Card(
            "主力点击通道",
            channels[top] > 0 ? HeatMatrix.ChannelName(top) : "--",
            channels[top] > 0 ? $"{channels[top] * 100.0 / total:0.#}% 点击" : "暂无点击",
            PageKit.T.Green);
    }

    public static WidgetView CreateKpiTopZone(WidgetContext context)
    {
        HeatMatrix matrix = Resolve(context).Matrix;
        long[] zones = matrix.Zones();
        int top = Array.IndexOf(zones, zones.Max());
        return MicroKpi.Card(
            "最热区域",
            zones[top] > 0 ? HeatMatrix.ZoneName(top) : "--",
            zones[top] > 0 ? $"{zones[top]:N0} 次点击" : "暂无采样",
            PageKit.T.Orange);
    }

    public static WidgetView CreateKpiCoverage(WidgetContext context)
    {
        HeatMatrix matrix = Resolve(context).Matrix;
        return MicroKpi.Card(
            "热区覆盖",
            matrix.Cells.ToString("N0", CultureInfo.InvariantCulture),
            "32 px 采样格",
            PageKit.T.Purple);
    }

    public static WidgetView CreateSummary(WidgetContext context)
    {
        HeatWidgetModel model = Resolve(context);
        HeatMatrix matrix = model.Matrix;
        long[] zones = matrix.Zones();
        int topZone = Array.IndexOf(zones, zones.Max());
        (int x, int y, long count) topCell = matrix.TopCells().FirstOrDefault();
        bool focused = matrix.FocusedScreen >= 0;
        Control body = PageKit.StatStripBody(
            ("总点击", matrix.AllTotal.ToString("N0"), PageKit.T.Cyan),
            (focused ? "本屏点击" : "覆盖格",
                focused ? matrix.Total.ToString("N0") : matrix.Cells.ToString("N0"), PageKit.T.Green),
            ("显示器", $"{matrix.MonitorCount} 屏", PageKit.T.Cyan),
            ("最热区域", zones[topZone] > 0 ? HeatMatrix.ZoneName(topZone) : "--", PageKit.T.Orange),
            ("最热格", topCell.count > 0 ? $"{topCell.x},{topCell.y} · {topCell.count:N0} 次" : "--", PageKit.T.Purple),
            ("通道", model.Channel == 0 ? "全部" : HeatMatrix.ChannelName(model.Channel), PageKit.T.Accent));
        return new WidgetView
        {
            Content = body,
            ShowHeader = false,
            ContentPadding = new Thickness(12),
            Elevated = true
        };
    }

    public static WidgetView CreateCanvas(WidgetContext context)
    {
        HeatWidgetModel model = Resolve(context);
        return new WidgetView
        {
            Content = new HeatCanvasView(model),
            HeaderHint = "所有通道汇总 · 色阶从冷到热",
            HeaderTrailing = PageKit.Badge("MAP", PageKit.T.Cyan),
            ContentPadding = new Thickness(16)
        };
    }

    public static WidgetView CreateZones(WidgetContext context)
    {
        HeatWidgetModel model = Resolve(context);
        bool focused = model.Matrix.FocusedScreen >= 0 && model.Matrix.FocusedScreen < model.Monitors.Count;
        return new WidgetView
        {
            Content = new HeatZonesView(model, context.Span),
            HeaderHint = focused
                ? "按所选屏幕位置三等分 · 九宫格按所选屏幕计算"
                : "按虚拟桌面位置三等分 · 跨屏全览",
            HeaderTrailing = PageKit.Badge("3 × 3", PageKit.T.Orange),
            ContentPadding = new Thickness(16)
        };
    }

    public static WidgetView CreateTopCells(WidgetContext context)
    {
        HeatWidgetModel model = Resolve(context);
        return new WidgetView
        {
            Content = new HeatTopCellsView(model),
            HeaderHint = "按点击次数排序 · 局部翻页",
            HeaderTrailing = PageKit.Badge("TOP", PageKit.T.Green),
            ContentPadding = new Thickness(12)
        };
    }

    public static WidgetView CreateChannels(WidgetContext context)
    {
        HeatWidgetModel model = Resolve(context);
        return new WidgetView
        {
            Content = new HeatChannelsView(model),
            HeaderHint = "五通道构成与采样摘要",
            HeaderTrailing = PageKit.Badge("MIX", PageKit.T.Purple),
            ContentPadding = new Thickness(12)
        };
    }
}

internal sealed class HeatCanvasView : UserControl
{
    private readonly HeatWidgetModel _model;
    private readonly HeatSurface _surface = new();
    private readonly Avalonia.Controls.TextBlock _caption = PageKit.Muted("", 9);

    public HeatCanvasView(HeatWidgetModel model)
    {
        _model = model;
        _surface.SetCloud(model.Cloud);
        _surface.SetData(model.Matrix);
        StackPanel body = new() { Spacing = 5 };
        body.Children.Add(_surface);
        body.Children.Add(_caption);
        Content = body;
        UpdateCaption();
        SizeChanged += (_, _) => _surface.Height = Math.Clamp(Bounds.Height - 22, 130, 520);
    }

    private void UpdateCaption()
    {
        HeatMatrix matrix = _model.Matrix;
        string screenText;
        if (matrix.FocusedScreen >= 0 && matrix.FocusedScreen < _model.Monitors.Count)
        {
            DisplayRect monitor = _model.Monitors[matrix.FocusedScreen];
            screenText = $"屏幕 {matrix.FocusedScreen + 1} · {monitor.Width}×{monitor.Height}";
        }
        else
        {
            screenText = $"{_model.Monitors.Count} 屏 · 跨屏全览";
        }

        _caption.Text = _model.Channel == 0
            ? $"32 px 采样格 · 屏幕物理像素 · {_model.Scope.Caption} · {screenText} · 所有通道汇总"
            : $"32 px 采样格 · {HeatMatrix.ChannelName(_model.Channel)} · {_model.Scope.Caption} · {screenText} · 仅保留最近 30 天";
    }
}

internal sealed class HeatZonesView : UserControl
{
    private readonly HeatWidgetModel _model;
    private readonly bool _compact;
    private readonly StackPanel _body = new() { Spacing = 6 };

    public HeatZonesView(HeatWidgetModel model, WidgetSpan span)
    {
        _model = model;
        _compact = span.Rows <= 2;
        Content = _body;
        Build();
        SizeChanged += (_, _) => Build();
    }

    private void Build()
    {
        _body.Children.Clear();
        HeatMatrix matrix = _model.Matrix;
        long[] zones = matrix.Zones();
        long maximum = Math.Max(1, zones.Max());
        bool focused = matrix.FocusedScreen >= 0;
        bool compact = _compact;
        string scope = focused && matrix.FocusedScreen < _model.Monitors.Count
            ? $"屏幕 {matrix.FocusedScreen + 1} · {_model.Monitors[matrix.FocusedScreen].Width}×{_model.Monitors[matrix.FocusedScreen].Height} · {matrix.Total:N0} 次"
            : $"跨屏 · {matrix.ContentWidthPx}×{matrix.ContentHeightPx} · {matrix.Total:N0} 次";
        _body.Children.Add(PageKit.Muted(scope, compact ? 8 : 9));

        double gridHeight = compact
            ? Math.Clamp(Bounds.Height - 46, 52, 88)
            : Math.Clamp(Bounds.Height * 0.45, 90, 210);
        Grid grid = new()
        {
            ColumnSpacing = compact ? 3 : 6,
            RowSpacing = compact ? 3 : 6,
            Height = gridHeight
        };
        for (int i = 0; i < 3; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            grid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));
        }

        int hottest = Array.IndexOf(zones, zones.Max());
        for (int index = 0; index < 9; index++)
        {
            double fraction = zones[index] / (double)maximum;
            Color tone = index == hottest ? PageKit.T.Orange : PageKit.T.Accent;
            Border cell = new()
            {
                Background = PageKit.Gradient(PageKit.Alpha(tone, 0.10 + 0.30 * fraction),
                    PageKit.Alpha(PageKit.T.Card, 0.9), 135),
                BorderBrush = PageKit.Brush(PageKit.Alpha(tone, 0.20 + 0.35 * fraction)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(compact ? 6 : 9),
                Padding = new Thickness(compact ? 3 : 7, compact ? 2 : 4),
                Child = new StackPanel
                {
                    Spacing = 0,
                    Children =
                    {
                        PageKit.Muted(HeatMatrix.ZoneName(index), compact ? 7.2 : 8.5),
                        PageKit.Label(zones[index].ToString("N0"), compact ? 10 : 13,
                            fraction > 0.6 ? tone : PageKit.T.Text, FontWeight.SemiBold),
                        PageKit.Muted($"{zones[index] * 100.0 / Math.Max(1, matrix.Total):0.#}%", compact ? 7.2 : 8.5)
                    }
                }
            };
            Grid.SetColumn(cell, index % 3);
            Grid.SetRow(cell, index / 3);
            grid.Children.Add(cell);
        }

        _body.Children.Add(grid);
        if (!compact && Bounds.Height >= 220)
        {
            int rank = 1;
            foreach ((long value, int index) in zones
                         .Select((value, index) => (value, index))
                         .Where(item => item.value > 0)
                         .OrderByDescending(item => item.value)
                         .Take(3))
            {
                _body.Children.Add(PageKit.StatRow(
                    $"{rank++}  {HeatMatrix.ZoneName(index)}",
                    $"{value * 100.0 / Math.Max(1, matrix.Total):0.#}%",
                    rank == 2 ? PageKit.T.Orange : PageKit.T.Text));
            }
        }

        _body.Children.Add(PageKit.StatRow("覆盖格子", $"{matrix.Cells:N0}", PageKit.T.Green));
        if (focused && !compact)
        {
            _body.Children.Add(PageKit.StatRow("其他屏幕", $"{matrix.OtherScreensTotal:N0} 次", PageKit.T.Muted));
        }

        if (!compact)
        {
            _body.Children.Add(PageKit.Muted($"色阶 1 → {matrix.MaxCount:N0} 次 · 位置明细保留 30 天", 8.5));
        }
    }
}

internal sealed class HeatTopCellsView : UserControl
{
    private readonly HeatWidgetModel _model;
    private readonly PageKit.PagedList _list = new(3, 4);

    public HeatTopCellsView(HeatWidgetModel model)
    {
        _model = model;
        Content = _list;
        Build();
        SizeChanged += (_, _) =>
        {
            int size = Bounds.Height > 0 ? Math.Clamp((int)((Bounds.Height - 24) / 38), 1, 8) : 3;
            _list.SetPageSize(size);
        };
    }

    private void Build()
    {
        List<(int X, int Y, long Count)> cells = _model.Matrix.TopCells().Take(8).ToList();
        long max = Math.Max(1, cells.Count == 0 ? 1 : cells[0].Count);
        List<Control> rows = [];
        int rank = 1;
        foreach ((int x, int y, long count) in cells)
        {
            rows.Add(PageKit.RankRow(
                rank++,
                $"格 {x}, {y}",
                count.ToString("N0"),
                count / (double)max,
                rank switch
                {
                    2 => PageKit.T.Orange,
                    3 => PageKit.T.Accent,
                    4 => PageKit.T.Green,
                    _ => PageKit.T.Cyan
                },
                "次点击"));
        }

        if (rows.Count == 0)
        {
            rows.Add(PageKit.Empty("暂无点击采样"));
        }

        _list.SetItems(rows);
    }
}

internal sealed class HeatChannelsView : UserControl
{
    private readonly HeatWidgetModel _model;
    private readonly StackPanel _body = new() { Spacing = 3 };

    public HeatChannelsView(HeatWidgetModel model)
    {
        _model = model;
        Content = _body;
        long[] channels = model.Matrix.ChannelTotals();
        long total = Math.Max(1, channels.Sum());
        Color[] colors = [PageKit.T.Accent, PageKit.T.Green, PageKit.T.Orange, PageKit.T.Purple, PageKit.T.Cyan];
        string[] labels = ["左键", "右键", "中键", "侧键 1", "侧键 2"];
        WrapPanel legend = new() { Orientation = Orientation.Horizontal };
        for (int i = 0; i < channels.Length; i++)
        {
            if (channels[i] <= 0)
            {
                continue;
            }

            legend.Children.Add(new Border
            {
                Margin = new Thickness(0, 0, 8, 2),
                Padding = new Thickness(7, 3),
                CornerRadius = new CornerRadius(20),
                Background = PageKit.Brush(PageKit.Alpha(colors[i], 0.10)),
                Child = PageKit.LegendDot(colors[i], labels[i], $"{channels[i] * 100.0 / total:0.#}%")
            });
        }

        _body.Children.Add(legend);
        long[] zones = model.Matrix.Zones();
        int topZone = Array.IndexOf(zones, zones.Max());
        _body.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14,
            Children =
            {
                PageKit.Muted($"最热区域 {HeatMatrix.ZoneName(topZone)}", 9),
                PageKit.Muted($"采样格 {model.Matrix.Cells:N0}", 9),
                PageKit.Muted($"总点击 {model.Matrix.Total:N0}", 9)
            }
        });
    }
}

internal sealed class HeatMatrix
{
    private const int MaxColumns = 1024;
    private const int MaxRows = 512;

    public long[,] Values { get; private init; } = new long[1, 1];
    public long Total { get; private set; }
    public long AllTotal { get; private init; }
    public long OtherScreensTotal { get; private init; }
    public long MaxCount { get; private init; }
    public long Cells { get; private set; }
    public int CellX { get; private set; }
    public int CellY { get; private set; }
    public int CellScale { get; private init; } = 1;
    public int ContentWidthPx { get; private init; } = ClickHeat.Cell;
    public int ContentHeightPx { get; private init; } = ClickHeat.Cell;
    public int FocusedScreen { get; private init; } = -1;
    public long[] ScreenTotals { get; private init; } = [];
    public IReadOnlyList<DisplayRect> Monitors { get; private init; } = [];
    public long[] Channels { get; private init; } = new long[5];

    public int MonitorCount => Monitors.Count;

    public static HeatMatrix Build(
        DayRecord day,
        int channel,
        IReadOnlyList<DisplayRect> monitors,
        int focusedScreen)
    {
        long[] channelTotals = new long[5];
        foreach ((long key, long count) in day.ClickCellsV1)
        {
            int keyChannel = (int)(key >> 32);
            if (keyChannel is >= 1 and <= 4)
            {
                channelTotals[keyChannel] += count;
            }
        }

        if (channelTotals.Sum() == 0)
        {
            channelTotals[1] = day.ClickCells.Values.Sum();
        }

        List<(int X, int Y, long Count)> cells = [];
        if (channel == 0)
        {
            foreach ((int key, long count) in day.ClickCells)
            {
                cells.Add((DecodeX(key), DecodeY(key), count));
            }
        }
        else
        {
            foreach ((long key, long count) in day.ClickCellsV1)
            {
                if ((int)(key >> 32) != channel)
                {
                    continue;
                }

                int cell = (int)(key & 0xFFFFFFFF);
                cells.Add((DecodeX(cell), DecodeY(cell), count));
            }
        }

        bool focus = focusedScreen >= 0
            && focusedScreen < monitors.Count
            && monitors[focusedScreen].IsValid;
        int focusScreen = focus ? focusedScreen : -1;

        long allTotal = 0;
        long[] screenTotals = new long[monitors.Count];
        foreach ((int x, int y, long count) in cells)
        {
            allTotal += count;
            int index = ScreenAt(monitors, x * ClickHeat.Cell + ClickHeat.Cell / 2,
                y * ClickHeat.Cell + ClickHeat.Cell / 2);
            if (index >= 0)
            {
                screenTotals[index] += count;
            }
        }

        if (cells.Count == 0 && monitors.Count == 0)
        {
            return new HeatMatrix { Channels = channelTotals };
        }

        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = int.MinValue;
        int maxY = int.MinValue;
        if (focus)
        {
            DisplayRect monitor = monitors[focusScreen];
            minX = FloorDiv(monitor.X, ClickHeat.Cell);
            minY = FloorDiv(monitor.Y, ClickHeat.Cell);
            maxX = FloorDiv(monitor.Right - 1, ClickHeat.Cell);
            maxY = FloorDiv(monitor.Bottom - 1, ClickHeat.Cell);
        }
        else
        {
            foreach ((int x, int y, _) in cells)
            {
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }

            foreach (DisplayRect monitor in monitors)
            {
                if (!monitor.IsValid)
                {
                    continue;
                }

                minX = Math.Min(minX, FloorDiv(monitor.X, ClickHeat.Cell));
                minY = Math.Min(minY, FloorDiv(monitor.Y, ClickHeat.Cell));
                maxX = Math.Max(maxX, FloorDiv(monitor.Right - 1, ClickHeat.Cell));
                maxY = Math.Max(maxY, FloorDiv(monitor.Bottom - 1, ClickHeat.Cell));
            }
        }

        if (minX == int.MaxValue || minY == int.MaxValue || maxX < minX || maxY < minY)
        {
            return new HeatMatrix
            {
                Channels = channelTotals,
                AllTotal = allTotal,
                ScreenTotals = screenTotals,
                Monitors = monitors
            };
        }

        int rawWidth = Math.Max(1, maxX - minX + 1);
        int rawHeight = Math.Max(1, maxY - minY + 1);
        int scale = 1;
        while ((rawWidth + scale - 1) / scale > MaxColumns
               || (rawHeight + scale - 1) / scale > MaxRows)
        {
            scale++;
        }

        int width = (rawWidth + scale - 1) / scale;
        int height = (rawHeight + scale - 1) / scale;
        long[,] matrix = new long[height, width];
        long total = 0;
        long used = 0;
        long maxCount = 0;
        foreach ((int x, int y, long count) in cells)
        {
            if (focus && ScreenAt(monitors, x * ClickHeat.Cell + ClickHeat.Cell / 2,
                    y * ClickHeat.Cell + ClickHeat.Cell / 2) != focusScreen)
            {
                continue;
            }

            int column = (x - minX) / scale;
            int row = (y - minY) / scale;
            if ((uint)column >= (uint)width || (uint)row >= (uint)height)
            {
                continue;
            }

            matrix[row, column] += count;
            total += count;
            used++;
            maxCount = Math.Max(maxCount, count);
        }

        return new HeatMatrix
        {
            Values = matrix,
            Total = total,
            AllTotal = allTotal,
            OtherScreensTotal = Math.Max(0, allTotal - total),
            MaxCount = maxCount,
            Cells = used,
            CellX = minX,
            CellY = minY,
            CellScale = scale,
            ContentWidthPx = rawWidth * ClickHeat.Cell,
            ContentHeightPx = rawHeight * ClickHeat.Cell,
            FocusedScreen = focusScreen,
            ScreenTotals = screenTotals,
            Monitors = monitors,
            Channels = channelTotals
        };
    }

    public long[] Zones()
    {
        long[] zones = new long[9];
        int height = Values.GetLength(0);
        int width = Values.GetLength(1);
        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < width; column++)
            {
                long value = Values[row, column];
                if (value <= 0)
                {
                    continue;
                }

                int zoneRow = Math.Min(2, row * 3 / Math.Max(1, height));
                int zoneColumn = Math.Min(2, column * 3 / Math.Max(1, width));
                zones[zoneRow * 3 + zoneColumn] += value;
            }
        }

        return zones;
    }

    public long[] ChannelTotals() => Channels;

    public IEnumerable<(int X, int Y, long Count)> TopCells()
    {
        List<(int X, int Y, long Count)> cells = [];
        for (int row = 0; row < Values.GetLength(0); row++)
        {
            for (int column = 0; column < Values.GetLength(1); column++)
            {
                long value = Values[row, column];
                if (value > 0)
                {
                    cells.Add((CellX + column * CellScale, CellY + row * CellScale, value));
                }
            }
        }

        return cells.OrderByDescending(cell => cell.Count).ThenBy(cell => cell.X).ThenBy(cell => cell.Y);
    }

    public static string ChannelName(int channel) => channel switch
    {
        1 => "左键",
        2 => "右键",
        3 => "中键",
        4 => "侧键",
        _ => "全部"
    };

    public static string ZoneName(int index) => index switch
    {
        0 => "左上", 1 => "上中", 2 => "右上",
        3 => "左中", 4 => "正中", 5 => "右中",
        6 => "左下", 7 => "下中", _ => "右下"
    };

    private static int ScreenAt(IReadOnlyList<DisplayRect> monitors, int x, int y)
    {
        for (int i = 0; i < monitors.Count; i++)
        {
            DisplayRect monitor = monitors[i];
            if (monitor.IsValid
                && x >= monitor.X && x < monitor.Right
                && y >= monitor.Y && y < monitor.Bottom)
            {
                return i;
            }
        }

        return -1;
    }

    private static int FloorDiv(int value, int divisor) =>
        value >= 0
            ? value / divisor
            : -((-value + divisor - 1) / divisor);

    private static int DecodeX(int key) => ((key >> 16) & 0xFFFF) - 32768;

    private static int DecodeY(int key) => (key & 0xFFFF) - 32768;
}

internal sealed class HeatSurface : Control
{
    private long[,] _counts = new long[1, 1];
    private IReadOnlyList<DisplayRect> _monitors = [];
    private int _originCellX;
    private int _originCellY;
    private int _cellScale = 1;
    private int _contentWidth = ClickHeat.Cell;
    private int _contentHeight = ClickHeat.Cell;
    private int _focusedScreen = -1;
    private bool _cloud;

    public HeatSurface()
    {
        RenderOptions.SetBitmapInterpolationMode(this, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
    }

    public void SetData(HeatMatrix matrix)
    {
        _counts = matrix.Values;
        _monitors = matrix.Monitors;
        _originCellX = matrix.CellX;
        _originCellY = matrix.CellY;
        _cellScale = Math.Max(1, matrix.CellScale);
        _contentWidth = Math.Max(1, matrix.ContentWidthPx);
        _contentHeight = Math.Max(1, matrix.ContentHeightPx);
        _focusedScreen = matrix.FocusedScreen;
        InvalidateVisual();
    }

    public void SetCloud(bool cloud)
    {
        _cloud = cloud;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_counts.Length == 0)
        {
            return;
        }

        Rect plot = FitAspect(new Rect(0, 0, Bounds.Width, Bounds.Height));
        HeatGridGeometry geometry = HeatGrid.Measure(plot, _counts, PageKit.T, HeatScale.All, gap: 1.5);
        if (!_cloud)
        {
            HeatGrid.Draw(context, geometry);
            DrawMonitors(context, geometry);
            return;
        }

        HeatCell[] dim = new HeatCell[geometry.Cells.Count];
        for (int i = 0; i < dim.Length; i++)
        {
            HeatCell cell = geometry.Cells[i];
            Color fill = cell.Fill;
            dim[i] = new HeatCell(cell.Row, cell.Column, cell.Count, cell.Bounds,
                Color.FromArgb((byte)(fill.A * 0.55), fill.R, fill.G, fill.B));
        }

        HeatGrid.Draw(context, geometry with { Cells = dim }, 1.5);
        RenderCloud(context, plot);
        DrawMonitors(context, geometry);
    }

    private Rect FitAspect(Rect bounds)
    {
        const double padding = 8;
        Rect area = new(bounds.X + padding, bounds.Y + padding,
            Math.Max(1, bounds.Width - padding * 2), Math.Max(1, bounds.Height - padding * 2));
        double scale = Math.Min(area.Width / _contentWidth, area.Height / _contentHeight);
        if (!double.IsFinite(scale) || scale <= 0)
        {
            scale = 1;
        }

        double width = Math.Max(1, _contentWidth * scale);
        double height = Math.Max(1, _contentHeight * scale);
        return new Rect(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);
    }

    private void DrawMonitors(DrawingContext context, HeatGridGeometry geometry)
    {
        if (_monitors.Count == 0 || geometry.Columns <= 0 || geometry.Rows <= 0)
        {
            return;
        }

        double stepX = geometry.CellWidth + geometry.Gap;
        double stepY = geometry.CellHeight + geometry.Gap;
        if (stepX <= 0 || stepY <= 0)
        {
            return;
        }

        IBrush brush = PageKit.Brush(PageKit.Alpha(PageKit.T.Cyan, 0.55));
        IPen pen = new Pen(brush, 1);
        for (int index = 0; index < _monitors.Count; index++)
        {
            if (_focusedScreen >= 0 && index != _focusedScreen)
            {
                continue;
            }

            DisplayRect monitor = _monitors[index];
            if (!monitor.IsValid)
            {
                continue;
            }

            double left = (monitor.X / (double)ClickHeat.Cell - _originCellX) / _cellScale;
            double right = (monitor.Right / (double)ClickHeat.Cell - _originCellX) / _cellScale;
            double top = (monitor.Y / (double)ClickHeat.Cell - _originCellY) / _cellScale;
            double bottom = (monitor.Bottom / (double)ClickHeat.Cell - _originCellY) / _cellScale;
            double x0 = geometry.Bounds.X + left * stepX;
            double x1 = geometry.Bounds.X + right * stepX - geometry.Gap;
            double y0 = geometry.Bounds.Y + top * stepY;
            double y1 = geometry.Bounds.Y + bottom * stepY - geometry.Gap;
            Rect rect = new(
                Math.Max(geometry.Bounds.Left, x0),
                Math.Max(geometry.Bounds.Top, y0),
                Math.Max(0, Math.Min(geometry.Bounds.Right, x1) - Math.Max(geometry.Bounds.Left, x0)),
                Math.Max(0, Math.Min(geometry.Bounds.Bottom, y1) - Math.Max(geometry.Bounds.Top, y0)));
            if (rect.Width <= 1 || rect.Height <= 1)
            {
                continue;
            }

            context.DrawRectangle(null, pen, rect, 8);
            if (rect.Width >= 54)
            {
                Kms.Render.Primitives.TextBlock.DrawText(
                    context, $"屏幕 {index + 1}", new Point(rect.Left + 6, rect.Top + 4), 9,
                    PageKit.Brush(PageKit.Alpha(PageKit.T.Text, 0.88)),
                    Kms.Render.Primitives.TextBlock.DefaultFamily, maxWidth: Math.Min(80, rect.Width - 8));
            }
        }
    }

    private void RenderCloud(DrawingContext context, Rect bounds)
    {
        ThemeTokens theme = PageKit.T;
        int rows = _counts.GetLength(0);
        int columns = _counts.GetLength(1);
        if (rows <= 0 || columns <= 0)
        {
            return;
        }

        double[,] smooth = Smooth(_counts, rows, columns);
        double max = 1;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                max = Math.Max(max, smooth[r, c]);
            }
        }

        using var bitmap = new Avalonia.Media.Imaging.WriteableBitmap(
            new PixelSize(columns, rows), new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Unpremul);
        using (var frame = bitmap.Lock())
        {
            byte[] pixels = new byte[frame.RowBytes * frame.Size.Height];
            for (int r = 0; r < rows; r++)
            {
                int rowOffset = r * frame.RowBytes;
                for (int c = 0; c < columns; c++)
                {
                    double value = smooth[r, c];
                    if (value <= 0.01)
                    {
                        continue;
                    }

                    double fraction = max <= 1 ? 0 : Math.Clamp(Math.Log(1 + value) / Math.Log(1 + max), 0, 1);
                    if (fraction < 0.08)
                    {
                        continue;
                    }

                    Color color = HeatScale.For(theme, HeatScale.All,
                        (long)Math.Round(value * 1000), (long)Math.Round(max * 1000));
                    int offset = rowOffset + c * 4;
                    pixels[offset] = color.B;
                    pixels[offset + 1] = color.G;
                    pixels[offset + 2] = color.R;
                    pixels[offset + 3] = (byte)Math.Clamp(175 * Math.Pow((fraction - 0.08) / 0.92, 1.2), 0, 255);
                }
            }

            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, frame.Address, pixels.Length);
        }

        context.DrawImage(bitmap, bounds);
    }

    private static double[,] Smooth(long[,] counts, int rows, int columns)
    {
        double[,] result = new double[rows, columns];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                double sum = 0;
                double weight = 0;
                for (int dr = -2; dr <= 2; dr++)
                {
                    int rr = r + dr;
                    if (rr < 0 || rr >= rows)
                    {
                        continue;
                    }

                    for (int dc = -2; dc <= 2; dc++)
                    {
                        int cc = c + dc;
                        if (cc < 0 || cc >= columns)
                        {
                            continue;
                        }

                        double w = (3 - Math.Abs(dr)) * (3 - Math.Abs(dc));
                        sum += counts[rr, cc] * w;
                        weight += w;
                    }
                }

                result[r, c] = weight > 0 ? sum / weight : 0;
            }
        }

        return result;
    }
}
