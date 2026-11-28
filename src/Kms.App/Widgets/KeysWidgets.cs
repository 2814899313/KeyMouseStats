using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Kms.App.Apps;
using Kms.App.Pages;
using Kms.Core.Apps;
using Kms.Core.Model;
using Kms.Core.Time;
using Kms.Input.Counting;
using Kms.Input.Devices;
using Kms.Render.Primitives;
using Kms.Render.Tokens;

namespace Kms.App.Widgets;

internal sealed class KeysWidgetModel : WidgetPageModel
{
    public KeysWidgetModel(
        StatsData data,
        TimeScopeRange scope,
        AppPageContext? appContext = null)
    {
        AppContext = appContext ?? AppPageContext.All;
        Data = AppContext.FilterStats(data);
        Scope = scope;
        Aggregate = KeysAggregate.Build(
            Data,
            scope.Start,
            scope.End,
            AppContext.IsAll ? null : AppContext.Profile);
    }

    public AppPageContext AppContext { get; }

    public StatsData Data { get; }

    public TimeScopeRange Scope { get; }

    public KeysAggregate Aggregate { get; }
}

internal sealed class KeysAggregate
{
    public Dictionary<int, long> Keys { get; } = [];

    public Dictionary<int, KeyHold> Holds { get; } = [];

    public Dictionary<string, long> Combos { get; } = new(StringComparer.Ordinal);

    public Dictionary<int, long> PhysicalKeys { get; } = [];

    public long[] HoldBuckets { get; } = new long[8];

    public long HoldCount { get; private set; }

    public double HoldTotalMs { get; private set; }

    public double HoldMaxMs { get; private set; }

    public double HoldActiveSeconds { get; private set; }

    public double ActiveSeconds { get; private set; }

    public int Days { get; private set; }

