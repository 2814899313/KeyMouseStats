using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Kms.Core.Model;
using Kms.Render.Primitives;
using Kms.Render.Tokens;

namespace Kms.App.Pages;

/// <summary>应用统计：区间聚合、时长构成、排序表、键位组分布。</summary>
public sealed class AppsPage : UserControl
{
    private readonly StatsData _data;
    private readonly DateTime _end;
    private readonly StackPanel _summary = new();
    private readonly StackPanel _filters = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly PageKit.PagedList _table = new(7, 4);
    private readonly StackPanel _tableBody = new() { Spacing = 4 };
    private readonly StackPanel _mix = new() { Spacing = 6 };
    private readonly StackPanel _groups = new() { Spacing = 5 };
    private Snapshot? _snapshot;
    private int _range = 1;
    private int _sort;
    private double _mixRingHeight = 104;
    private int _groupCount = 5;

    public AppsPage(StatsData data, string title, string subtitle)
    {
        _data = data;
        _end = ResolveEnd(data);

        Grid root = new()
        {
            Margin = new Thickness(20, 16, 20, 12),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"),
            RowSpacing = 10
        };
        root.Children.Add(PageKit.PageHeader(
            "APPS / ATTRIBUTION",
            title,
            subtitle,
            _end.ToString("yyyy-MM-dd")));

        Grid.SetRow(_summary, 1);
        root.Children.Add(_summary);
        Grid.SetRow(_filters, 2);
        root.Children.Add(_filters);

        _tableBody.Children.Add(TableHeader());
        _tableBody.Children.Add(PageKit.Divider());
        _tableBody.Children.Add(_table);

        Grid main = new()
        {
            ColumnDefinitions = new ColumnDefinitions("1.7*,1*"),
            ColumnSpacing = 14
        };
        main.Children.Add(PageKit.Section(
            "应用排行",
            "时长、击键、点击与 APM · 局部翻页",
            _tableBody,
            PageKit.Badge("RANK", PageKit.T.Accent)));

        Grid side = new()
        {
            RowDefinitions = new RowDefinitions("1.05*,1*"),
            RowSpacing = 10
        };
        side.Children.Add(PageKit.Section(
            "时长构成",
            "已归因应用时长占比",
            _mix,
            PageKit.Badge("SHARE", PageKit.T.Cyan),
            PageKit.T.Cyan));
        Control groups = PageKit.Section(
            "应用 × 键位组",
            "Top 6 应用的六类键位分布 · 堆叠条",
            _groups,
            PageKit.Badge("GROUPS", PageKit.T.Purple),
            PageKit.T.Purple);
        Grid.SetRow(groups, 1);
        side.Children.Add(groups);
        Grid.SetColumn(side, 1);
        main.Children.Add(side);
        Grid.SetRow(main, 3);
        root.Children.Add(main);

        Content = root;
        SizeChanged += (_, _) => ApplyDensity(Bounds.Height);
        Refresh();
    }

    private void ApplyDensity(double height)
    {
        bool compact = height > 0 && height < 720;
        _table.SetPageSize(compact ? 6 : 7);
        double ring = compact ? 70 : 104;
        int groupCount = compact ? 3 : 5;
        if (Math.Abs(ring - _mixRingHeight) > 0.5 || groupCount != _groupCount)
        {
            _mixRingHeight = ring;
            _groupCount = groupCount;
            if (_snapshot is not null)
            {
                BuildMix(_snapshot);
                BuildGroups(_snapshot);
            }
        }
    }

    private void Refresh()
    {
        Snapshot snapshot = Snapshot.Build(_data, _end, _range);
        _snapshot = snapshot;
        BuildFilters(snapshot);
        BuildSummary(snapshot);
        BuildTable(snapshot);
        BuildMix(snapshot);
        BuildGroups(snapshot);
    }

    private void BuildFilters(Snapshot snapshot)
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
        foreach ((string label, int sort) in new[] { ("按时长", 0), ("按击键", 1), ("按点击", 2) })
        {
            int selected = sort;
            _filters.Children.Add(PageKit.Filter(label, _sort == sort, () =>
            {
                _sort = selected;
                Refresh();
            }, PageKit.T.Green));
        }

