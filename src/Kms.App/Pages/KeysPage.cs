using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Kms.Core.Model;
using Kms.Input.Counting;
using Kms.Input.Devices;
using Kms.Render.Primitives;
using Kms.Render.Tokens;

namespace Kms.App.Pages;

/// <summary>按键分析：区间排行、按住分布、组合键、键位构成与键盘热力。</summary>
public sealed class KeysPage : UserControl
{
    private readonly StatsData _data;
    private readonly DateTime _end;
    private readonly StackPanel _summary = new();
    private readonly PageKit.PagedList _ranking = new(10, 4);
    private readonly StackPanel _filters = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly PageKit.PagedList _combos = new(8, 4);
    private readonly StackPanel _groups = new() { Spacing = 6 };
    private readonly StackPanel _holds = new() { Spacing = 4 };
    private readonly StackPanel _keyboardHost = new() { Spacing = 8 };
    private readonly ContentControl _modeHost = new();
    private readonly ContentControl _viewHost = new();
    private readonly ContentControl _toolbarHost = new();
    private readonly ContentControl _contentHost = new();
    private readonly StackPanel _statsToolbar;
    private readonly Control _healthToolbar;
    private HoldChart? _holdChart;
    private KeyboardHeat? _heat;
    private int _mode;
    private int _view;
    private int _range = 7;
    private int _layout = KeyCapGrid.LayoutAnsi104;
    private double _holdHeight = 150;
    private double _heatHeight = 280;
    private Aggregate _aggregate = Aggregate.Empty;

    public KeysPage(StatsData data, string title, string subtitle)
    {
        _data = data;
        _end = ResolveEnd(data);
        _statsToolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children = { _viewHost, _filters }
        };
        _healthToolbar = PageKit.Muted("本地外设档案 · 磨损为估算值", 10);