    public static KeysAggregate Build(
        StatsData data,
        DateTime start,
        DateTime end,
        AppProfileSnapshot? profile = null)
    {
        KeysAggregate result = new();
        if (profile is not null)
        {
            result.ActiveSeconds = profile.Totals.ActiveSeconds;
            foreach (AppKeyCount key in profile.Keys)
            {
                result.Keys[key.Vk] = result.Keys.GetValueOrDefault(key.Vk) + key.Count;
            }

            return result;
        }

        for (DateTime date = start.Date; date <= end.Date; date = date.AddDays(1))
        {
            string key = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
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

internal static class KeysWidgets
{
    private static KeysWidgetModel Resolve(WidgetContext context) =>
        context.ResolveModel("keys", () => new KeysWidgetModel(
            context.Data,
            context.Scope,
            context.AppPage ?? AppPageContext.Create(context.AppFilter, context.Profile)));

    public static WidgetView CreateKpiTotal(WidgetContext context)
    {
        KeysAggregate aggregate = Resolve(context).Aggregate;
        long total = aggregate.Keys.Values.Sum();
        return MicroKpi.Card("总击键", PageKit.FormatCount(total), $"{aggregate.Keys.Count} 个独立按键", PageKit.T.Accent);
    }

    public static WidgetView CreateKpiTopKey(WidgetContext context)
    {
        KeysAggregate aggregate = Resolve(context).Aggregate;
        KeyValuePair<int, long> top = aggregate.Keys
            .Where(static pair => pair.Value > 0)
            .OrderByDescending(static pair => pair.Value)
            .ThenBy(static pair => pair.Key)
            .FirstOrDefault();
        return top.Key == 0
            ? MicroKpi.Card("最高频按键", "--", "暂无按键记录", PageKit.T.Green)
            : MicroKpi.Card("最高频按键", PeripheralWear.KeyName(top.Key), $"{PageKit.FormatCount(top.Value)} 次", PageKit.T.Green);
    }

    public static WidgetView CreateKpiAverageHold(WidgetContext context)
    {
        KeysAggregate aggregate = Resolve(context).Aggregate;
        double average = aggregate.HoldCount > 0 ? aggregate.HoldTotalMs / aggregate.HoldCount : 0;
        return MicroKpi.Card(
            "平均按住",
            aggregate.HoldCount > 0 ? $"{average:0} ms" : "--",
            $"{PageKit.FormatCount(aggregate.HoldCount)} 次样本",
            PageKit.T.Orange);
    }

    public static WidgetView CreateKpiComboRatio(WidgetContext context)
    {
        KeysAggregate aggregate = Resolve(context).Aggregate;
        long total = aggregate.Keys.Values.Sum();
        long combos = aggregate.Combos.Values.Sum();
        double ratio = total > 0 ? combos / (double)total : 0;
        return MicroKpi.Card(
            "组合键占比",
            total > 0 ? MicroKpi.Percent(ratio) : "--",
            $"{PageKit.FormatCount(combos)} 次组合动作",
            PageKit.T.Purple);
    }

    public static WidgetView CreateSummary(WidgetContext context)
    {
        KeysWidgetModel model = Resolve(context);
        KeysAggregate aggregate = model.Aggregate;
        int topVk = 0;
        long topCount = 0;
        if (aggregate.Keys.Count > 0)
        {
            KeyValuePair<int, long> top = aggregate.Keys.OrderByDescending(pair => pair.Value).First();
            topVk = top.Key;
            topCount = top.Value;
        }

        double averageHold = aggregate.HoldCount == 0 ? 0 : aggregate.HoldTotalMs / aggregate.HoldCount;
        Control body = PageKit.StatStripBody(
            ("区间击键", PageKit.FormatCount(aggregate.Keys.Values.Sum()), PageKit.T.Accent),
            ("独立按键", $"{aggregate.Keys.Count} 个", PageKit.T.Cyan),
            ("平均按住", $"{averageHold:0} ms", PageKit.T.Purple),
            ("最长按住", $"{aggregate.HoldMaxMs:0} ms", PageKit.T.Orange),
            ("最常用", topVk == 0 ? "--" : PeripheralWear.KeyName(topVk), PageKit.T.Green),
            ("按住样本", $"{aggregate.HoldCount:N0}", PageKit.T.Text));
        return new WidgetView
        {
            Content = body,
            ShowHeader = false,
            ContentPadding = new Thickness(12),
            Elevated = true
        };
    }

    public static WidgetView CreateRanking(WidgetContext context)
    {
        KeysWidgetModel model = Resolve(context);
        return new WidgetView
        {
            Content = new KeysRankingView(model, context.VariantId, context.Span),
            HeaderHint = string.Equals(context.VariantId, "keycaps", StringComparison.Ordinal)
                ? "实体键帽矩阵 · 高频按键 / 按压次数 / 占比"
                : "按击键数排序 · 右侧为占比与平均按住 · 局部翻页",
            HeaderTrailing = PageKit.Badge("TOP 20", PageKit.T.Accent),
            ContentPadding = new Thickness(16)
        };
    }

    public static WidgetView CreateCombos(WidgetContext context)
    {
        KeysWidgetModel model = Resolve(context);
        return new WidgetView
        {
            Content = new KeysComboView(model, context.Span),
            HeaderHint = "只统计带 Ctrl / Alt / Win / Shift 的组合 · 局部翻页",
            HeaderTrailing = PageKit.Badge("COMBO", PageKit.T.Purple),
            ContentPadding = new Thickness(16)
        };
    }

    public static WidgetView CreateHolds(WidgetContext context)
    {
        KeysWidgetModel model = Resolve(context);
        return new WidgetView
        {
            Content = new KeysHoldView(model),
            HeaderHint = "8 档直方图 · 平均与最长",
            HeaderTrailing = PageKit.Badge("HOLD", PageKit.T.Orange),
            ContentPadding = new Thickness(16)
        };
    }

    public static WidgetView CreateKeyboard(WidgetContext context)
    {
        KeysWidgetModel model = Resolve(context);
        return new WidgetView
        {
            Content = new KeysKeyboardView(model),
            HeaderHint = "物理键位聚合 · 32px 扫描码归一 · 没有扫描码的记录不会伪造键位",
            HeaderTrailing = PageKit.Badge("LAYOUT", PageKit.T.Cyan),
            ContentPadding = new Thickness(16)
        };
    }

    public static WidgetView CreateGroups(WidgetContext context)
    {
        KeysWidgetModel model = Resolve(context);
        return new WidgetView
        {
            Content = new KeysGroupView(model),
            HeaderHint = "六类语义分组 · 用于看操作习惯",
            HeaderTrailing = PageKit.Badge("MIX", PageKit.T.Green),
            ContentPadding = new Thickness(16)
        };
    }

    public static WidgetView CreateWear(WidgetContext context)
    {
        return new WidgetView
        {
            Content = new PeripheralWearView(context.Data),
            HeaderHint = "全生命周期物理累计（不受全局时间范围限制） · 按额定次数估算",
            HeaderTrailing = PageKit.Badge("WEAR", PageKit.T.Orange),
            ContentPadding = new Thickness(16)
        };
    }

    public static WidgetView CreateHealthSummary(WidgetContext context)
    {
        return new WidgetView
        {
            Content = new PeripheralHealthView(context.Data),
            HeaderHint = "全生命周期物理累计（不受全局时间范围限制）",
            HeaderTrailing = PageKit.Badge("SUMMARY", PageKit.T.Cyan),
            ContentPadding = new Thickness(16)
        };
    }

    public static WidgetView CreateHealthNotes(WidgetContext context)
    {
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
        return new WidgetView
        {
            Content = notes,
            HeaderHint = "磨损只是使用强度参考",
            HeaderTrailing = PageKit.Badge("NOTE", PageKit.T.Purple),
            ContentPadding = new Thickness(16)
        };
    }
}

internal sealed class KeysRankingView : UserControl
{
    private readonly KeysWidgetModel _model;
    private readonly bool _compact;
    private readonly PageKit.PagedList _list = new(10, 4);

    public KeysRankingView(KeysWidgetModel model, string variant, WidgetSpan span)
    {
        _model = model;
        _compact = span.Rows <= 2;
        if (string.Equals(variant, "keycaps", StringComparison.Ordinal))
        {
            KeysAggregate aggregate = model.Aggregate;
            long total = Math.Max(1, aggregate.Keys.Values.Sum());
            KeycapMatrix matrix = new();
            matrix.SetData(aggregate.Keys
                .OrderByDescending(pair => pair.Value)
                .Take(_compact ? 9 : 20)
                .Select((pair, index) => new KeycapItem(
                    PeripheralWear.KeyName(pair.Key),
                    pair.Value,
                    pair.Value / (double)total,
                    index switch
                    {
                        0 => PageKit.T.Purple,
                        1 => PageKit.T.Cyan,
                        2 => PageKit.T.Orange,
                        3 => PageKit.T.Green,
                        _ => PageKit.T.Accent
                    }))
                .ToArray());
            Content = matrix;
            return;
        }

        Content = _list;
        Build();
        if (_compact)
        {
            _list.SetPageSize(3);
        }

        SizeChanged += (_, _) =>
        {
            int size = _compact
                ? 3
                : Bounds.Height > 0 ? Math.Clamp((int)(Bounds.Height / 38), 2, 20) : 10;
            _list.SetPageSize(size);
        };
    }

    private void Build()
    {
        KeysAggregate aggregate = _model.Aggregate;
        List<Control> rows = [];
        if (aggregate.Keys.Count == 0)
        {
            rows.Add(PageKit.Empty("还没有按键记录"));
            _list.SetItems(rows);
            return;
        }

        long max = Math.Max(1, aggregate.Keys.Values.Max());
        int rank = 1;
        foreach ((int vk, long count) in aggregate.Keys.OrderByDescending(pair => pair.Value).Take(20))
        {
            string hold = aggregate.Holds.TryGetValue(vk, out KeyHold? keyHold) && keyHold.Count > 0
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

        _list.SetItems(rows);
    }
}

internal sealed class KeysComboView : UserControl
{
    private readonly KeysWidgetModel _model;
    private readonly bool _compact;
    private readonly PageKit.PagedList _list = new(8, 4);

    public KeysComboView(KeysWidgetModel model, WidgetSpan span)
    {
        _model = model;
        _compact = span.Rows <= 2;
        Content = _list;
        Build();
        if (_compact)
        {
            _list.SetPageSize(3);
        }

        SizeChanged += (_, _) =>
        {
            int size = _compact
                ? 3
                : Bounds.Height > 0 ? Math.Clamp((int)(Bounds.Height / 40), 2, 16) : 8;
            _list.SetPageSize(size);
        };
    }

    private void Build()
    {
        List<Control> rows = [];
        KeysAggregate aggregate = _model.Aggregate;
        if (aggregate.Combos.Count == 0)
        {
            rows.Add(PageKit.Empty("还没有组合键记录"));
            _list.SetItems(rows);
            return;
        }

        long max = Math.Max(1, aggregate.Combos.Values.Max());
        int rank = 1;
        foreach ((string combo, long count) in aggregate.Combos.OrderByDescending(pair => pair.Value).Take(10))
        {
            rows.Add(PageKit.RankRow(
                rank++,
                combo.Replace("_", " + "),
                PageKit.FormatCount(count),
                count / (double)max,
                PageKit.T.Purple,
                keycap: false));
        }

        _list.SetItems(rows);
    }
}

internal sealed class KeysHoldView : UserControl
{
    private readonly KeysWidgetModel _model;
    private readonly StackPanel _body = new() { Spacing = 4 };

    public KeysHoldView(KeysWidgetModel model)
    {
        _model = model;
        Content = _body;
        Build();
        SizeChanged += (_, _) => Build();
    }

    private void Build()
    {
        _body.Children.Clear();
        KeysAggregate aggregate = _model.Aggregate;
        if (aggregate.HoldCount == 0)
        {
            _body.Children.Add(PageKit.Empty("还没有按住时长样本"));
            return;
        }

        _body.Children.Add(new HoldChart(aggregate.HoldBuckets)
        {
            Height = Math.Clamp(Bounds.Height - 110, 90, 500)
        });
        _body.Children.Add(PageKit.StatRow("平均按住",
            $"{aggregate.HoldTotalMs / Math.Max(1, aggregate.HoldCount):0} ms", PageKit.T.Orange));
        _body.Children.Add(PageKit.StatRow("最长按住", $"{aggregate.HoldMaxMs:0} ms", PageKit.T.Red));
        _body.Children.Add(PageKit.Progress("按住占用",
            aggregate.HoldActiveSeconds / Math.Max(1, aggregate.ActiveSeconds), PageKit.T.Orange));
    }
}

internal sealed class KeysKeyboardView : UserControl
{
    private readonly KeysWidgetModel _model;
    private readonly StackPanel _body = new() { Spacing = 7 };
    private int _layout = KeyCapGrid.LayoutAnsi104;

    public KeysKeyboardView(KeysWidgetModel model)
    {
        _model = model;
        Content = _body;
        Build();
        SizeChanged += (_, _) => Build();
    }

    private void Build()
    {
        _body.Children.Clear();
        StackPanel filters = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
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
                Build();
            }, PageKit.T.Cyan));
        }

        filters.Children.Add(new Border { Width = 4 });
        filters.Children.Add(PageKit.Muted(_model.Aggregate.PhysicalKeys.Count == 0
            ? "当前记录没有物理扫描码"
            : $"{_model.Aggregate.PhysicalKeys.Count} 个物理键位", 10));
        _body.Children.Add(filters);

        _body.Children.Add(new KeyboardHeat(_model.Aggregate.PhysicalKeys, _layout)
        {
            Height = Math.Clamp(Bounds.Height - 100, 130, 560)
        });
        _body.Children.Add(PageKit.Legend(
            (PageKit.T.Accent, "低频"),
            (PageKit.T.Cyan, "中频"),
            (PageKit.T.Green, "高频"),
            (PageKit.T.Orange, "最热")));
    }
}

