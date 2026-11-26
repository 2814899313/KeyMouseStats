using Avalonia;
using Avalonia.Media;
using Kms.Render.Tokens;

namespace Kms.Render.Primitives;

/// <summary>一个键帽的几何数据（单位：1u 键宽；X/Y 为左上角，Width/Height 为键位数）。</summary>
public sealed record KeyCap(
    string Label,
    int Scan,
    int VirtualKey,
    double X,
    double Y,
    double Width,
    double Height);

public readonly record struct KeyCapGeometry(KeyCap Key, Rect Bounds);

public sealed record KeyCapGridGeometry(
    Rect Bounds,
    IReadOnlyList<KeyCapGeometry> Keys,
    int Layout,
    double Unit,
    double WidthUnits,
    double HeightUnits)
{
    public KeyCapGeometry? HitTest(Point point)
    {
        for (int i = Keys.Count - 1; i >= 0; i--)
        {
            if (Keys[i].Bounds.Contains(point))
            {
                return Keys[i];
            }
        }

        return null;
    }
}

/// <summary>
/// 键盘热力图的 6 种配列几何表。数据移植自 v3 KeyboardHeat.Layout（1u 网格）：
/// 0 自动（按 87 键兜底）、1 ANSI 104、2 87 键、3 75%、4 65%、5 60%。
/// 只负责几何与简单键帽，不画贴图。
/// </summary>
public static class KeyCapGrid
{
    public const int LayoutAuto = 0;
    public const int LayoutAnsi104 = 1;
    public const int LayoutTkl87 = 2;
    public const int Layout75 = 3;
    public const int Layout65 = 4;
    public const int Layout60 = 5;

    public static IReadOnlyList<string> Names { get; } = new[]
    {
        "自动（设备建议）",
        "全尺寸 · ANSI 104",
        "无小键盘 · 87 键",
        "75% · 紧凑",
        "65% · 紧凑",
        "60% · 紧凑"
    };

    private static readonly IReadOnlyList<KeyCap>[] Tables = BuildAll();

    public static int NormalizeLayout(int layout) => layout is >= 0 and <= 5 ? layout : 0;

    public static IReadOnlyList<KeyCap> GetLayout(int layout) => Tables[NormalizeLayout(layout)];

    public static KeyCapGridGeometry Measure(
        Rect bounds,
        int layout,
        double padding = 4,
        double gap = 1.5)
    {
        Rect normalized = PrimitiveMath.Normalize(bounds);
        Rect inner = PrimitiveMath.Inset(normalized, padding);
        int normalizedLayout = NormalizeLayout(layout);
        IReadOnlyList<KeyCap> keys = Tables[normalizedLayout];

        double maxX = 0;
        double maxY = 0;
        for (int i = 0; i < keys.Count; i++)
        {
            KeyCap key = keys[i];
            maxX = Math.Max(maxX, key.X + key.Width);
            maxY = Math.Max(maxY, key.Y + key.Height);
        }

        double unit = maxX > 0 && maxY > 0
            ? Math.Min(inner.Width / maxX, inner.Height / maxY)
            : 0;
        unit = Math.Max(0, unit);

        double offsetX = inner.X + (inner.Width - maxX * unit) / 2;
        double offsetY = inner.Y + (inner.Height - maxY * unit) / 2;
        double spacing = Math.Max(0, PrimitiveMath.Finite(gap));
        KeyCapGeometry[] result = new KeyCapGeometry[keys.Count];

        for (int i = 0; i < keys.Count; i++)
        {
            KeyCap key = keys[i];
            Rect rect = new(
                offsetX + key.X * unit,
                offsetY + key.Y * unit,
                key.Width * unit,
                key.Height * unit);

            if (spacing > 0)
            {
                rect = PrimitiveMath.Inset(rect, spacing / 2);
            }

            result[i] = new KeyCapGeometry(key, rect);
        }

        return new KeyCapGridGeometry(
            normalized,
            result,
            normalizedLayout,
            unit,
            maxX,
            maxY);
    }

    public static void Draw(
        DrawingContext context,
        KeyCapGridGeometry geometry,
        ThemeTokens theme,
        IReadOnlyDictionary<int, long>? counts = null,
        int channel = HeatScale.All,
        double fontSize = 0)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(theme);

        long max = 0;
        if (counts is not null)
        {
            foreach (long value in counts.Values)
            {
                max = Math.Max(max, Math.Max(0, value));
            }
        }

