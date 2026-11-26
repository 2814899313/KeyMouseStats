using System.Globalization;
using System.Text;
using Kms.Core.Model;

namespace Kms.Storage.Text;

/// <summary>
/// v13 文本各段落的编解码。格式与 v3 落盘逐字段对齐：
/// 数字一律 InvariantCulture，浮点用 "R"，屏幕移动量用 "0.###"，
/// 空集合用 "-" 哨兵，路径/标题/名称用 Base64。
/// </summary>
public static class V13Codecs
{
    public const int MaxClickCellsPerDay = 32768;
    public const int ClickCellCapacity = MaxClickCellsPerDay * 5;
    public const int KeyboardKeySlots = 256;
    public const int MouseDaySlots = 7;
    public const int HoldBucketCount = 8;
    public const int HoldKeyDetailLimit = 48;
    public const int OtherHoldKey = -1;
    public const int AppDetailLimit = 12;
    public const int AppCategoryCount = 10;
    public const int MonthArchiveAppLimit = 16;
    public const int ThemeCount = 13;
    public const int KeyboardLayoutCount = 6;
    public const string OtherAppPath = "[其他应用汇总]";
    public const long DefaultKeyboardRated = 50_000_000;
    public const long DefaultMouseRated = 80_000_000;
    public const long MaxRated = 10_000_000_000;
    public const string LegacyKeyboardId = "legacy";
    public const string LegacyMouseId = "mlegacy";
    public const int MotionLevelOff = 0;
    public const int MotionLevelReduced = 1;
    public const int MotionLevelFull = 2;
    public const int MinIdleThresholdSeconds = 10;
    public const int MaxIdleThresholdSeconds = 500;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // ---------------------------------------------------------------- 基础解析

    /// <summary>与 v3 <c>Store.ParseL</c> 相同：失败按 0，允许千分位等 NumberStyles.Any。</summary>
    public static long ParseLong(string? text)
    {
        long value;
        long.TryParse((text ?? "").Trim(), NumberStyles.Any, Invariant, out value);
        return value;
    }

    /// <summary>与 v3 <c>Store.ParseD</c> 相同：失败按 0，NaN/Infinity 会原样通过。</summary>
    public static double ParseDouble(string? text)
    {
        double value;
        double.TryParse((text ?? "").Trim(), NumberStyles.Any, Invariant, out value);
        return value;
    }

    /// <summary>解析失败、负数、NaN、Infinity 一律为 0（v3 ActivityMonitor.ParseSeconds）。</summary>
    public static double ParseSeconds(string? text)
    {
        double seconds;
        return double.TryParse((text ?? "").Trim(), NumberStyles.Float, Invariant, out seconds)
            && seconds >= 0
            && !double.IsInfinity(seconds)
            ? seconds
            : 0;
    }