internal sealed class KeysGroupView : UserControl
{
    private readonly KeysWidgetModel _model;
    private readonly StackPanel _body = new() { Spacing = 6 };

    public KeysGroupView(KeysWidgetModel model)
    {
        _model = model;
        Content = _body;
        Build();
    }

    private void Build()
    {
        _body.Children.Clear();
        long[] counts = new long[6];
        foreach ((int vk, long count) in _model.Aggregate.Keys)
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
            _body.Children.Add(PageKit.LegendDot(colors[i], labels[i], $"{counts[i] * 100.0 / total:0.#}%"));
            _body.Children.Add(new PageKit.Proportion(counts[i] / (double)total, colors[i]) { Height = 6 });
        }
    }
}

internal sealed class PeripheralWearView : UserControl
{
    private readonly StatsData _data;
    private readonly StackPanel _body = new() { Spacing = 4 };
    private double _lastHeight = -1;

    public PeripheralWearView(StatsData data)
    {
        _data = data;
        Content = _body;
        Build();
        SizeChanged += (_, _) =>
        {
            if (Math.Abs(Bounds.Height - _lastHeight) > 8)
            {
                Build();
            }
        };
    }

    private void Build()
    {
        _lastHeight = Bounds.Height;
        _body.Children.Clear();
        if (_data.Keyboards.Count == 0 && _data.Mice.Count == 0)
        {
            _body.Children.Add(PageKit.Empty("还没有外设档案；插拔键盘/鼠标后会自动建立"));
            return;
        }

        bool compact = Bounds.Height is > 0 and < 220;
        int keyboardLimit = compact ? 1 : 3;
        int mouseLimit = compact ? 1 : 2;
        int rows = 0;
        foreach (KeyboardProfile keyboard in _data.Keyboards.Where(profile => !profile.Pending).Take(keyboardLimit))
        {
            (int worstVk, long worstCount) = WorstKeyboardKey(keyboard);
            string detail = worstVk == 0
                ? "暂无单键数据"
                : $"最重单键 {PeripheralWear.KeyName(worstVk)} · {worstCount:N0} 次";
            if (rows++ > 0)
            {
                _body.Children.Add(PageKit.Divider());
            }

            _body.Children.Add(WearRow(keyboard.DisplayName, "键盘", worstCount, keyboard.Rated, detail, PageKit.T.Cyan));
        }

        foreach (MouseProfile mouse in _data.Mice.Where(profile => !profile.Pending).Take(mouseLimit))
        {
            double leftRatio = PeripheralWear.Ratio(mouse.Left, mouse.RatedLeft);
            double rightRatio = PeripheralWear.Ratio(mouse.Right, mouse.RatedRight);
            bool leftWorst = leftRatio >= rightRatio;
            long count = leftWorst ? mouse.Left : mouse.Right;
            long rated = leftWorst ? mouse.RatedLeft : mouse.RatedRight;
            string detail = $"左 {mouse.Left:N0} / 右 {mouse.Right:N0} · 总 {mouse.TotalClicks:N0}";
            if (rows++ > 0)
            {
                _body.Children.Add(PageKit.Divider());
            }

            _body.Children.Add(WearRow(mouse.DisplayName, "鼠标", count, rated, detail, PageKit.T.Orange));
        }

        _body.Children.Add(PageKit.Muted("磨损百分比是估算值，可在后续设置页修改额定次数。", 9));
    }