        Grid root = new()
        {
            Margin = new Thickness(20, 16, 20, 12),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"),
            RowSpacing = 10
        };
        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                _modeHost,
                PageKit.Badge(_end.ToString("yyyy-MM-dd"), PageKit.T.Accent)
            }
        };
        root.Children.Add(PageKit.PageHeader("INPUT / KEYBOARD", title, subtitle, null, actions));

        Grid.SetRow(_summary, 1);
        root.Children.Add(_summary);
        Grid.SetRow(_toolbarHost, 2);
        root.Children.Add(_toolbarHost);
        Grid.SetRow(_contentHost, 3);
        root.Children.Add(_contentHost);

        Content = root;
        SizeChanged += (_, _) => ApplyDensity(Bounds.Height);
        Refresh();
    }

    private void ShowMode(int mode)
    {
        _mode = mode;
        _modeHost.Content = PageKit.Segmented(["统计", "外设健康"], mode, ShowMode);
        _viewHost.Content = PageKit.Segmented(["按键排行", "组合键", "全键热力"], _view, ShowView);
        _summary.IsVisible = mode == 0;

        // 复用卡片控件前先摘除旧父级，避免双父级。
        foreach (Control control in new Control[] { _ranking, _combos, _holds, _groups, _keyboardHost })
        {
            if (control.Parent is Panel panel)
            {
                panel.Children.Remove(control);
            }
        }

        _toolbarHost.Content = null;
        _contentHost.Content = null;
        _toolbarHost.Content = mode == 0 ? _statsToolbar : _healthToolbar;
        _contentHost.Content = mode == 0 ? StatsView() : HealthView();
    }

    private void ShowView(int view)
    {
        _view = view;
        ShowMode(0);
    }

    private Control StatsView()
    {
        if (_view == 2)
        {
            Grid heat = new()
            {
                ColumnDefinitions = new ColumnDefinitions("1.7*,1*"),
                ColumnSpacing = 14
            };
            heat.Children.Add(PageKit.Section(
                "全键热力",
                "物理键位聚合 · 32px 扫描码归一 · 没有扫描码的记录不会伪造键位",
                _keyboardHost,
                PageKit.Badge("LAYOUT", PageKit.T.Cyan),
                PageKit.T.Cyan));
            Control groups = PageKit.Section(
                "键位构成",
                "六类语义分组 · 用于看操作习惯",
                _groups,
                PageKit.Badge("MIX", PageKit.T.Green),
                PageKit.T.Green);
            Grid.SetColumn(groups, 1);
            heat.Children.Add(groups);
            return heat;
        }

        Grid main = new()
        {
            ColumnDefinitions = new ColumnDefinitions("1.45*,1*"),
            ColumnSpacing = 14
        };
        main.Children.Add(_view == 0
            ? PageKit.Section(
                "按键排行",
                "按击键数排序 · 右侧为占比与平均按住 · 局部翻页",
                _ranking,
                PageKit.Badge("TOP 20", PageKit.T.Accent))
            : PageKit.Section(
                "组合键 Top 10",
                "只统计带 Ctrl / Alt / Win / Shift 的组合 · 局部翻页",
                _combos,
                PageKit.Badge("COMBO", PageKit.T.Purple),
                PageKit.T.Purple));

        Control holds = PageKit.Section(
            "按住时长",
            "8 档直方图 · 平均与最长",
            _holds,
            PageKit.Badge("HOLD", PageKit.T.Orange),
            PageKit.T.Orange);
        Grid.SetColumn(holds, 1);
        main.Children.Add(holds);
        return main;
    }

    private Control HealthView()
    {
        Grid main = new()
        {
            ColumnDefinitions = new ColumnDefinitions("1.6*,1*"),
            ColumnSpacing = 14
        };
        main.Children.Add(PageKit.Section(
            "设备磨损",
            "按额定次数估算 · 键盘取最重单键，鼠标取左右键中较重者",
            WearList(),
            PageKit.Badge("WEAR", PageKit.T.Orange),
            PageKit.T.Orange));

        Grid side = new()
        {
            RowDefinitions = new RowDefinitions("1.1*,1*"),
            RowSpacing = 10
        };
        side.Children.Add(PageKit.Section(
            "健康摘要",
            "当前已建档的外设",
            HealthSummary(),
            PageKit.Badge("SUMMARY", PageKit.T.Cyan),
            PageKit.T.Cyan));
        StackPanel notes = new()
        {
            Spacing = 6,
            Children =
            {
                PageKit.Muted("额定次数来自常见微动 / 轴体寿命的保守值，仅用于相对比较。", 10),
                PageKit.Muted("插拔键盘鼠标后会自动建立档案；磨损不会影响统计数据。", 10),
                PageKit.Muted("可在后续设置页修改额定次数与设备名称。", 10)
            }
        };
        Control note = PageKit.Section(
            "估算说明",
            "磨损只是使用强度参考",
            notes,
            PageKit.Badge("NOTE", PageKit.T.Purple),
            PageKit.T.Purple);
        Grid.SetRow(note, 1);
        side.Children.Add(note);
        Grid.SetColumn(side, 1);
        main.Children.Add(side);
        return main;
    }

    private void ApplyDensity(double height)
    {
        bool compact = height > 0 && height < 720;
        _ranking.SetPageSize(compact ? 7 : 10);
        _combos.SetPageSize(compact ? 6 : 8);
        _holdHeight = compact ? 104 : 150;
        _heatHeight = Math.Clamp((height > 0 ? height : 760) - 420, 150, 330);
        if (_holdChart is not null)
        {
            _holdChart.Height = _holdHeight;
        }

        if (_heat is not null)
        {
            _heat.Height = _heatHeight;
        }
    }

    private void Refresh()
    {
        _aggregate = Aggregate.Build(_data, _end, _range);
        BuildFilters();
        BuildSummary();
        BuildRanking();
        BuildHolds();
        BuildCombos();
        BuildGroups();
        BuildKeyboard();
        ApplyDensity(Bounds.Height);
        ShowMode(_mode);
    }

    private void BuildFilters()
    {
        _filters.Children.Clear();
        foreach ((string label, int range) in new[] { ("今天", 1), ("近 7 天", 7), ("近 30 天", 30) })
        {
            int selected = range;
            _filters.Children.Add(PageKit.Filter(label, _range == range, () =>
            {
                _range = selected;
                Refresh();
            }, PageKit.T.Accent));
        }

        _filters.Children.Add(new Border { Width = 8 });
        _filters.Children.Add(PageKit.Muted($"{_aggregate.Days} 天 · {_aggregate.Keys.Values.Sum():N0} 次击键", 10));
    }

    private void BuildSummary()
    {
        _summary.Children.Clear();
        int topVk = 0;
        long topCount = 0;
        if (_aggregate.Keys.Count > 0)
        {
            KeyValuePair<int, long> top = _aggregate.Keys.OrderByDescending(pair => pair.Value).First();
            topVk = top.Key;
            topCount = top.Value;
        }

        double averageHold = _aggregate.HoldCount == 0 ? 0 : _aggregate.HoldTotalMs / _aggregate.HoldCount;

        _summary.Children.Add(PageKit.StatStrip(
            ("区间击键", PageKit.FormatCount(_aggregate.Keys.Values.Sum()), PageKit.T.Accent),
            ("独立按键", $"{_aggregate.Keys.Count} 个", PageKit.T.Cyan),
            ("平均按住", $"{averageHold:0} ms", PageKit.T.Purple),
            ("最长按住", $"{_aggregate.HoldMaxMs:0} ms", PageKit.T.Orange),
            ("最常用", topVk == 0 ? "--" : PeripheralWear.KeyName(topVk), PageKit.T.Green),
            ("按住样本", $"{_aggregate.HoldCount:N0}", PageKit.T.Text)));
    }

    private void BuildRanking()
    {
        List<Control> rows = [];
        if (_aggregate.Keys.Count == 0)
        {
            rows.Add(PageKit.Empty("还没有按键记录"));
            _ranking.SetItems(rows);
            return;
        }

        long max = Math.Max(1, _aggregate.Keys.Values.Max());
        int rank = 1;
        foreach ((int vk, long count) in _aggregate.Keys.OrderByDescending(pair => pair.Value).Take(20))
        {
            string hold = _aggregate.Holds.TryGetValue(vk, out KeyHold? keyHold) && keyHold.Count > 0
                ? $"均 {keyHold.TotalMs / keyHold.Count:0} ms"
                : "--";
            rows.Add(PageKit.RankRow(
                rank++,
                PeripheralWear.KeyName(vk),
                PageKit.FormatCount(count),
                count / (double)max,
                rank switch
                {
                    2 => PageKit.T.Orange,
                    3 => PageKit.T.Accent,
                    4 => PageKit.T.Green,
                    5 => PageKit.T.Purple,
                    _ => PageKit.T.Cyan
                },
                hold,
                keycap: true));
        }

        _ranking.SetItems(rows);
    }

    private void BuildHolds()
    {
        _holds.Children.Clear();
        if (_aggregate.HoldCount == 0)
        {
            _holdChart = null;
            _holds.Children.Add(PageKit.Empty("还没有按住时长样本"));
            return;
        }

        _holdChart = new HoldChart(_aggregate.HoldBuckets) { Height = _holdHeight };
        _holds.Children.Add(_holdChart);
        _holds.Children.Add(PageKit.StatRow("平均按住", $"{_aggregate.HoldTotalMs / Math.Max(1, _aggregate.HoldCount):0} ms", PageKit.T.Orange));
        _holds.Children.Add(PageKit.StatRow("最长按住", $"{_aggregate.HoldMaxMs:0} ms", PageKit.T.Red));
        double active = _aggregate.HoldActiveSeconds;
        _holds.Children.Add(PageKit.Progress("按住占用", active / Math.Max(1, _aggregate.ActiveSeconds), PageKit.T.Orange));
    }

    private void BuildCombos()
    {
        List<Control> rows = [];
        if (_aggregate.Combos.Count == 0)
        {
            rows.Add(PageKit.Empty("还没有组合键记录"));
            _combos.SetItems(rows);
            return;
        }

        long max = Math.Max(1, _aggregate.Combos.Values.Max());
        int rank = 1;
        foreach ((string combo, long count) in _aggregate.Combos.OrderByDescending(pair => pair.Value).Take(10))
        {
            rows.Add(PageKit.RankRow(
                rank++,
                combo.Replace("_", " + "),
                PageKit.FormatCount(count),
                count / (double)max,
                PageKit.T.Purple,
                keycap: false));
        }

        _combos.SetItems(rows);
    }

    private void BuildGroups()
    {
        _groups.Children.Clear();
        long[] counts = new long[6];
        foreach ((int vk, long count) in _aggregate.Keys)
        {
            int group = KeySemantics.Group(vk);
            if ((uint)group < counts.Length)
            {
                counts[group] += count;
            }
        }

        Color[] colors = [PageKit.T.Accent, PageKit.T.Green, PageKit.T.Orange, PageKit.T.Purple, PageKit.T.Cyan, PageKit.T.Red];
        string[] labels = ["移动 / WASD", "数字 / 技能栏", "空格 / Shift", "功能 / 修饰", "文字及其他", "未归位"];
        long total = Math.Max(1, counts.Sum());
        for (int i = 0; i < counts.Length; i++)
        {
            _groups.Children.Add(PageKit.LegendDot(colors[i], labels[i], $"{counts[i] * 100.0 / total:0.#}%"));
            _groups.Children.Add(new PageKit.Proportion(counts[i] / (double)total, colors[i]) { Height = 6 });
        }
    }

    private void BuildKeyboard()
    {
        _keyboardHost.Children.Clear();
        StackPanel filters = new() { Orientation = Orientation.Horizontal, Spacing = 7 };
        (string Label, int Layout)[] layouts =
        [
            ("104", KeyCapGrid.LayoutAnsi104),
            ("87", KeyCapGrid.LayoutTkl87),
            ("75%", KeyCapGrid.Layout75),
            ("65%", KeyCapGrid.Layout65),
            ("60%", KeyCapGrid.Layout60)
        ];
        foreach ((string label, int layout) in layouts)
        {
            int selected = layout;
            filters.Children.Add(PageKit.Filter(label, _layout == layout, () =>
            {
                _layout = selected;
                BuildKeyboard();
            }, PageKit.T.Cyan));
        }

        filters.Children.Add(new Border { Width = 6 });
        filters.Children.Add(PageKit.Muted(_aggregate.PhysicalKeys.Count == 0
            ? "当前记录没有物理扫描码"
            : $"{_aggregate.PhysicalKeys.Count} 个物理键位", 10));

        _keyboardHost.Children.Add(filters);
        _heat = new KeyboardHeat(_aggregate.PhysicalKeys, _layout)
        {
            Height = _heatHeight
        };
        _keyboardHost.Children.Add(_heat);
        _keyboardHost.Children.Add(PageKit.Legend(
            (PageKit.T.Accent, "低频"),
            (PageKit.T.Cyan, "中频"),
            (PageKit.T.Green, "高频"),
            (PageKit.T.Orange, "最热")));
    }

    private Control WearList()
    {
        StackPanel body = new() { Spacing = 4 };
        if (_data.Keyboards.Count == 0 && _data.Mice.Count == 0)
        {
            body.Children.Add(PageKit.Empty("还没有外设档案；插拔键盘/鼠标后会自动建立"));
            return body;
        }

        int rows = 0;
        foreach (KeyboardProfile keyboard in _data.Keyboards.Where(profile => !profile.Pending).Take(3))
        {
            (int worstVk, long worstCount) = WorstKeyboardKey(keyboard);
            string detail = worstVk == 0
                ? "暂无单键数据"
                : $"最重单键 {PeripheralWear.KeyName(worstVk)} · {worstCount:N0} 次";
            if (rows++ > 0)
            {
                body.Children.Add(PageKit.Divider());
            }

            body.Children.Add(WearRow(
                keyboard.DisplayName,
                "键盘",
                worstCount,
                keyboard.Rated,
                detail,
                PageKit.T.Cyan));
        }

        foreach (MouseProfile mouse in _data.Mice.Where(profile => !profile.Pending).Take(2))
        {
            double leftRatio = Kms.Input.Devices.PeripheralWear.Ratio(mouse.Left, mouse.RatedLeft);
            double rightRatio = Kms.Input.Devices.PeripheralWear.Ratio(mouse.Right, mouse.RatedRight);
            bool leftWorst = leftRatio >= rightRatio;
            long count = leftWorst ? mouse.Left : mouse.Right;
            long rated = leftWorst ? mouse.RatedLeft : mouse.RatedRight;
            string detail = $"左 {mouse.Left:N0} / 右 {mouse.Right:N0} · 总 {mouse.TotalClicks:N0}";
            if (rows++ > 0)
            {
                body.Children.Add(PageKit.Divider());
            }

            body.Children.Add(WearRow(mouse.DisplayName, "鼠标", count, rated, detail, PageKit.T.Orange));
        }

        body.Children.Add(PageKit.Muted("磨损百分比是估算值，可在后续设置页修改额定次数。", 9));
        return body;
    }

    private Control WearRow(string name, string kind, long count, long rated, string detail, Color tone)
    {
        double ratio = Kms.Input.Devices.PeripheralWear.Ratio(count, rated);
        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("1.25*,1.7*,Auto"),
            ColumnSpacing = 12,
            Margin = new Thickness(0, 3)
        };

        StackPanel info = new() { Spacing = 1 };
        info.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                PageKit.Label(name, 11.5, PageKit.T.Text, FontWeight.SemiBold, 250, TextTrimming.CharacterEllipsis),
                PageKit.Badge(kind, tone)
            }
        });
        info.Children.Add(PageKit.Label(detail, 9, PageKit.T.Muted, null, 320, TextTrimming.CharacterEllipsis));
        row.Children.Add(info);

        StackPanel meter = new() { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        meter.Children.Add(new PageKit.Proportion(Math.Min(1, ratio), tone) { Height = 7 });
        meter.Children.Add(PageKit.Muted($"累计 {PageKit.FormatCount(count)} / 额定 {PageKit.FormatCount(rated)}", 9));
        Grid.SetColumn(meter, 1);
        row.Children.Add(meter);

        Avalonia.Controls.TextBlock percent = PageKit.Label($"{ratio * 100:0.##}%", 17, tone, FontWeight.SemiBold);
        percent.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(percent, 2);
        row.Children.Add(percent);
        return row;
    }

    private Control HealthSummary()
    {
        List<KeyboardProfile> keyboards = _data.Keyboards.Where(profile => !profile.Pending).ToList();
        List<MouseProfile> mice = _data.Mice.Where(profile => !profile.Pending).ToList();
        if (keyboards.Count == 0 && mice.Count == 0)
        {
            return PageKit.Empty("还没有可估算的外设");
        }

        int pending = _data.Keyboards.Count(profile => profile.Pending) + _data.Mice.Count(profile => profile.Pending);
        string worstName = "--";
        double worstRatio = 0;
        foreach (KeyboardProfile keyboard in keyboards)
        {
            (_, long worstCount) = WorstKeyboardKey(keyboard);
            double ratio = Kms.Input.Devices.PeripheralWear.Ratio(worstCount, keyboard.Rated);
            if (ratio >= worstRatio)
            {
                worstRatio = ratio;
                worstName = keyboard.DisplayName;
            }
        }

        foreach (MouseProfile mouse in mice)
        {
            double ratio = Math.Max(
                Kms.Input.Devices.PeripheralWear.Ratio(mouse.Left, mouse.RatedLeft),
                Kms.Input.Devices.PeripheralWear.Ratio(mouse.Right, mouse.RatedRight));
            if (ratio >= worstRatio)
            {
                worstRatio = ratio;
                worstName = mouse.DisplayName;
            }
        }

        StackPanel stack = new() { Spacing = 6 };
        stack.Children.Add(PageKit.StatRow("键盘", $"{keyboards.Count} 台", PageKit.T.Cyan));
        stack.Children.Add(PageKit.StatRow("鼠标", $"{mice.Count} 个", PageKit.T.Orange));
        stack.Children.Add(PageKit.StatRow("待确认", $"{pending} 个", PageKit.T.Muted));
        stack.Children.Add(PageKit.Divider());
        stack.Children.Add(PageKit.StatRow("最高磨损", worstName, PageKit.T.Red));
        stack.Children.Add(new PageKit.Proportion(Math.Min(1, worstRatio), PageKit.T.Red)
        {
            Height = 8,
            Margin = new Thickness(0, 2)
        });
        stack.Children.Add(PageKit.StatRow("最高比例", $"{worstRatio * 100:0.##}%", PageKit.T.Red));
        return stack;
    }

    private static (int Vk, long Count) WorstKeyboardKey(KeyboardProfile keyboard)
    {
        int worstVk = 0;
        long worstCount = 0;
        for (int vk = 0; vk < keyboard.Keys.Length; vk++)
        {
            if (keyboard.Keys[vk] > worstCount)
            {
                worstCount = keyboard.Keys[vk];
                worstVk = vk;
            }
        }

        return (worstVk, worstCount);
    }

    private static DateTime ResolveEnd(StatsData data)
    {
        DateTime today = DateTime.Today;
        string key = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (data.History.ContainsKey(key))
        {
            return today;
        }

        DateTime latest = today;
        foreach (DayRecord day in data.History.Values)
        {
            if (day.Date > latest)
            {
                latest = day.Date;
            }
        }

        return latest;
    }

    private sealed class Aggregate
    {
        public static Aggregate Empty { get; } = new();

        public Dictionary<int, long> Keys { get; } = new();
        public Dictionary<int, KeyHold> Holds { get; } = new();
        public Dictionary<string, long> Combos { get; } = new(StringComparer.Ordinal);
        public Dictionary<int, long> PhysicalKeys { get; } = new();
        public long[] HoldBuckets { get; } = new long[8];
        public long HoldCount { get; private set; }
        public double HoldTotalMs { get; private set; }
        public double HoldMaxMs { get; private set; }
        public double HoldActiveSeconds { get; private set; }
        public double ActiveSeconds { get; private set; }
        public int Days { get; private set; }

        public static Aggregate Build(StatsData data, DateTime end, int range)
        {
            Aggregate result = new();
            for (int i = 0; i < range; i++)
            {
                string key = end.AddDays(-i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (!data.History.TryGetValue(key, out DayRecord? day))
                {
                    continue;
                }

                result.Days++;
                result.ActiveSeconds += day.ActiveSeconds;
                result.HoldCount += day.HoldCount;
                result.HoldTotalMs += day.HoldTotalMs;
                result.HoldMaxMs = Math.Max(result.HoldMaxMs, day.HoldMaxMs);
                result.HoldActiveSeconds += day.HoldActiveSeconds;
                for (int bucket = 0; bucket < result.HoldBuckets.Length; bucket++)
                {
                    result.HoldBuckets[bucket] += day.HoldBuckets[bucket];
                }

                foreach ((int vk, long count) in day.KeyCounts)
                {
                    result.Keys[vk] = result.Keys.GetValueOrDefault(vk) + count;
                }

                foreach ((int vk, KeyHold hold) in day.Holds)
                {
                    if (!result.Holds.TryGetValue(vk, out KeyHold? target))
                    {
                        target = new KeyHold();
                        result.Holds[vk] = target;
                    }

                    target.Count += hold.Count;
                    target.TotalMs += hold.TotalMs;
                    target.MaxMs = Math.Max(target.MaxMs, hold.MaxMs);
                }

                foreach ((string combo, long count) in day.ComboCounts)
                {
                    result.Combos[combo] = result.Combos.GetValueOrDefault(combo) + count;
                }

                foreach ((int scan, long count) in day.PhysicalKeys)
                {
                    result.PhysicalKeys[scan] = result.PhysicalKeys.GetValueOrDefault(scan) + count;
                }
            }

            return result;
        }
    }

    private sealed class HoldChart : Control
    {
        private readonly long[] _buckets;

        public HoldChart(long[] buckets) => _buckets = buckets;

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            ThemeTokens theme = PageKit.T;
            string[] labels = ["<50", "100", "200", "500", "1s", "2s", "5s", "≥5s"];
            long max = Math.Max(1, _buckets.Max());
            double gap = 8;
            double width = Math.Max(8, (Bounds.Width - gap * 7) / 8);
            for (int i = 0; i < 8; i++)
            {
                double x = i * (width + gap);
                double height = Math.Max(3, (Bounds.Height - 22) * _buckets[i] / max);
                Color tone = i < 3 ? theme.Accent : i < 5 ? theme.Cyan : i < 7 ? theme.Orange : theme.Red;
                context.FillRectangle(
                    PageKit.Brush(PageKit.Alpha(tone, 0.18)),
                    new Rect(x, 0, width, Math.Max(3, Bounds.Height - 22)),
                    4);
                context.FillRectangle(
                    PageKit.Gradient(PageKit.Alpha(tone, 0.45), tone, 90),
                    new Rect(x, Bounds.Height - 22 - height, width, height),
                    4);
                Kms.Render.Primitives.TextBlock.DrawText(
                    context, labels[i], new Point(x + width / 2 - 9, Bounds.Height - 17),
                    7, PageKit.Brush(theme.Muted), Kms.Render.Primitives.TextBlock.NumericFamily, maxWidth: 26);
            }
        }
    }

    private sealed class KeyboardHeat : Control
    {
        private readonly IReadOnlyDictionary<int, long> _counts;
        private readonly int _layout;

        public KeyboardHeat(IReadOnlyDictionary<int, long> counts, int layout)
        {
            _counts = counts;
            _layout = layout;
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            KeyCapGridGeometry geometry = KeyCapGrid.Measure(
                new Rect(0, 0, Bounds.Width, Bounds.Height),
                _layout,
                padding: 4,
                gap: 2);
            KeyCapGrid.Draw(context, geometry, PageKit.T, _counts, HeatScale.All,
                Math.Clamp(geometry.Unit * 0.22, 6, 9));
        }
    }
}