        double textSize = fontSize > 0
            ? fontSize
            : Math.Clamp(geometry.Unit * 0.22, 7, 11);

        IPen border = new Pen(new SolidColorBrush(theme.Line), 0.8);
        IBrush labelBrush = new SolidColorBrush(theme.Text);

        for (int i = 0; i < geometry.Keys.Count; i++)
        {
            KeyCapGeometry key = geometry.Keys[i];
            Rect rect = key.Bounds;
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                continue;
            }

            Color fill = theme.Raised;
            if (counts is not null && counts.TryGetValue(key.Key.Scan, out long count))
            {
                fill = HeatScale.For(theme, channel, Math.Max(0, count), max);
            }

            double radius = Math.Min(3, Math.Min(rect.Width, rect.Height) / 2);
            IBrush brush = new SolidColorBrush(fill);
            if (radius > 0)
            {
                context.DrawRectangle(brush, border, new RoundedRect(rect, radius, radius), default);
            }
            else
            {
                context.FillRectangle(brush, rect, 0);
            }

            if (string.IsNullOrEmpty(key.Key.Label) || textSize <= 0 || rect.Width < 8)
            {
                continue;
            }

            using (context.PushClip(rect))
            {
                TextBlock.DrawCentered(
                    context,
                    key.Key.Label,
                    PrimitiveMath.Inset(rect, 1),
                    textSize,
                    labelBrush,
                    TextBlock.MonoFamily,
                    maxWidth: Math.Max(0, rect.Width - 2));
            }
        }
    }

    /// <summary>登记每个键帽的命中区；payload 为 <see cref="KeyCapHit"/>。</summary>
    public static void RegisterHits(
        HitLayer layer,
        KeyCapGridGeometry geometry,
        IReadOnlyDictionary<int, long>? counts = null,
        string idPrefix = "key")
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(geometry);

        for (int i = 0; i < geometry.Keys.Count; i++)
        {
            KeyCapGeometry key = geometry.Keys[i];
            long count = 0;
            counts?.TryGetValue(key.Key.Scan, out count);
            layer.Add(
                $"{idPrefix}:{key.Key.Scan}:{i}",
                key.Bounds,
                new KeyCapHit(key.Key.Scan, key.Key.VirtualKey, key.Key.Label, count));
        }
    }

    private static IReadOnlyList<KeyCap>[] BuildAll()
    {
        IReadOnlyList<KeyCap>[] tables = new IReadOnlyList<KeyCap>[6];
        for (int i = 0; i < tables.Length; i++)
        {
            tables[i] = Build(i);
        }

        return tables;
    }

    private static List<KeyCap> Build(int layout)
    {
        List<KeyCap> keys = [];
        bool compact = layout >= 3;
        bool fnRow = layout <= 3;
        double top = fnRow ? 1.35 : 0;

        Row(
            keys,
            "`|1|2|3|4|5|6|7|8|9|0|-|=|Back",
            [41, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14],
            [192, 49, 50, 51, 52, 53, 54, 55, 56, 57, 48, 189, 187, 8],
            top,
            1,
            2);

        Row(
            keys,
            "Tab|Q|W|E|R|T|Y|U|I|O|P|[|]|\\",
            [15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 43],
            [9, 81, 87, 69, 82, 84, 89, 85, 73, 79, 80, 219, 221, 220],
            top + 1,
            1.5,
            1.5);

        Row(
            keys,
            "Caps|A|S|D|F|G|H|J|K|L|;|'|Enter",
            [58, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 28],
            [20, 65, 83, 68, 70, 71, 72, 74, 75, 76, 186, 222, 13],
            top + 2,
            1.75,
            2.25);

        Row(
            keys,
            "Shift|Z|X|C|V|B|N|M|,|.|/|Shift",
            [42, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54],
            [160, 90, 88, 67, 86, 66, 78, 77, 188, 190, 191, 161],
            top + 3,
            2.25,
            compact && layout != 5 ? 1.75 : 2.75);

        keys.Add(new KeyCap("Ctrl", 29, 162, 0, top + 4, 1.25, 1));
        keys.Add(new KeyCap("Win", 347, 91, 1.25, top + 4, 1.25, 1));
        keys.Add(new KeyCap("Alt", 56, 164, 2.5, top + 4, 1.25, 1));
        keys.Add(new KeyCap("Space", 57, 32, 3.75, top + 4, 6.25, 1));

        if (compact && layout != 5)
        {
            string[] labels = ["Alt", "Fn", "←", "↓", "→"];
            int[] scans = [312, 0, 331, 336, 333];
            int[] vks = [165, 0, 37, 40, 39];
            for (int i = 0; i < labels.Length; i++)
            {
                keys.Add(new KeyCap(labels[i], scans[i], vks[i], 10 + i, top + 4, 1, 1));
            }

            keys.Add(new KeyCap("↑", 328, 38, 14, top + 3, 1, 1));

            string[] nav = ["Del", "Home", "PgUp", "PgDn"];
            int[] navScans = [339, 327, 329, 337];
            int[] navVks = [46, 36, 33, 34];
            for (int i = 0; i < nav.Length; i++)
            {
                keys.Add(new KeyCap(nav[i], navScans[i], navVks[i], 15.25, top + i, 1, 1));
            }
        }
        else
        {
            string[] labels = ["Alt", "Win", "Menu", "Ctrl"];
            int[] scans = [312, 348, 349, 285];
            int[] vks = [165, 92, 93, 163];
            for (int i = 0; i < labels.Length; i++)
            {
                keys.Add(new KeyCap(labels[i], scans[i], vks[i], 10 + i * 1.25, top + 4, 1.25, 1));
            }
        }

        if (fnRow)
        {
            keys.Add(new KeyCap("Esc", 1, 27, 0, 0, 1, 1));
            for (int i = 0; i < 12; i++)
            {
                keys.Add(new KeyCap(
                    "F" + (i + 1),
                    i < 10 ? 59 + i : 87 + i - 10,
                    112 + i,
                    compact ? 1.25 + i * 1.1 : 2 + i + i / 4 * 0.5,
                    0,
                    1,
                    1));
            }
        }
        else
        {
            // 60/65 常见把 Esc 放在 ` 键；固件层无法推断，只做标注。
            keys[0] = keys[0] with { Label = "` / Esc" };
        }

        if (!compact)
        {
            string[] labels = ["PrtSc", "ScrLk", "Pause", "Ins", "Home", "PgUp", "Del", "End", "PgDn"];
            int[] scans = [311, 70, 581, 338, 327, 329, 339, 335, 337];
            int[] vks = [44, 145, 19, 45, 36, 33, 46, 35, 34];
            for (int i = 0; i < labels.Length; i++)
            {
                keys.Add(new KeyCap(labels[i], scans[i], vks[i], 15.5 + i % 3, i < 3 ? 0 : top + (i - 3) / 3, 1, 1));
            }

            keys.Add(new KeyCap("↑", 328, 38, 16.5, top + 3, 1, 1));

            string[] arrows = ["←", "↓", "→"];
            int[] arrowScans = [331, 336, 333];
            int[] arrowVks = [37, 40, 39];
            for (int i = 0; i < arrows.Length; i++)
            {
                keys.Add(new KeyCap(arrows[i], arrowScans[i], arrowVks[i], 15.5 + i, top + 4, 1, 1));
            }

            if (layout == LayoutAnsi104)
            {
                string[] numpadLabels = ["Num", "/", "*", "−", "7", "8", "9", "+", "4", "5", "6", "1", "2", "3", "Enter", "0", "."];
                int[] numpadScans = [325, 309, 55, 74, 71, 72, 73, 78, 75, 76, 77, 79, 80, 81, 284, 82, 83];
                int[] numpadVks = [144, 111, 106, 109, 103, 104, 105, 107, 100, 101, 102, 97, 98, 99, 13, 96, 110];
                double[] xs = [0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 0, 1, 2, 3, 0, 2];
                double[] ys = [0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 3, 3, 3, 3, 4, 4];

                for (int i = 0; i < numpadLabels.Length; i++)
                {
                    keys.Add(new KeyCap(
                        numpadLabels[i],
                        numpadScans[i],
                        numpadVks[i],
                        19 + xs[i],
                        top + ys[i],
                        i == 15 ? 2 : 1,
                        i == 7 || i == 14 ? 2 : 1));
                }
            }
        }

        return keys;
    }

    private static void Row(
        List<KeyCap> keys,
        string labels,
        int[] scans,
        int[] vks,
        double y,
        double first,
        double last)
    {
        string[] words = labels.Split('|');
        double x = 0;
        for (int i = 0; i < words.Length; i++)
        {
            double width = i == 0 ? first : i == words.Length - 1 ? last : 1;
            keys.Add(new KeyCap(words[i], scans[i], vks[i], x, y, width, 1));
            x += width;
        }
    }
}

public readonly record struct KeyCapHit(int Scan, int VirtualKey, string Label, long Count);