    private static Control WearRow(string name, string kind, long count, long rated, string detail, Color tone)
    {
        double ratio = PeripheralWear.Ratio(count, rated);
        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("1.1*,1.5*,Auto"),
            ColumnSpacing = 10,
            Margin = new Thickness(0, 3)
        };
        StackPanel info = new() { Spacing = 1 };
        info.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                PageKit.Label(name, 11.5, PageKit.T.Text, FontWeight.SemiBold, 220, TextTrimming.CharacterEllipsis),
                PageKit.Badge(kind, tone)
            }
        });
        info.Children.Add(PageKit.Label(detail, 9, PageKit.T.Muted, null, 280, TextTrimming.CharacterEllipsis));
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
}

internal sealed class PeripheralHealthView : UserControl
{
    private readonly StackPanel _body = new() { Spacing = 6 };

    public PeripheralHealthView(StatsData data)
    {
        Content = _body;
        List<KeyboardProfile> keyboards = data.Keyboards.Where(profile => !profile.Pending).ToList();
        List<MouseProfile> mice = data.Mice.Where(profile => !profile.Pending).ToList();
        if (keyboards.Count == 0 && mice.Count == 0)
        {
            _body.Children.Add(PageKit.Empty("还没有可估算的外设"));
            return;
        }

        int pending = data.Keyboards.Count(profile => profile.Pending) + data.Mice.Count(profile => profile.Pending);
        string worstName = "--";
        double worstRatio = 0;
        foreach (KeyboardProfile keyboard in keyboards)
        {
            (_, long worstCount) = WorstKeyboardKey(keyboard);
            double ratio = PeripheralWear.Ratio(worstCount, keyboard.Rated);
            if (ratio >= worstRatio)
            {
                worstRatio = ratio;
                worstName = keyboard.DisplayName;
            }
        }

        foreach (MouseProfile mouse in mice)
        {
            double ratio = Math.Max(PeripheralWear.Ratio(mouse.Left, mouse.RatedLeft),
                PeripheralWear.Ratio(mouse.Right, mouse.RatedRight));
            if (ratio >= worstRatio)
            {
                worstRatio = ratio;
                worstName = mouse.DisplayName;
            }
        }

        _body.Children.Add(PageKit.StatRow("键盘", $"{keyboards.Count} 台", PageKit.T.Cyan));
        _body.Children.Add(PageKit.StatRow("鼠标", $"{mice.Count} 个", PageKit.T.Orange));
        _body.Children.Add(PageKit.StatRow("待确认", $"{pending} 个", PageKit.T.Muted));
        _body.Children.Add(PageKit.Divider());
        _body.Children.Add(PageKit.StatRow("最高磨损", worstName, PageKit.T.Red));
        _body.Children.Add(new PageKit.Proportion(Math.Min(1, worstRatio), PageKit.T.Red)
        {
            Height = 8,
            Margin = new Thickness(0, 2)
        });
        _body.Children.Add(PageKit.StatRow("最高比例", $"{worstRatio * 100:0.##}%", PageKit.T.Red));
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
}

internal sealed class HoldChart : Control
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
            context.FillRectangle(PageKit.Brush(PageKit.Alpha(tone, 0.18)),
                new Rect(x, 0, width, Math.Max(3, Bounds.Height - 22)), 4);
            context.FillRectangle(PageKit.Gradient(PageKit.Alpha(tone, 0.45), tone, 90),
                new Rect(x, Bounds.Height - 22 - height, width, height), 4);
            Kms.Render.Primitives.TextBlock.DrawText(
                context, labels[i], new Point(x + width / 2 - 9, Bounds.Height - 17),
                8.5, PageKit.Brush(theme.Muted), Kms.Render.Primitives.TextBlock.NumericFamily, maxWidth: 26);
        }
    }
}

internal sealed class KeyboardHeat : Control
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
            new Rect(0, 0, Bounds.Width, Bounds.Height), _layout, padding: 4, gap: 2);
        KeyCapGrid.Draw(context, geometry, PageKit.T, _counts, HeatScale.All,
            Math.Clamp(geometry.Unit * 0.22, 6, 9));
    }
}