        _filters.Children.Add(new Border { Width = 4 });
        _filters.Children.Add(PageKit.Muted($"{snapshot.Days} 天 · {snapshot.Apps.Count} 个应用", 10));
    }

    private void BuildSummary(Snapshot snapshot)
    {
        _summary.Children.Clear();
        double observed = snapshot.ObservedSeconds;
        double attributed = snapshot.AttributedSeconds;
        double unattributed = Math.Max(0, observed - attributed);
        _summary.Children.Add(PageKit.StatStrip(
            ("前台观测", PageKit.FormatDuration(observed), PageKit.T.Cyan),
            ("已归因", PageKit.FormatDuration(attributed), PageKit.T.Green),
            ("覆盖率", $"{(observed <= 0 ? 0 : attributed / observed * 100):0.#}%", PageKit.T.Accent),
            ("未归因", PageKit.FormatDuration(unattributed), PageKit.T.Orange),
            ("应用数", snapshot.Apps.Count.ToString("N0"), PageKit.T.Purple),
            ("Top 1", TopName(snapshot), PageKit.T.Text)));
    }

    private static string TopName(Snapshot snapshot) =>
        snapshot.Apps.Count == 0 ? "--" : snapshot.Apps[0].Name;

    private void BuildTable(Snapshot snapshot)
    {
        List<Control> rows = [];
        if (snapshot.Apps.Count == 0)
        {
            rows.Add(PageKit.Empty("还没有应用归属；开始使用后会自动出现"));
            _table.SetItems(rows);
            return;
        }

        List<AppRow> ordered = _sort switch
        {
            1 => snapshot.Apps.OrderByDescending(app => app.Keys).ToList(),
            2 => snapshot.Apps.OrderByDescending(app => app.Clicks).ToList(),
            _ => snapshot.Apps.OrderByDescending(app => app.Seconds).ToList()
        };

        double max = _sort switch
        {
            1 => Math.Max(1, ordered.Max(app => app.Keys)),
            2 => Math.Max(1, ordered.Max(app => app.Clicks)),
            _ => Math.Max(1, ordered.Max(app => app.Seconds))
        };

        int rank = 1;
        foreach (AppRow app in ordered.Take(30))
        {
            double fraction = _sort switch
            {
                1 => app.Keys / max,
                2 => app.Clicks / max,
                _ => app.Seconds / max
            };

            rows.Add(Row(rank++, app, fraction));
        }

        _table.SetItems(rows);
    }

    private Control TableHeader()
    {
        Grid grid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("24,*,88,56,56,78"),
            ColumnSpacing = 6
        };
        grid.Children.Add(PageKit.Muted("#", 8));
        AddHeader(grid, 1, "应用");
        AddHeader(grid, 2, "时长");
        AddHeader(grid, 3, "击键");
        AddHeader(grid, 4, "点击");
        AddHeader(grid, 5, "APM");
        return grid;
    }

    private void AddHeader(Grid grid, int column, string text)
    {
        Control label = PageKit.Muted(text, 8);
        Grid.SetColumn(label, column);
        grid.Children.Add(label);
    }

    private Control Row(int rank, AppRow app, double fraction)
    {
        Color tone = ColorFor(app.Name);
        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("24,*,88,56,56,78"),
            ColumnSpacing = 6,
            MinHeight = 42
        };

        Control rankLabel = PageKit.Label(rank.ToString("00", CultureInfo.InvariantCulture), 9,
            rank <= 3 ? tone : PageKit.T.Muted, FontWeight.SemiBold);
        row.Children.Add(rankLabel);

        Grid appCell = new() { ColumnDefinitions = new ColumnDefinitions("30,*"), ColumnSpacing = 8 };
        Border avatar = new()
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(9),
            Background = PageKit.Brush(PageKit.Alpha(tone, 0.18)),
            Child = PageKit.Label(Initial(app.Name), 11, tone, FontWeight.Bold)
        };
        appCell.Children.Add(avatar);

        StackPanel names = new() { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(PageKit.Label(app.Name, 11, PageKit.T.Text, FontWeight.SemiBold, 172, TextTrimming.CharacterEllipsis));
        names.Children.Add(PageKit.Label(app.Detail, 8, PageKit.T.Muted, null, 172, TextTrimming.CharacterEllipsis));
        Grid.SetColumn(names, 1);
        appCell.Children.Add(names);
        Grid.SetColumn(appCell, 1);
        row.Children.Add(appCell);

        StackPanel duration = new() { Spacing = 4 };
        duration.Children.Add(PageKit.Label(PageKit.FormatDuration(app.Seconds), 10, PageKit.T.Text, FontWeight.SemiBold));
        duration.Children.Add(new PageKit.Proportion(fraction, tone) { Height = 5 });
        Grid.SetColumn(duration, 2);
        row.Children.Add(duration);

        AddValue(row, 3, PageKit.FormatCount(app.Keys), tone);
        AddValue(row, 4, PageKit.FormatCount(app.Clicks), PageKit.T.Green);
        double apm = app.Seconds > 0 ? (app.Keys + app.Clicks) * 60.0 / app.Seconds : 0;
        double keyApm = app.Seconds > 0 ? app.Keys * 60.0 / app.Seconds : 0;
        AddValue(row, 5, $"{apm:0.#} / {keyApm:0.#}", PageKit.T.Cyan);
        return row;
    }

    private void AddValue(Grid row, int column, string value, Color tone)
    {
        Control label = PageKit.Label(value, 10, column == 3 ? tone : PageKit.T.Text, FontWeight.SemiBold);
        Grid.SetColumn(label, column);
        row.Children.Add(label);
    }

    private void BuildMix(Snapshot snapshot)
    {
        _mix.Children.Clear();
        if (snapshot.Apps.Count == 0)
        {
            _mix.Children.Add(PageKit.Empty("暂无归因数据"));
            return;
        }

        List<AppRow> top = snapshot.Apps.OrderByDescending(app => app.Seconds).Take(3).ToList();
        double total = Math.Max(0.001, snapshot.AttributedSeconds);
        List<DonutSlice> slices = top
            .Select((app, index) => new DonutSlice(app.Seconds, Palette(index)))
            .ToList();
        double rest = Math.Max(0, total - top.Sum(app => app.Seconds));
        if (rest > 0)
        {
            slices.Add(new DonutSlice(rest, PageKit.T.Muted));
        }

        _mix.Children.Add(new PageKit.Ring(slices, $"{snapshot.AttributedSeconds / 3600:0.0}h", "已归因") { Height = _mixRingHeight });

        List<(Color Color, string Text)> legend = [];
        for (int i = 0; i < top.Count; i++)
        {
            legend.Add((Palette(i), $"{ShortName(top[i].Name)} {top[i].Seconds / total * 100:0.#}%"));
        }

        if (rest > 0)
        {
            legend.Add((PageKit.T.Muted, $"其他 {rest / total * 100:0.#}%"));
        }

        _mix.Children.Add(PageKit.Legend(legend.ToArray()));
    }

    private static string ShortName(string name)
    {
        string trimmed = name.Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        return trimmed.Length > 10 ? trimmed[..10] : trimmed;
    }

    private void BuildGroups(Snapshot snapshot)
    {
        _groups.Children.Clear();
        if (snapshot.Apps.Count == 0)
        {
            _groups.Children.Add(PageKit.Empty("暂无可对比的应用"));
            return;
        }

        Color[] colors = [PageKit.T.Accent, PageKit.T.Green, PageKit.T.Orange, PageKit.T.Purple, PageKit.T.Cyan, PageKit.T.Red];
        string[] labels = ["移动", "数字", "交互", "功能", "文字", "未归位"];
        foreach (AppRow app in snapshot.Apps.OrderByDescending(app => app.Seconds).Take(_groupCount))
        {
            _groups.Children.Add(GroupRow(app, colors));
        }

        _groups.Children.Add(PageKit.Legend(
            (colors[0], labels[0]), (colors[1], labels[1]), (colors[2], labels[2]),
            (colors[3], labels[3]), (colors[4], labels[4]), (colors[5], labels[5])));
    }

    private Control GroupRow(AppRow app, Color[] colors)
    {
        long total = Math.Max(1, app.Groups.Sum());
        Grid row = new()
        {
            ColumnDefinitions = new ColumnDefinitions("105,*"),
            ColumnSpacing = 8,
            Margin = new Thickness(0, 1)
        };
        row.Children.Add(PageKit.Label(app.Name, 9.5, PageKit.T.Text, FontWeight.Medium, 100, TextTrimming.CharacterEllipsis));

        Canvas bar = new()
        {
            Height = 10,
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = true
        };
        bar.Children.Add(new Border
        {
            Height = 6,
            Width = 140,
            CornerRadius = new CornerRadius(3),
            Background = PageKit.Brush(PageKit.Alpha(PageKit.T.Line, 0.2))
        });

        double offset = 0;
        for (int i = 0; i < 6; i++)
        {
            if (app.Groups[i] <= 0)
            {
                continue;
            }

            double width = 140 * app.Groups[i] / (double)total;
            Border segment = new()
            {
                Height = 6,
                Width = Math.Max(1, width),
                Background = PageKit.Brush(colors[i])
            };
            Canvas.SetLeft(segment, offset);
            Canvas.SetTop(segment, 2);
            bar.Children.Add(segment);
            offset += width;
        }

        if (offset <= 0)
        {
            bar.Children.Add(PageKit.Muted("无键位组", 8));
        }

        Grid.SetColumn(bar, 1);
        row.Children.Add(bar);
        return row;
    }

    private static Color Palette(int index)
    {
        Color[] colors = [PageKit.T.Accent, PageKit.T.Cyan, PageKit.T.Green, PageKit.T.Purple, PageKit.T.Orange, PageKit.T.Red];
        return colors[Math.Abs(index) % colors.Length];
    }

    private static Color ColorFor(string name)
    {
        uint hash = 2166136261;
        foreach (char c in name)
        {
            hash = (hash ^ c) * 16777619;
        }

        Color[] colors = [PageKit.T.Accent, PageKit.T.Cyan, PageKit.T.Green, PageKit.T.Purple, PageKit.T.Orange, PageKit.T.Red];
        return colors[hash % colors.Length];
    }

    private static string Initial(string name) =>
        string.IsNullOrEmpty(name) ? "?" : name[..1].ToUpperInvariant();

    private static DateTime ResolveEnd(StatsData data)
    {
        DateTime today = DateTime.Today;
        string key = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (data.History.ContainsKey(key))
        {
            return today;
        }

        DateTime latest = DateTime.MinValue;
        foreach (DayRecord day in data.History.Values)
        {
            if (day.Date > latest)
            {
                latest = day.Date;
            }
        }

        return latest == DateTime.MinValue ? today : latest;
    }

    private sealed class AppRow
    {
        public string Name { get; init; } = "";
        public string Detail { get; init; } = "";
        public double Seconds { get; set; }
        public long Keys { get; set; }
        public long Clicks { get; set; }
        public long Wheel { get; set; }
        public long[] Groups { get; } = new long[6];
    }

    private sealed class Snapshot
    {
        public List<AppRow> Apps { get; } = new();
        public double ObservedSeconds { get; private set; }
        public double AttributedSeconds { get; private set; }
        public long Switches { get; private set; }
        public int Days { get; private set; }

        public static Snapshot Build(StatsData data, DateTime end, int range)
        {
            Snapshot result = new();
            Dictionary<string, AppRow> apps = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < range; i++)
            {
                string key = end.AddDays(-i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (!data.History.TryGetValue(key, out DayRecord? day))
                {
                    continue;
                }

                result.Days++;
                result.ObservedSeconds += day.AppObservedSeconds;
                result.Switches += day.AppSwitches;
                foreach (AppUsage app in day.Apps.Values)
                {
                    string id = app.ProcessPath.Length > 0 ? app.ProcessPath : app.AppName;
                    if (!apps.TryGetValue(id, out AppRow? row))
                    {
                        row = new AppRow
                        {
                            Name = app.AppName,
                            Detail = app.ProcessPath.Length > 0 ? app.ProcessPath : app.Title
                        };
                        apps[id] = row;
                    }

                    row.Seconds += app.ActiveSeconds;
                    row.Keys += app.Keys;
                    row.Clicks += app.Clicks;
                    row.Wheel += app.Wheel;
                    for (int group = 0; group < 6 && group < app.KeyGroups.Length; group++)
                    {
                        row.Groups[group] += app.KeyGroups[group];
                    }
                }
            }

            result.Apps.AddRange(apps.Values.OrderByDescending(app => app.Seconds));
            result.AttributedSeconds = result.Apps.Sum(app => app.Seconds);
            return result;
        }
    }
}
