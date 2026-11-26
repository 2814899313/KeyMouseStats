using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Kms.Core.Model;
using Kms.Render.Primitives;
using Kms.Render.Tokens;

namespace Kms.App.Pages;

/// <summary>热区：点击格聚合、三通道筛选、九宫格与通道构成。</summary>
public sealed class HeatPage : UserControl
{
    private readonly DayRecord? _day;
    private readonly StackPanel _summary = new();
    private readonly StackPanel _filters = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly StackPanel _zones = new() { Spacing = 6 };
    private readonly StackPanel _channels = new() { Spacing = 6 };
    private readonly PageKit.PagedList _topCells = new(4, 4);
    private readonly Avalonia.Controls.TextBlock _caption;
    private readonly HeatSurface _surface;
    private int _channel;
    private bool _cloud;

    public HeatPage(StatsData data, string title, string subtitle)
    {
        _day = ResolveDay(data);
        _surface = new HeatSurface { Height = 270 };
        _surface.SetCloud(_cloud);
        _caption = PageKit.Muted("32 px 采样格 · 屏幕物理像素 · 只保留最近 30 天", 9);

        Grid root = new()
        {
            Margin = new Thickness(20, 16, 20, 12),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"),
            RowSpacing = 10
        };
        root.Children.Add(PageKit.PageHeader(
            "CLICK / HEATMAP",
            title,
            subtitle,
            _day?.Date.ToString("yyyy-MM-dd") ?? DateTime.Today.ToString("yyyy-MM-dd")));

        Grid.SetRow(_summary, 1);
        root.Children.Add(_summary);
        Grid.SetRow(_filters, 2);
        root.Children.Add(_filters);

        Grid main = new()
        {
            ColumnDefinitions = new ColumnDefinitions("1.65*,1*"),
            ColumnSpacing = 14
        };

        StackPanel heatBody = new() { Spacing = 6 };
        heatBody.Children.Add(_surface);
        heatBody.Children.Add(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                _caption,
                AlignRight(PageKit.Badge("32 PX GRID", PageKit.T.Cyan))
            }
        });
        heatBody.Children.Add(PageKit.Divider());
        heatBody.Children.Add(_channels);
        main.Children.Add(PageKit.Section(
            "点击位置",
            "所有通道汇总 · 色阶从冷到热",
            heatBody,
            PageKit.Badge("MAP", PageKit.T.Cyan),
            PageKit.T.Cyan));

        Grid side = new()
        {
            RowDefinitions = new RowDefinitions("1.05*,1*"),
            RowSpacing = 10
        };
        side.Children.Add(PageKit.Section(
            "九宫格",
            "按屏幕位置三等分",
            _zones,
            PageKit.Badge("3 × 3", PageKit.T.Orange),
            PageKit.T.Orange));
        Control top = PageKit.Section(
            "Top 采样格",
            "按点击次数排序 · 局部翻页",
            _topCells,
            PageKit.Badge("TOP", PageKit.T.Green),
            PageKit.T.Green);
        Grid.SetRow(top, 1);
        side.Children.Add(top);
        Grid.SetColumn(side, 1);
        main.Children.Add(side);
        Grid.SetRow(main, 3);
        root.Children.Add(main);

        Content = root;
        SizeChanged += (_, _) => ApplyDensity(Bounds.Height);
        Update(0);
    }

    private void ApplyDensity(double height)
    {
        bool compact = height > 0 && height < 720;
        _surface.Height = compact ? 210 : 270;
        _topCells.SetPageSize(compact ? 2 : 3);
    }

    private static Control AlignRight(Control control)
    {
        Grid.SetColumn(control, 1);
        return control;
    }

    private void Update(int channel)
    {
        _channel = channel;
        Matrix matrix = Matrix.Build(_day, channel);
        _surface.SetData(matrix.Values);

        BuildFilters(matrix);
        BuildSummary(matrix);
        BuildZones(matrix);
        BuildChannels(matrix);
        BuildTopCells(matrix);
        _caption.Text = channel == 0
            ? "32 px 采样格 · 屏幕物理像素 · 所有通道汇总"
            : $"32 px 采样格 · {ChannelName(channel)} · 只保留最近 30 天";
    }

    private void BuildFilters(Matrix matrix)
    {
        _filters.Children.Clear();
        (string Label, int Channel)[] items =
        [
            ("全部", 0), ("左键", 1), ("右键", 2), ("中键", 3), ("侧键", 4)
        ];
        foreach ((string label, int channel) in items)
        {
            int selected = channel;
            _filters.Children.Add(PageKit.Filter(label, _channel == channel, () => Update(selected),
                channel switch
                {
                    1 => PageKit.T.Accent,
                    2 => PageKit.T.Green,
                    3 => PageKit.T.Orange,
                    4 => PageKit.T.Purple,
                    _ => PageKit.T.Cyan
                }));
        }

        _filters.Children.Add(new Border { Width = 8 });
        _filters.Children.Add(PageKit.Filter(_cloud ? "热斑" : "网格", _cloud, ToggleCloud, PageKit.T.Purple));
        _filters.Children.Add(new Border { Width = 8 });
        _filters.Children.Add(PageKit.Muted($"{matrix.Total:N0} 次点击 · {matrix.Cells:N0} 个采样格", 10));
    }

    private void ToggleCloud()
    {
        _cloud = !_cloud;
        _surface.SetCloud(_cloud);
        Update(_channel);
    }

    private void BuildSummary(Matrix matrix)
    {
        _summary.Children.Clear();
        long[] zoneValues = matrix.Zones();
        int topZoneIndex = Array.IndexOf(zoneValues, zoneValues.Max());
        (int x, int y, long count) topCell = matrix.TopCells().FirstOrDefault();

        _summary.Children.Add(PageKit.StatStrip(
            ("总点击", matrix.Total.ToString("N0"), PageKit.T.Cyan),
            ("采样格", matrix.Cells.ToString("N0"), PageKit.T.Green),
            ("最热区域", zoneValues[topZoneIndex] > 0 ? ZoneName(topZoneIndex) : "--", PageKit.T.Orange),
            ("最热格", topCell.count > 0 ? $"{topCell.x},{topCell.y} · {topCell.count:N0} 次" : "--", PageKit.T.Purple),
            ("通道", _channel == 0 ? "全部" : ChannelName(_channel), PageKit.T.Accent)));
    }

    private void BuildZones(Matrix matrix)
    {
        _zones.Children.Clear();
        long[] zones = matrix.Zones();
        long maximum = Math.Max(1, zones.Max());
        Grid grid = new() { ColumnSpacing = 6, RowSpacing = 6 };
        for (int i = 0; i < 3; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        }

        for (int i = 0; i < 3; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));
        }

        for (int index = 0; index < 9; index++)
        {
            double fraction = zones[index] / (double)maximum;
            Color tone = index == Array.IndexOf(zones, zones.Max()) ? PageKit.T.Orange : PageKit.T.Accent;
            Border cell = new()
            {
                Background = PageKit.Gradient(PageKit.Alpha(tone, 0.10 + 0.30 * fraction), PageKit.Alpha(PageKit.T.Card, 0.9), 135),
                BorderBrush = PageKit.Brush(PageKit.Alpha(tone, 0.20 + 0.35 * fraction)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(7, 4),
                Child = new StackPanel
                {
                    Spacing = 0,
                    Children =
                    {
                        PageKit.Muted(ZoneName(index), 7.5),
                        PageKit.Label(zones[index].ToString("N0"), 13, fraction > 0.6 ? tone : PageKit.T.Text, FontWeight.SemiBold),
                        PageKit.Muted($"{zones[index] * 100.0 / Math.Max(1, matrix.Total):0.#}%", 7.5)
                    }
                }
            };
            Grid.SetColumn(cell, index % 3);
            Grid.SetRow(cell, index / 3);
            grid.Children.Add(cell);
        }

        _zones.Children.Add(grid);
    }

    private void BuildChannels(Matrix matrix)
    {
        _channels.Children.Clear();
        long[] channels = matrix.ChannelTotals();
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

            Border chip = new()
            {
                Margin = new Thickness(0, 0, 8, 3),
                Padding = new Thickness(7, 3),
                CornerRadius = new CornerRadius(20),
                Background = PageKit.Brush(PageKit.Alpha(colors[i], 0.10)),
                Child = PageKit.LegendDot(colors[i], labels[i], $"{channels[i] * 100.0 / total:0.#}%")
            };
            legend.Children.Add(chip);
        }

        _channels.Children.Add(legend);

        long[] zoneValues = matrix.Zones();
        int topZone = Array.IndexOf(zoneValues, zoneValues.Max());
        StackPanel stats = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            Children =
            {
                PageKit.Muted($"最热区域 {ZoneName(topZone)}", 9),
                PageKit.Muted($"采样格 {matrix.Cells:N0}", 9),
                PageKit.Muted($"总点击 {matrix.Total:N0}", 9)
            }
        };
        _channels.Children.Add(stats);
    }

    private void BuildTopCells(Matrix matrix)
    {
        List<Control> rows = [];
        List<(int X, int Y, long Count)> cells = matrix.TopCells().Take(8).ToList();
        long max = Math.Max(1, cells.Count == 0 ? 1 : cells[0].Count);
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

        _topCells.SetItems(rows);
    }

    private static string ChannelName(int channel) => channel switch
    {
        1 => "左键",
        2 => "右键",
        3 => "中键",
        4 => "侧键",
        _ => "全部"
    };

    private static string ZoneName(int index) => index switch
    {
        0 => "左上", 1 => "上中", 2 => "右上",
        3 => "左中", 4 => "正中", 5 => "右中",
        6 => "左下", 7 => "下中", _ => "右下"
    };

    private static DayRecord? ResolveDay(StatsData data)
    {
        DateTime today = DateTime.Today;
        string key = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
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

    private sealed class Matrix
    {
        public long[,] Values { get; private init; } = new long[1, 1];
        public long Total { get; private set; }
        public long Cells { get; private set; }
        public int CellX { get; private set; }
        public int CellY { get; private set; }
        public long[] Channels { get; private init; } = new long[5];

        public static Matrix Build(DayRecord? day, int channel)
        {
            if (day is null || (day.ClickCells.Count == 0 && day.ClickCellsV1.Count == 0))
            {
                return new Matrix();
            }

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

            if (cells.Count == 0)
            {
                return new Matrix { Channels = channelTotals };
            }

            int minX = cells.Min(cell => cell.X);
            int minY = cells.Min(cell => cell.Y);
            int width = Math.Clamp(cells.Max(cell => cell.X) - minX + 1, 1, 64);
            int height = Math.Clamp(cells.Max(cell => cell.Y) - minY + 1, 1, 40);
            long[,] matrix = new long[height, width];
            long total = 0;
            long used = 0;
            foreach ((int x, int y, long count) in cells)
            {
                int column = Math.Clamp(x - minX, 0, width - 1);
                int row = Math.Clamp(y - minY, 0, height - 1);
                matrix[row, column] += count;
                total += count;
                used++;
            }

            return new Matrix
            {
                Values = matrix,
                Total = total,
                Cells = used,
                CellX = minX,
                CellY = minY,
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
                        cells.Add((CellX + column, CellY + row, value));
                    }
                }
            }

            return cells.OrderByDescending(cell => cell.Count).ThenBy(cell => cell.X).ThenBy(cell => cell.Y);
        }

        private static int DecodeX(int key) => ((key >> 16) & 0xFFFF) - 32768;
        private static int DecodeY(int key) => (key & 0xFFFF) - 32768;
    }

    private sealed class HeatSurface : Control
    {
        private long[,] _counts = new long[1, 1];
        private bool _cloud;

        public HeatSurface()
        {
            RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        }

        public void SetData(long[,] counts)
        {
            _counts = counts;
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

            if (!_cloud)
            {
                HeatGridGeometry geometry = HeatGrid.Measure(
                    new Rect(0, 0, Bounds.Width, Bounds.Height),
                    _counts,
                    PageKit.T,
                    HeatScale.All,
                    gap: 1.5);
                HeatGrid.Draw(context, geometry);
                return;
            }

            Rect bounds = new(0, 0, Bounds.Width, Bounds.Height);
            HeatGridGeometry grid = HeatGrid.Measure(bounds, _counts, PageKit.T, HeatScale.All, gap: 1.5);
            HeatCell[] dim = new HeatCell[grid.Cells.Count];
            for (int i = 0; i < dim.Length; i++)
            {
                HeatCell cell = grid.Cells[i];
                Color fill = cell.Fill;
                dim[i] = new HeatCell(
                    cell.Row,
                    cell.Column,
                    cell.Count,
                    cell.Bounds,
                    Color.FromArgb((byte)(fill.A * 0.55), fill.R, fill.G, fill.B));
            }

            HeatGrid.Draw(context, grid with { Cells = dim }, 1.5);
            RenderCloud(context, bounds);
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

            using WriteableBitmap bitmap = new(
                new PixelSize(columns, rows),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Unpremul);
            using (ILockedFramebuffer frame = bitmap.Lock())
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

                        double fraction = max <= 1
                            ? 0
                            : Math.Clamp(Math.Log(1 + value) / Math.Log(1 + max), 0, 1);
                        if (fraction < 0.08)
                        {
                            continue;
                        }

                        Color color = HeatScale.For(
                            theme,
                            HeatScale.All,
                            (long)Math.Round(value * 1000),
                            (long)Math.Round(max * 1000));
                        int offset = rowOffset + c * 4;
                        pixels[offset] = color.B;
                        pixels[offset + 1] = color.G;
                        pixels[offset + 2] = color.R;
                        pixels[offset + 3] = (byte)Math.Clamp(
                            175 * Math.Pow((fraction - 0.08) / 0.92, 1.2), 0, 255);
                    }
                }

                Marshal.Copy(pixels, 0, frame.Address, pixels.Length);
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
}