    /// <summary>失败、NaN、Infinity、负数一律为 0（v3 Store.NonNegative）。</summary>
    public static double NonNegative(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value) || value < 0 ? 0 : value;
    }

    /// <summary>v3 MonthArchive.ParseDouble：失败或 NaN/Infinity 为 0，负值交给调用方 Math.Max。</summary>
    public static double ParseFiniteDouble(string? text)
    {
        double value;
        double.TryParse((text ?? "").Trim(), NumberStyles.Float, Invariant, out value);
        return double.IsNaN(value) || double.IsInfinity(value) ? 0 : value;
    }

    public static string FormatDouble(double value) => value.ToString("R", Invariant);

    public static string FormatMove(double value) => value.ToString("0.###", Invariant);

    // ---------------------------------------------------------------- Base64 文本

    public static string EncodeText(string? value)
    {
        return string.IsNullOrEmpty(value) ? "" : Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

    /// <summary>严格解码：空串为 ""，非法 Base64 返回 false（v3 AppActivity.Decode 抛出 FormatException 的效果）。</summary>
    public static bool TryDecodeText(string? value, out string decoded)
    {
        decoded = "";
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>宽松解码：非法 Base64 原样返回（v3 PeripheralWear.DecodeName）。</summary>
    public static string DecodeStoredName(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
        catch (FormatException)
        {
            return value;
        }
    }

    // ---------------------------------------------------------------- 点击热区

    public static int ClickCellKey(int cellX, int cellY)
    {
        int x = Math.Max(-32768, Math.Min(32767, cellX)) + 32768;
        int y = Math.Max(-32768, Math.Min(32767, cellY)) + 32768;
        return (x << 16) | y;
    }

    public static void SplitClickCellKey(int key, out int cellX, out int cellY)
    {
        cellX = ((key >> 16) & 0xFFFF) - 32768;
        cellY = (key & 0xFFFF) - 32768;
    }

    public static long ClickChannelKey(int channel, int cellKey)
    {
        return ((long)channel << 32) | (uint)cellKey;
    }

    public static int ClickChannelOfKey(long key) => (int)(key >> 32);

    public static int ClickCellKeyOfChannelKey(long key) => (int)(key & 0xFFFFFFFF);

    /// <summary>单通道热区：cx:cy:count，按 key 升序，最多 32768 格。</summary>
    public static string EncodeClickCells(Dictionary<int, long>? cells)
    {
        if (cells == null || cells.Count == 0)
        {
            return "";
        }

        List<int> keys = new(cells.Keys);
        keys.Sort();
        StringBuilder sb = new(Math.Min(keys.Count, MaxClickCellsPerDay) * 10);
        int written = 0;
        foreach (int key in keys)
        {
            if (written >= MaxClickCellsPerDay)
            {
                break;
            }

            long count = cells[key];
            if (count <= 0)
            {
                continue;
            }

            SplitClickCellKey(key, out int cellX, out int cellY);
            if (sb.Length > 0)
            {
                sb.Append(',');
            }

            sb.Append(cellX.ToString(Invariant)).Append(':')
                .Append(cellY.ToString(Invariant)).Append(':')
                .Append(count.ToString(Invariant));
            written++;
        }

        return sb.ToString();
    }

    public static void DecodeClickCells(string? text, Dictionary<int, long>? target)
    {
        if (string.IsNullOrEmpty(text) || target == null)
        {
            return;
        }

        string[] parts = text.Split(',');
        for (int i = 0; i < parts.Length && target.Count < MaxClickCellsPerDay; i++)
        {
            string[] fields = parts[i].Split(':');
            if (fields.Length != 3)
            {
                continue;
            }

            if (!int.TryParse(fields[0], NumberStyles.Integer, Invariant, out int cellX)
                || !int.TryParse(fields[1], NumberStyles.Integer, Invariant, out int cellY)
                || !long.TryParse(fields[2], NumberStyles.Integer, Invariant, out long count))
            {
                continue;
            }

            if (count <= 0 || cellX < -32768 || cellX > 32767 || cellY < -32768 || cellY > 32767)
            {
                continue;
            }

            int key = ClickCellKey(cellX, cellY);
            target[key] = target.GetValueOrDefault(key) + Math.Min(count, 100_000_000L);
        }
    }

    /// <summary>三通道热区：channel:cx:cy:count，容量为 32768 × 5。</summary>
    public static string EncodeClickCellsV1(Dictionary<long, long>? cells)
    {
        if (cells == null || cells.Count == 0)
        {
            return "";
        }

        List<long> keys = new(cells.Keys);
        keys.Sort();
        StringBuilder sb = new(Math.Min(keys.Count, ClickCellCapacity) * 12);
        int written = 0;
        foreach (long key in keys)
        {
            if (written >= ClickCellCapacity)
            {
                break;
            }

            long count = cells[key];
            if (count <= 0)
            {
                continue;
            }

            int channel = ClickChannelOfKey(key);
            SplitClickCellKey(ClickCellKeyOfChannelKey(key), out int cellX, out int cellY);
            if (sb.Length > 0)
            {
                sb.Append(',');
            }

            sb.Append(channel.ToString(Invariant)).Append(':')
                .Append(cellX.ToString(Invariant)).Append(':')
                .Append(cellY.ToString(Invariant)).Append(':')
                .Append(count.ToString(Invariant));
            written++;
        }

        return sb.ToString();
    }

    public static void DecodeClickCellsV1(string? text, Dictionary<long, long>? target)
    {
        if (string.IsNullOrEmpty(text) || target == null)
        {
            return;
        }

        string[] parts = text.Split(',');
        for (int i = 0; i < parts.Length && target.Count < ClickCellCapacity; i++)
        {
            string[] fields = parts[i].Split(':');
            if (fields.Length != 4)
            {
                continue;
            }

            if (!int.TryParse(fields[0], NumberStyles.Integer, Invariant, out int channel)
                || !int.TryParse(fields[1], NumberStyles.Integer, Invariant, out int cellX)
                || !int.TryParse(fields[2], NumberStyles.Integer, Invariant, out int cellY)
                || !long.TryParse(fields[3], NumberStyles.Integer, Invariant, out long count))
            {
                continue;
            }

            if (channel < 0 || channel > 4 || count <= 0
                || cellX < -32768 || cellX > 32767 || cellY < -32768 || cellY > 32767)
            {
                continue;
            }

            long key = ClickChannelKey(channel, ClickCellKey(cellX, cellY));
            target[key] = target.GetValueOrDefault(key) + Math.Min(count, 100_000_000L);
        }
    }

    // ---------------------------------------------------------------- 小时桶 / 键位桶

    /// <summary>hk/hc 用：只写非零桶，全零写 "-"。</summary>
    public static string EncodeHourBuckets(long[] buckets)
    {
        StringBuilder sb = new();
        for (int i = 0; i < buckets.Length; i++)
        {
            if (buckets[i] == 0)
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append(',');
            }

            sb.Append(i.ToString(Invariant)).Append(':').Append(buckets[i].ToString(Invariant));
        }

        return sb.Length == 0 ? "-" : sb.ToString();
    }

    public static void DecodeHourBuckets(string? text, long[] buckets)
    {
        string[] parts = (text ?? "").Split(',');
        foreach (string part in parts)
        {
            int colon = part.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            int hour = (int)ParseLong(part[..colon].Trim());
            long value = ParseLong(part[(colon + 1)..].Trim());
            if (hour >= 0 && hour < 24)
            {
                buckets[hour] += value;
            }
        }
    }

    /// <summary>active_hours：固定 24 段 "R" 浮点。</summary>
    public static string EncodeHours(double[] hours)
    {
        string[] values = new string[24];
        for (int i = 0; i < 24; i++)
        {
            values[i] = FormatDouble(hours[i]);
        }

        return string.Join(",", values);
    }

    public static void DecodeHours(string? text, double[] hours)
    {
        string[] values = (text ?? "").Split(',');
        for (int i = 0; i < Math.Min(24, values.Length); i++)
        {
            hours[i] = ParseSeconds(values[i]);
        }
    }

    // ---------------------------------------------------------------- 逐键计数

    public static string EncodeKeyCounts(Dictionary<int, long>? counts)
    {
        if (counts == null || counts.Count == 0)
        {
            return "-";
        }

        List<int> keys = new(counts.Keys);
        keys.Sort();
        StringBuilder sb = new();
        foreach (int key in keys)
        {
            if (sb.Length > 0)
            {
                sb.Append(',');
            }

            sb.Append(key.ToString(Invariant)).Append(':').Append(counts[key].ToString(Invariant));
        }

        return sb.ToString();
    }

    public static void DecodeKeyCounts(string? text, Dictionary<int, long>? target, int limit = 256)
    {
        if (target == null)
        {
            return;
        }

        string[] parts = (text ?? "").Split(',');
        foreach (string part in parts)
        {
            int colon = part.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            int vk = (int)ParseLong(part[..colon].Trim());
            long value = ParseLong(part[(colon + 1)..].Trim());
            if (vk > 0 && vk < limit && value > 0)
            {
                target[vk] = target.GetValueOrDefault(vk) + value;
            }
        }
    }

    // ---------------------------------------------------------------- 组合键

    public static bool IsValidComboKey(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > 40)
        {
            return false;
        }

        string[] parts = key.Split('_');
        if (parts.Length < 2 || parts.Length > 5)
        {
            return false;
        }

        string[] modifiers = ["ctrl", "alt", "win", "shift"];
        int previous = -1;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            int index = Array.IndexOf(modifiers, parts[i]);
            if (index <= previous)
            {
                return false;
            }

            previous = index;
        }

        string last = parts[^1];
        if (last == "tab")
        {
            return true;
        }

        if (last.Length == 1
            && (last[0] >= 'a' && last[0] <= 'z' || last[0] >= '0' && last[0] <= '9'))
        {
            return true;
        }

        return last.Length == 4
            && last.StartsWith("vk", StringComparison.Ordinal)
            && int.TryParse(last.AsSpan(2), NumberStyles.HexNumber, Invariant, out int vk)
            && vk >= 8
            && vk < 255
            && vk != 16
            && vk != 17
            && vk != 18
            && !IsModifierVk(vk);
    }

    public static void DecodeCombo(DayRecord day, string? text)
    {
        int split = (text ?? "").IndexOf(':');
        if (split <= 0)
        {
            return;
        }

        string key = text![..split];
        long count;
        if (IsValidComboKey(key)
            && long.TryParse(text.AsSpan(split + 1), NumberStyles.Integer, Invariant, out count)
            && count > 0)
        {
            day.ComboCounts[key] = count;
        }
    }

    public static string EncodeCombo(string key, long count) => key + ":" + count.ToString(Invariant);

    private static bool IsModifierVk(int vk) => (vk >= 0xA0 && vk <= 0xA5) || vk == 0x5B || vk == 0x5C;

    // ---------------------------------------------------------------- 应用记录

    public static string EncodeApp(AppUsage app)
    {
        return EncodeText(app.ProcessPath) + "|" + EncodeText(app.Title) + "|"
            + app.Keys.ToString(Invariant) + "|" + app.Clicks.ToString(Invariant) + "|"
            + app.Wheel.ToString(Invariant) + "|" + FormatDouble(app.ActiveSeconds);
    }

    public static void DecodeApp(DayRecord day, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        string[] parts = value.Split('|');
        if (parts.Length != 5 && parts.Length != 6)
        {
            return;
        }

        if (!long.TryParse(parts[2], NumberStyles.Integer, Invariant, out long keys)
            || !long.TryParse(parts[3], NumberStyles.Integer, Invariant, out long clicks)
            || !long.TryParse(parts[4], NumberStyles.Integer, Invariant, out long wheel)
            || keys < 0 || clicks < 0 || wheel < 0)
        {
            return;
        }

        if (!TryDecodeText(parts[0], out string path) || !TryDecodeText(parts[1], out string title))
        {
            return;
        }

        AppUsage app = new()
        {
            ProcessPath = path,
            Title = title,
            Keys = keys,
            Clicks = clicks,
            Wheel = wheel
        };
        if (parts.Length == 6)
        {
            app.ActiveSeconds = ParseSeconds(parts[5]);
        }

        day.Apps[app.Id] = app;
    }

    public static string EncodeAppRule(AppRule rule)
    {
        return EncodeText(rule.RuleKey) + "|" + rule.CategoryId.ToString(Invariant);
    }

    public static bool TryDecodeAppRule(string? value, out AppRule? rule)
    {
        rule = null;
        string[] parts = (value ?? "").Split('|');
        if (parts.Length != 2
            || !int.TryParse(parts[1], NumberStyles.Integer, Invariant, out int category)
            || category < 0
            || category >= AppCategoryCount)
        {
            return false;
        }

        if (!TryDecodeText(parts[0], out string key))
        {
            return false;
        }

        rule = new AppRule(key, category);
        return true;
    }

    public static string EncodeAppKeyLine(string path, long[] groups)
    {
        return EncodeText(path) + "|" + EncodeGroups(groups);
    }

    public static string[] EncodeAppKeyLines(DayRecord day)
    {
        Dictionary<string, long[]> byPath = new(StringComparer.OrdinalIgnoreCase);
        foreach (AppUsage app in day.Apps.Values)
        {
            if (!app.HasKeyGroups)
            {
                continue;
            }

            string path = app.ProcessPath ?? "";
            if (!byPath.TryGetValue(path, out long[]? groups))
            {
                groups = new long[6];
                byPath[path] = groups;
            }

            for (int i = 0; i < 6 && i < app.KeyGroups.Length; i++)
            {
                groups[i] += app.KeyGroups[i];
            }
        }

        if (byPath.Count == 0)
        {
            return [];
        }

        List<KeyValuePair<string, long[]>> list = new(byPath);
        list.Sort((a, b) =>
        {
            long left = SumGroups(a.Value);
            long right = SumGroups(b.Value);
            int order = right.CompareTo(left);
            return order != 0 ? order : string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
        });

        List<string> lines = [];
        long[] other = new long[6];
        for (int i = 0; i < list.Count; i++)
        {
            if (i < AppDetailLimit)
            {
                lines.Add("app_keys_v1=" + EncodeAppKeyLine(list[i].Key, list[i].Value));
            }
            else
            {
                for (int group = 0; group < 6; group++)
                {
                    other[group] += list[i].Value[group];
                }
            }
        }

        if (SumGroups(other) > 0)
        {
            lines.Add("app_keys_v1=" + EncodeAppKeyLine(OtherAppPath, other));
        }

        return lines.ToArray();
    }

    public static void DecodeAppKey(DayRecord day, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        string[] fields = value.Split('|');
        if (fields.Length != 2 || !TryDecodeText(fields[0], out string path))
        {
            return;
        }

        string[] groups = fields[1].Split(',');
        long[] counts = new long[6];
        for (int i = 0; i < groups.Length && i < 6; i++)
        {
            if (long.TryParse(groups[i].Trim(), NumberStyles.Integer, Invariant, out long count) && count > 0)
            {
                counts[i] = count;
            }
        }

        AppUsage? target = null;
        foreach (AppUsage app in day.Apps.Values)
        {
            if (string.Equals(app.ProcessPath ?? "", path, StringComparison.OrdinalIgnoreCase))
            {
                target = app;
                break;
            }
        }

        if (target == null)
        {
            target = new AppUsage { ProcessPath = path, Title = "" };
            day.Apps[target.Id] = target;
        }

        target.HasKeyGroups = true;
        for (int i = 0; i < 6; i++)
        {
            target.KeyGroups[i] += counts[i];
        }
    }

    private static string EncodeGroups(long[] groups)
    {
        StringBuilder sb = new();
        for (int i = 0; i < 6; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append(i < groups.Length ? groups[i].ToString(Invariant) : "0");
        }

        return sb.ToString();
    }

    private static long SumGroups(long[] groups)
    {
        long total = 0;
        foreach (long value in groups)
        {
            total += value;
        }

        return total;
    }

    // ---------------------------------------------------------------- 活跃段 / 空闲段

    public static string EncodeSession(ActiveSession session)
    {
        string value = session.Start.ToString("O", Invariant) + "|"
            + session.End.ToString("O", Invariant) + "|"
            + FormatDouble(session.Seconds);
        if (session.RateMeasured)
        {
            value += "|" + session.PeakApm.ToString(Invariant)
                + "|" + FormatDouble(session.PeakOffsetSeconds)
                + "|" + session.LastApm.ToString(Invariant)
                + "|1";
        }

        return value;
    }

    public static string EncodeIdlePeriod(ActiveSession period)
    {
        return period.Start.ToString("O", Invariant) + "|"
            + period.End.ToString("O", Invariant) + "|"
            + FormatDouble(period.Seconds);
    }

    public static string EncodeSessionReason(ActiveSession session)
    {
        return session.Start.Ticks.ToString(Invariant) + "|"
            + ((int)session.StartReason).ToString(Invariant);
    }

    public static void DecodeSession(DayRecord day, string? text) => DecodePeriod(day, text, false);

    public static void DecodeIdlePeriod(DayRecord day, string? text) => DecodePeriod(day, text, true);

    public static void DecodeSessionReason(DayRecord day, string? text)
    {
        string[] fields = (text ?? "").Split('|');
        if (fields.Length != 2
            || !long.TryParse(fields[0], NumberStyles.Integer, Invariant, out long ticks)
            || !int.TryParse(fields[1], NumberStyles.Integer, Invariant, out int reason)
            || !Enum.IsDefined(typeof(SessionStartReason), reason))
        {
            return;
        }

        foreach (ActiveSession session in day.Sessions)
        {
            if (session.Start.Ticks == ticks)
            {
                session.StartReason = (SessionStartReason)reason;
                return;
            }
        }
    }

    private static void DecodePeriod(DayRecord day, string? text, bool idle)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        string[] fields = text.Split('|');
        if ((fields.Length != 3 && fields.Length != 7)
            || !DateTime.TryParseExact(fields[0], "O", Invariant, DateTimeStyles.RoundtripKind, out DateTime start)
            || !DateTime.TryParseExact(fields[1], "O", Invariant, DateTimeStyles.RoundtripKind, out DateTime end))
        {
            return;
        }

        double seconds = ParseSeconds(fields[2]);
        if (start.Date != day.Date
            || end < start
            || end > day.Date.AddDays(1)
            || seconds <= 0
            || seconds > (end - start).TotalSeconds + 0.001)
        {
            return;
        }

        ActiveSession session = new() { Start = start, End = end, Seconds = seconds };
        if (!idle
            && fields.Length == 7
            && fields[6] == "1"
            && long.TryParse(fields[3], NumberStyles.Integer, Invariant, out long peak)
            && long.TryParse(fields[5], NumberStyles.Integer, Invariant, out long last)
            && peak >= 0
            && last >= 0
            && last <= peak)
        {
            session.RateMeasured = true;
            session.PeakApm = peak;
            session.LastApm = last;
            session.PeakOffsetSeconds = Math.Min(seconds, ParseSeconds(fields[4]));
        }

        (idle ? day.IdlePeriods : day.Sessions).Add(session);
    }

    // ---------------------------------------------------------------- 交叉遥测

    public static void EncodeCross(DayRecord day, StringBuilder sb)
    {
        sb.AppendLine("cross_hours_v1=" + EncodeHours(day.Cross.ObservedHours));
        foreach (KeyValuePair<string, double[]> app in day.Cross.AppHours)
        {
            sb.AppendLine("app_hours_v1=" + EncodeText(app.Key) + "|" + EncodeHours(app.Value));
        }

        foreach (ActiveSession session in day.Sessions)
        {
            sb.AppendLine("session_coverage_v1=" + session.Start.Ticks.ToString(Invariant)
                + "|" + FormatDouble(session.CrossObservedSeconds));
            foreach (KeyValuePair<string, double> app in session.AppSeconds)
            {
                sb.AppendLine("session_app_v1=" + session.Start.Ticks.ToString(Invariant)
                    + "|" + EncodeText(app.Key) + "|" + FormatDouble(app.Value));
            }
        }

        CrossDay cross = day.Cross;
        double[] values =
        [
            cross.Direction[0], cross.Direction[1], cross.Direction[2], cross.Direction[3],
            cross.PathCounts, cross.DragCounts, cross.Packets, cross.CalibratedPackets,
            cross.Flicks, cross.Reversals, cross.Clicks, cross.MoveClicks
        ];
        sb.AppendLine("mouse_vector_v1=" + string.Join("|", values.Select(FormatDouble)));
    }

    public static void DecodeCross(DayRecord day, string key, string? value)
    {
        string[] fields = (value ?? "").Split('|');
        if (key == "cross_hours_v1")
        {
            DecodeHours(value, day.Cross.ObservedHours);
        }
        else if (key == "app_hours_v1" && fields.Length == 2)
        {
            if (!TryDecodeText(fields[0], out string path))
            {
                return;
            }

            if (day.Cross.AppHours.Count >= 129 && !day.Cross.AppHours.ContainsKey(path))
            {
                return;
            }

            double[] hours = new double[24];
            DecodeHours(fields[1], hours);
            day.Cross.AppHours[path] = hours;
        }
        else if ((key == "session_app_v1" && fields.Length == 3)
            || (key == "session_coverage_v1" && fields.Length == 2))
        {
            if (!long.TryParse(fields[0], NumberStyles.Integer, Invariant, out long ticks))
            {
                return;
            }

            foreach (ActiveSession session in day.Sessions)
            {
                if (session.Start.Ticks != ticks)
                {
                    continue;
                }

                if (fields.Length == 2)
                {
                    session.CrossObservedSeconds = ParseSeconds(fields[1]);
                }
                else if (TryDecodeText(fields[1], out string path)
                    && (session.AppSeconds.Count < 33 || session.AppSeconds.ContainsKey(path)))
                {
                    session.AppSeconds[path] = ParseSeconds(fields[2]);
                }

                return;
            }
        }
        else if (key == "mouse_vector_v1" && fields.Length == 12)
        {
            double[] values = new double[12];
            for (int i = 0; i < 12; i++)
            {
                values[i] = ParseSeconds(fields[i]);
            }

            CrossDay cross = day.Cross;
            Array.Copy(values, cross.Direction, 4);
            cross.PathCounts = values[4];
            cross.DragCounts = Math.Min(values[5], values[4]);
            cross.Packets = Count(values[6]);
            cross.CalibratedPackets = Math.Min(cross.Packets, Count(values[7]));
            cross.Flicks = Count(values[8]);
            cross.Reversals = Count(values[9]);
            cross.Clicks = Count(values[10]);
            cross.MoveClicks = Math.Min(cross.Clicks, Count(values[11]));
        }
    }

    private static long Count(double value)
    {
        return value >= long.MaxValue ? long.MaxValue : (long)value;
    }

    // ---------------------------------------------------------------- 按住时长

    /// <summary>hold_v1=b1,…,b8|样本数|总毫秒|最大毫秒|丢弃数。</summary>
    public static string? EncodeHoldSummary(DayRecord day)
    {
        if (day.HoldCount == 0 && day.HoldDiscarded == 0)
        {
            return null;
        }

        StringBuilder sb = new();
        for (int i = 0; i < HoldBucketCount; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append(day.HoldBuckets[i].ToString(Invariant));
        }

        sb.Append('|').Append(day.HoldCount.ToString(Invariant));
        sb.Append('|').Append(FormatDouble(day.HoldTotalMs));
        sb.Append('|').Append(FormatDouble(day.HoldMaxMs));
        sb.Append('|').Append(day.HoldDiscarded.ToString(Invariant));
        return sb.ToString();
    }

    public static void DecodeHoldSummary(DayRecord day, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        string[] parts = value.Split('|');
        if (parts.Length < 5)
        {
            return;
        }

        string[] buckets = parts[0].Split(',');
        for (int i = 0; i < buckets.Length && i < day.HoldBuckets.Length; i++)
        {
            day.HoldBuckets[i] += Math.Max(0, ParseLong(buckets[i].Trim()));
        }

        day.HoldCount += Math.Max(0, ParseLong(parts[1]));
        day.HoldTotalMs += ParseHoldNumber(parts[2]);
        double max = ParseHoldNumber(parts[3]);
        if (max > day.HoldMaxMs)
        {
            day.HoldMaxMs = max;
        }

        day.HoldDiscarded += Math.Max(0, ParseLong(parts[4]));
    }

    /// <summary>hold_key_v1=vk:次数:总毫秒:最大毫秒,…；超上限并入 vk=-1。</summary>
    public static string? EncodeHoldKeys(DayRecord day)
    {
        if (day.Holds.Count == 0)
        {
            return null;
        }

        List<KeyValuePair<int, KeyHold>> list = new(day.Holds);
        list.Sort((a, b) =>
        {
            int order = b.Value.Count.CompareTo(a.Value.Count);
            return order != 0 ? order : a.Key.CompareTo(b.Key);
        });

        KeyHold other = new();
        StringBuilder sb = new();
        for (int i = 0; i < list.Count; i++)
        {
            if (i >= HoldKeyDetailLimit)
            {
                other.Count += list[i].Value.Count;
                other.TotalMs += list[i].Value.TotalMs;
                if (list[i].Value.MaxMs > other.MaxMs)
                {
                    other.MaxMs = list[i].Value.MaxMs;
                }

                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append(',');
            }

            sb.Append(list[i].Key.ToString(Invariant)).Append(':')
                .Append(list[i].Value.Count.ToString(Invariant)).Append(':')
                .Append(FormatDouble(list[i].Value.TotalMs)).Append(':')
                .Append(FormatDouble(list[i].Value.MaxMs));
        }

        if (other.Count > 0)
        {
            if (sb.Length > 0)
            {
                sb.Append(',');
            }

            sb.Append(OtherHoldKey.ToString(Invariant)).Append(':')
                .Append(other.Count.ToString(Invariant)).Append(':')
                .Append(FormatDouble(other.TotalMs)).Append(':')
                .Append(FormatDouble(other.MaxMs));
        }

        return sb.Length == 0 ? null : sb.ToString();
    }

    public static void DecodeHoldKeys(DayRecord day, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        foreach (string entry in value.Split(','))
        {
            string[] fields = entry.Split(':');
            if (fields.Length != 4)
            {
                continue;
            }

            if (!int.TryParse(fields[0].Trim(), NumberStyles.Integer, Invariant, out int vk)
                || !long.TryParse(fields[1].Trim(), NumberStyles.Integer, Invariant, out long count)
                || count <= 0
                || !double.TryParse(fields[2].Trim(), NumberStyles.Float, Invariant, out double total)
                || !double.TryParse(fields[3].Trim(), NumberStyles.Float, Invariant, out double max))
            {
                continue;
            }

            if (!day.Holds.TryGetValue(vk, out KeyHold? hold))
            {
                hold = new KeyHold();
                day.Holds[vk] = hold;
            }

            hold.Count += count;
            hold.TotalMs += total;
            if (max > hold.MaxMs)
            {
                hold.MaxMs = max;
            }
        }
    }

    public static void DecodeHoldActive(DayRecord day, string? value)
    {
        day.HoldActiveSeconds += ParseHoldNumber(value);
    }

    public static void AppendHoldLines(DayRecord day, List<string> lines)
    {
        string? summary = EncodeHoldSummary(day);
        if (summary != null)
        {
            lines.Add("hold_v1=" + summary);
        }

        string? keys = EncodeHoldKeys(day);
        if (keys != null)
        {
            lines.Add("hold_key_v1=" + keys);
        }

        if (day.HoldActiveSeconds > 0)
        {
            lines.Add("hold_active_v1=" + FormatDouble(day.HoldActiveSeconds));
        }
    }

    private static double ParseHoldNumber(string? text)
    {
        double value;
        if (double.TryParse((text ?? "").Trim(), NumberStyles.Float, Invariant, out value)
            && !double.IsNaN(value)
            && !double.IsInfinity(value)
            && value >= 0)
        {
            return value;
        }

        return 0;
    }

    // ---------------------------------------------------------------- 月归档

    public static string EncodeArchive(MonthArchive archive)
    {
        StringBuilder sb = new();
        sb.Append(archive.Key).Append('|');
        sb.Append(archive.Days.ToString(Invariant)).Append('|');
        sb.Append(archive.Keys.ToString(Invariant)).Append('|');
        sb.Append(archive.Clicks.ToString(Invariant)).Append('|');
        sb.Append(archive.Wheel.ToString(Invariant)).Append('|');
        sb.Append(archive.Combos.ToString(Invariant)).Append('|');
        sb.Append(FormatDouble(archive.ActiveSeconds)).Append('|');
        sb.Append(FormatDouble(archive.MoveMeters)).Append('|');
        sb.Append(archive.Sessions.ToString(Invariant)).Append('|');

        List<KeyValuePair<string, long>> apps = new(archive.AppKeys);
        apps.Sort((a, b) =>
        {
            int order = b.Value.CompareTo(a.Value);
            return order != 0 ? order : string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
        });
        for (int i = 0; i < apps.Count && i < MonthArchiveAppLimit; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append(EncodeText(apps[i].Key)).Append(':').Append(apps[i].Value.ToString(Invariant));
        }

        sb.Append('|').Append(archive.HoldCount.ToString(Invariant));
        sb.Append('|').Append(FormatDouble(archive.HoldTotalMs));
        sb.Append('|').Append(FormatDouble(archive.HoldMaxMs));
        sb.Append('|').Append(FormatDouble(archive.HoldActiveSeconds));
        sb.Append('|').Append(archive.HoldDiscarded.ToString(Invariant));
        sb.Append('|').Append(archive.HWheel.ToString(Invariant));
        return sb.ToString();
    }

    public static bool TryDecodeArchive(string? value, out MonthArchive? archive)
    {
        archive = null;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        string[] fields = value.Split('|');
        if (fields.Length < 9)
        {
            return false;
        }

        string[] stamp = fields[0].Split('-');
        if (stamp.Length != 2
            || !int.TryParse(stamp[0], NumberStyles.Integer, Invariant, out int year)
            || !int.TryParse(stamp[1], NumberStyles.Integer, Invariant, out int month)
            || month < 1
            || month > 12)
        {
            return false;
        }

        MonthArchive result = new()
        {
            Year = year,
            Month = month,
            Days = (int)Math.Max(0, ParseLong(fields[1])),
            Keys = Math.Max(0, ParseLong(fields[2])),
            Clicks = Math.Max(0, ParseLong(fields[3])),
            Wheel = Math.Max(0, ParseLong(fields[4])),
            Combos = Math.Max(0, ParseLong(fields[5])),
            ActiveSeconds = Math.Max(0, ParseFiniteDouble(fields[6])),
            MoveMeters = Math.Max(0, ParseFiniteDouble(fields[7])),
            Sessions = (int)Math.Max(0, ParseLong(fields[8]))
        };

        if (fields.Length >= 10 && fields[9].Length > 0)
        {
            foreach (string entry in fields[9].Split(','))
            {
                int colon = entry.LastIndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }

                // v3 的 AddTo 外层 catch 会让一条坏 Base64 丢弃整条归档行。
                if (!TryDecodeText(entry[..colon], out string path))
                {
                    return false;
                }

                long appKeys = ParseLong(entry[(colon + 1)..]);
                if (appKeys <= 0)
                {
                    continue;
                }

                result.AppKeys[path] = result.AppKeys.GetValueOrDefault(path) + appKeys;
            }
        }

        if (fields.Length >= 15)
        {
            result.HoldCount = Math.Max(0, ParseLong(fields[10]));
            result.HoldTotalMs = Math.Max(0, ParseFiniteDouble(fields[11]));
            result.HoldMaxMs = Math.Max(0, ParseFiniteDouble(fields[12]));
            result.HoldActiveSeconds = Math.Max(0, ParseFiniteDouble(fields[13]));
            result.HoldDiscarded = Math.Max(0, ParseLong(fields[14]));
        }

        if (fields.Length >= 16)
        {
            result.HWheel = Math.Max(0, ParseLong(fields[15]));
        }

        archive = result;
        return true;
    }

    // ---------------------------------------------------------------- 键盘档案

    public static string EncodeKeyboardHeader(KeyboardProfile profile)
    {
        int flags = (profile.BuiltIn ? 1 : 0) | (profile.Legacy ? 2 : 0) | (profile.Pending ? 4 : 0);
        return profile.Id + "|" + EncodeText(profile.Alias) + "|"
            + EncodeText(profile.FriendlyName) + "|"
            + EncodeText(profile.DevicePath) + "|"
            + EncodeText(profile.HardwareKey) + "|"
            + EncodeText(profile.InstanceKey) + "|"
            + flags.ToString(Invariant) + "|"
            + profile.Rated.ToString(Invariant) + "|"
            + TimeText(profile.Since) + "|"
            + (profile.Seeded ? "1" : "0") + "|"
            + profile.LastSeenUtcTicks.ToString(Invariant);
    }

    public static KeyboardProfile? DecodeKeyboardHeader(string? text)
    {
        string[] parts = (text ?? "").Split('|');
        if (parts.Length < 11)
        {
            return null;
        }

        string id = parts[0] ?? "";
        if (id.Length == 0)
        {
            return null;
        }

        KeyboardProfile profile = new()
        {
            Id = id,
            Alias = DecodeStoredName(parts[1]),
            FriendlyName = DecodeStoredName(parts[2]),
            DevicePath = DecodeStoredName(parts[3]),
            HardwareKey = DecodeStoredName(parts[4]),
            InstanceKey = DecodeStoredName(parts[5])
        };

        int flags = (int)ParseLong(parts[6]);
        profile.BuiltIn = (flags & 1) != 0;
        profile.Legacy = (flags & 2) != 0;
        profile.Pending = (flags & 4) != 0;
        profile.Rated = ClampRated(ParseLong(parts[7]), DefaultKeyboardRated);
        profile.Since = ParseTimeText(parts[8]);
        profile.Seeded = parts[9] == "1";
        profile.LastSeenUtcTicks = ParseLong(parts[10]);
        return profile;
    }

    /// <summary>keyboard_keys_v1：id|vk:count,…（全零写 "-"）。</summary>
    public static string EncodeKeyboardKeys(KeyboardProfile profile)
    {
        return profile.Id + "|" + EncodeKeyboardKeyPayload(profile);
    }

    public static string EncodeKeyboardKeyPayload(KeyboardProfile profile)
    {
        StringBuilder sb = new();
        for (int vk = 0; vk < profile.Keys.Length; vk++)
        {
            long value = profile.Keys[vk];
            if (value <= 0)
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append(',');
            }

            sb.Append(vk.ToString(Invariant)).Append(':').Append(value.ToString(Invariant));
        }

        return sb.Length == 0 ? "-" : sb.ToString();
    }

    public static void DecodeKeyboardKeys(KeyboardProfile profile, string? text)
    {
        Array.Clear(profile.Keys);
        if (string.IsNullOrEmpty(text) || text == "-")
        {
            return;
        }

        foreach (string entry in text.Split(','))
        {
            int colon = entry.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            int vk = (int)ParseLong(entry[..colon]);
            long value = ParseLong(entry[(colon + 1)..]);
            if (vk >= 0 && vk < profile.Keys.Length && value > 0)
            {
                profile.Keys[vk] = value;
            }
        }
    }

    // ---------------------------------------------------------------- 鼠标档案

    public static string EncodeMouseHeader(MouseProfile profile)
    {
        int flags = (profile.BuiltIn ? 1 : 0) | (profile.Legacy ? 2 : 0) | (profile.Pending ? 4 : 0);
        return profile.Id + "|" + EncodeText(profile.Alias) + "|"
            + EncodeText(profile.FriendlyName) + "|"
            + EncodeText(profile.DevicePath) + "|"
            + EncodeText(profile.HardwareKey) + "|"
            + EncodeText(profile.InstanceKey) + "|"
            + flags.ToString(Invariant) + "|"
            + profile.RatedLeft.ToString(Invariant) + "|"
            + profile.RatedRight.ToString(Invariant) + "|"
            + TimeText(profile.Since) + "|"
            + (profile.Seeded ? "1" : "0") + "|"
            + profile.LastSeenUtcTicks.ToString(Invariant);
    }

    public static MouseProfile? DecodeMouseHeader(string? text)
    {
        string[] parts = (text ?? "").Split('|');
        if (parts.Length < 12)
        {
            return null;
        }

        string id = parts[0] ?? "";
        if (id.Length == 0)
        {
            return null;
        }

        MouseProfile profile = new()
        {
            Id = id,
            Alias = DecodeStoredName(parts[1]),
            FriendlyName = DecodeStoredName(parts[2]),
            DevicePath = DecodeStoredName(parts[3]),
            HardwareKey = DecodeStoredName(parts[4]),
            InstanceKey = DecodeStoredName(parts[5])
        };

        int flags = (int)ParseLong(parts[6]);
        profile.BuiltIn = (flags & 1) != 0;
        profile.Legacy = (flags & 2) != 0;
        profile.Pending = (flags & 4) != 0;
        profile.RatedLeft = ClampRated(ParseLong(parts[7]), DefaultMouseRated);
        profile.RatedRight = ClampRated(ParseLong(parts[8]), DefaultMouseRated);
        profile.Since = ParseTimeText(parts[9]);
        profile.Seeded = parts[10] == "1";
        profile.LastSeenUtcTicks = ParseLong(parts[11]);
        return profile;
    }

    /// <summary>mouse_buttons_v1：id|left,right,middle,x。</summary>
    public static string EncodeMouseButtons(MouseProfile profile)
    {
        return profile.Id + "|" + EncodeMouseButtonPayload(profile);
    }

    public static string EncodeMouseButtonPayload(MouseProfile profile)
    {
        return profile.Left.ToString(Invariant) + ","
            + profile.Right.ToString(Invariant) + ","
            + profile.Middle.ToString(Invariant) + ","
            + profile.X.ToString(Invariant);
    }

    public static void DecodeMouseButtons(MouseProfile profile, string? text)
    {
        ApplyMouseButtons(profile, (text ?? "").Split(','));
    }

    /// <summary>旧版 wear_mouse_v1 用竖线分隔按钮，而 mouse_buttons_v1 用逗号。</summary>
    public static void DecodeLegacyMouseButtons(MouseProfile profile, string? text)
    {
        ApplyMouseButtons(profile, (text ?? "").Split('|'));
    }

    private static void ApplyMouseButtons(MouseProfile profile, string[] parts)
    {
        profile.Left = 0;
        profile.Right = 0;
        profile.Middle = 0;
        profile.X = 0;
        if (parts.Length > 0)
        {
            profile.Left = Math.Max(0, ParseLong(parts[0]));
        }

        if (parts.Length > 1)
        {
            profile.Right = Math.Max(0, ParseLong(parts[1]));
        }

        if (parts.Length > 2)
        {
            profile.Middle = Math.Max(0, ParseLong(parts[2]));
        }

        if (parts.Length > 3)
        {
            profile.X = Math.Max(0, ParseLong(parts[3]));
        }
    }

    /// <summary>mouse_days_v1：id|stamp:l,r,m,x;…（无记录写 "-"）。</summary>
    public static string EncodeMouseDays(MouseProfile profile)
    {
        return profile.Id + "|" + EncodeMouseDaysPayload(profile);
    }

    public static string EncodeMouseDaysPayload(MouseProfile profile)
    {
        StringBuilder sb = new();
        bool any = false;
        for (int i = 0; i < profile.DayStamp.Length; i++)
        {
            if (profile.DayStamp[i] <= 0)
            {
                continue;
            }

            if (any)
            {
                sb.Append(';');
            }

            sb.Append(profile.DayStamp[i].ToString(Invariant)).Append(':')
                .Append(profile.DayLeft[i].ToString(Invariant)).Append(',')
                .Append(profile.DayRight[i].ToString(Invariant)).Append(',')
                .Append(profile.DayMiddle[i].ToString(Invariant)).Append(',')
                .Append(profile.DayX[i].ToString(Invariant));
            any = true;
        }

        return any ? sb.ToString() : "-";
    }

    public static void DecodeMouseDays(MouseProfile profile, string? text)
    {
        for (int i = 0; i < profile.DayStamp.Length; i++)
        {
            profile.DayStamp[i] = 0;
            profile.DayLeft[i] = 0;
            profile.DayRight[i] = 0;
            profile.DayMiddle[i] = 0;
            profile.DayX[i] = 0;
        }

        if (string.IsNullOrEmpty(text) || text == "-")
        {
            return;
        }

        string[] parts = text.Split(';');
        int valid = 0;
        foreach (string part in parts)
        {
            int colon = part.IndexOf(':');
            if (colon > 0 && ParseLong(part[..colon]) > 0)
            {
                valid++;
            }
        }

        int slot = Math.Max(0, MouseProfile.DaySlots - valid);
        for (int i = 0; i < parts.Length && slot < MouseProfile.DaySlots; i++)
        {
            int colon = parts[i].IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            long stamp = ParseLong(parts[i][..colon]);
            if (stamp <= 0)
            {
                continue;
            }

            string[] values = parts[i][(colon + 1)..].Split(',');
            if (values.Length < 4)
            {
                continue;
            }

            profile.DayStamp[slot] = stamp;
            profile.DayLeft[slot] = Math.Max(0, ParseLong(values[0]));
            profile.DayRight[slot] = Math.Max(0, ParseLong(values[1]));
            profile.DayMiddle[slot] = Math.Max(0, ParseLong(values[2]));
            profile.DayX[slot] = Math.Max(0, ParseLong(values[3]));
            slot++;
        }
    }

    // ---------------------------------------------------------------- 档案公共字段

    public static long ClampRated(long value, long fallback)
    {
        if (value <= 0)
        {
            return fallback;
        }

        return value > MaxRated ? MaxRated : value;
    }

    public static string TimeText(DateTime value)
    {
        return value == DateTime.MinValue ? "0" : value.Ticks.ToString(Invariant);
    }

    public static DateTime ParseTimeText(string? text)
    {
        long ticks = ParseLong(text);
        if (ticks <= 0)
        {
            return DateTime.MinValue;
        }

        try
        {
            return new DateTime(ticks);
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTime.MinValue;
        }
    }

    // ---------------------------------------------------------------- 选项归一化

    public static int ValidateTheme(int id) => id >= 0 && id < ThemeCount ? id : 0;

    public static int ValidateKeyboardLayout(int value) =>
        value >= 0 && value < KeyboardLayoutCount ? value : 0;

    public static int NormalizeRefresh(int seconds)
    {
        if (seconds <= 0)
        {
            return 0;
        }

        return seconds < 2 ? 2 : seconds > 3600 ? 3600 : seconds;
    }

    public static bool ValidDpi(double dpi) => !double.IsNaN(dpi) && dpi >= 1 && dpi <= 1_000_000;

    public static int NormalizeIdleThreshold(long seconds)
    {
        if (seconds < MinIdleThresholdSeconds)
        {
            return 60;
        }

        return seconds > MaxIdleThresholdSeconds ? MaxIdleThresholdSeconds : (int)seconds;
    }

    public static int NormalizeBackupKeep(long keep) => keep >= 1 && keep <= 60 ? (int)keep : 7;

    public static int NormalizeKeepDays(long keepDays)
    {
        if (keepDays <= 0)
        {
            return 0;
        }

        return (int)Math.Max(30, Math.Min(3650, keepDays));
    }

    public static string NormalizeShortcutModel(string? text)
    {
        string[] fields = (text ?? "").Split('|');
        if (fields.Length == 3
            && double.TryParse(fields[0], NumberStyles.Float, Invariant, out double menu)
            && double.TryParse(fields[1], NumberStyles.Float, Invariant, out double shortcut)
            && int.TryParse(fields[2], NumberStyles.Integer, Invariant, out int clicks)
            && menu >= 0 && menu <= 60
            && shortcut >= 0 && shortcut <= 60
            && clicks >= 0 && clicks <= 20)
        {
            return FormatDouble(menu) + "|" + FormatDouble(shortcut) + "|" + clicks.ToString(Invariant);
        }

        return "2|0.5|2";
    }

    public static string NormalizeMotion(string? text)
    {
        string[] fields = (text ?? "").Split('|');
        int level = fields.Length > 0 ? (int)ParseLong(fields[0]) : MotionLevelFull;
        if (level < MotionLevelOff || level > MotionLevelFull)
        {
            level = MotionLevelFull;
        }

        bool followSystem = fields.Length < 2 || ParseLong(fields[1]) != 0;
        bool globalHotkey = fields.Length >= 3 && ParseLong(fields[2]) != 0;
        return level.ToString(Invariant) + "|" + (followSystem ? "1" : "0") + "|"
            + (globalHotkey ? "1" : "0");
    }

    public static string NormalizeWindowLayout(string? text)
    {
        string[] fields = (text ?? "").Split('|');
        int width = fields.Length > 0 ? (int)ParseLong(fields[0]) : 0;
        int height = fields.Length > 1 ? (int)ParseLong(fields[1]) : 0;
        bool maximized = fields.Length >= 3 && ParseLong(fields[2]) != 0;
        return (width > 0 ? width : 0).ToString(Invariant) + "|"
            + (height > 0 ? height : 0).ToString(Invariant) + "|"
            + (maximized ? "1" : "0");
    }

    /// <summary>合法返回 device|x|y|w|h，非法返回 null（v3 只在宽高为正时接管）。</summary>
    public static string? NormalizeClickScreen(string? text)
    {
        string[] fields = (text ?? "").Split('|');
        if (fields.Length < 5)
        {
            return null;
        }

        int x = (int)ParseLong(fields[1]);
        int y = (int)ParseLong(fields[2]);
        int width = (int)ParseLong(fields[3]);
        int height = (int)ParseLong(fields[4]);
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        return (fields[0] ?? "") + "|" + x.ToString(Invariant) + "|" + y.ToString(Invariant)
            + "|" + width.ToString(Invariant) + "|" + height.ToString(Invariant);
    }

    public static string NormalizeDailyReport(string? text)
    {
        return DateTime.TryParseExact(text ?? "", "yyyy-MM-dd", Invariant, DateTimeStyles.None, out DateTime day)
            ? day.ToString("yyyy-MM-dd", Invariant)
            : "";
    }
}
