using System.Globalization;
using System.Text;
using Kms.Core;
using Kms.Core.Model;

namespace Kms.Storage.Text;

/// <summary>
/// v13 单文件文本后端的编解码。解析语义与 v3 <c>Store.ParseText</c> 对齐，
/// 保存顺序与 v3 <c>Store.Serialize / AppendDay / AppendTotals</c> 对齐。
/// </summary>
public static class TextStatsCodec
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static StatsData Load(string text)
    {
        StatsData data = new();
        DayRecord? current = null;
        DayRecord? legacyToday = null;
        string storedDate = "";
        string? hardware = null;
        string? wearKeys = null;
        string? wearMouse = null;
        bool hasWearKeys = false;
        bool hasWearMouse = false;
        List<string> keyboardHeaders = [];
        List<string> keyboardKeyLines = [];
        List<string> mouseHeaders = [];
        List<string> mouseButtonLines = [];
        List<string> mouseDayLines = [];

        string normalized = (text ?? "").Replace("\r\n", "\n").Replace("\r", "\n");
        foreach (string raw in normalized.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
            {
                continue;
            }

            if (line.StartsWith("[day=", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                string stamp = line.Substring(5, line.Length - 6).Trim();
                if (DateTime.TryParseExact(stamp, "yyyy-MM-dd", Invariant, DateTimeStyles.None, out DateTime day))
                {
                    string key = day.ToString("yyyy-MM-dd", Invariant);
                    // 重复 [day=] 以第一节为准；与 v3 一样，后续重复节整体忽略。
                    if (data.History.ContainsKey(key))
                    {
                        current = null;
                        continue;
                    }

                    DayRecord record = new() { Date = day };
                    data.History[key] = record;
                    current = record;
                }

                continue;
            }

            int equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            string key2 = line[..equals].Trim();
            string value = line[(equals + 1)..].Trim();

            if (current != null)
            {
                ParseDayLine(current, key2, value);
                continue;
            }

            bool known = true;
            switch (key2)
            {
                case "format":
                    data.FormatVersion = (int)V13Codecs.ParseLong(value);
                    break;
                case "storage_layout":
                    data.Settings[key2] = value;
                    break;
                case "shortcut_model_v1":
                    data.Settings[key2] = V13Codecs.NormalizeShortcutModel(value);
                    break;
                case "idle_threshold":
                    data.Settings[key2] = V13Codecs.NormalizeIdleThreshold(V13Codecs.ParseLong(value))
                        .ToString(Invariant);
                    break;
                case "backup_keep":
                    data.Settings[key2] = V13Codecs.NormalizeBackupKeep(V13Codecs.ParseLong(value))
                        .ToString(Invariant);
                    break;
                case "wellbeing_v1":
                    data.Settings[key2] = value;
                    break;
                case "hardware_v1":
                    hardware = value;
                    data.Settings[key2] = value;
                    break;
                case "wear_keys_v1":
                    wearKeys = value;
                    hasWearKeys = true;
                    data.Settings[key2] = value;
                    break;
                case "wear_mouse_v1":
                    wearMouse = value;
                    hasWearMouse = true;
                    data.Settings[key2] = value;
                    break;
                case "keyboard_v1":
                    keyboardHeaders.Add(value);
                    break;
                case "keyboard_keys_v1":
                    keyboardKeyLines.Add(value);
                    break;
                case "mouse_v1":
                    mouseHeaders.Add(value);
                    break;
                case "mouse_buttons_v1":
                    mouseButtonLines.Add(value);
                    break;
                case "mouse_days_v1":
                    mouseDayLines.Add(value);
                    break;
                case "keep_days":
                    data.Settings[key2] = V13Codecs.NormalizeKeepDays(V13Codecs.ParseLong(value))
                        .ToString(Invariant);
                    break;
                case "motion_v1":
                    data.Settings[key2] = V13Codecs.NormalizeMotion(value);
                    break;
                case "text_refresh_v1":
                    data.Settings[key2] = V13Codecs.NormalizeRefresh((int)V13Codecs.ParseLong(value))
                        .ToString(Invariant);
                    break;
                case "window_layout_v1":
                    data.Settings[key2] = V13Codecs.NormalizeWindowLayout(value);
                    break;
                case "click_screen_v1":
                    // v3 只在宽高为正时接管选屏；非法值回落成空，保存时也写空。
                    data.Settings[key2] = V13Codecs.NormalizeClickScreen(value) ?? "";
                    break;
                case "daily_report_v1":
                    data.Settings[key2] = V13Codecs.NormalizeDailyReport(value);
                    break;
                case "archive_v1":
                    if (V13Codecs.TryDecodeArchive(value, out MonthArchive? archive) && archive != null)
                    {
                        data.Archives[archive.Key] = archive;
                    }

                    break;
                case "art_theme":
                    data.Settings[key2] = V13Codecs.ValidateTheme((int)V13Codecs.ParseLong(value))
                        .ToString(Invariant);
                    break;
                case "keyboard_layout":
                    data.Settings[key2] = V13Codecs.ValidateKeyboardLayout((int)V13Codecs.ParseLong(value))
                        .ToString(Invariant);
                    break;
                case "all_time_peak_apm":
                    data.Settings[key2] = Math.Max(0, V13Codecs.ParseLong(value)).ToString(Invariant);
                    break;
                case "mouse_dpi":
                    double dpi = V13Codecs.ParseDouble(value);
                    data.Settings[key2] = V13Codecs.FormatDouble(V13Codecs.ValidDpi(dpi) ? dpi : 0);
                    break;
                case "app_rule":
                    if (V13Codecs.TryDecodeAppRule(value, out AppRule? rule) && rule != null)
                    {
                        int existing = data.AppRules.FindIndex(
                            item => string.Equals(item.RuleKey, rule.RuleKey, StringComparison.Ordinal));
                        if (existing >= 0)
                        {
                            data.AppRules[existing] = rule;
                        }
                        else
                        {
                            data.AppRules.Add(rule);
                        }
                    }

                    break;
                case "date":
                    storedDate = value;
                    data.Settings[key2] = value;
                    break;
                case "pos_x":
                    data.Settings[key2] = ((int)V13Codecs.ParseLong(value)).ToString(Invariant);
                    break;
                case "pos_y":
                    data.Settings[key2] = ((int)V13Codecs.ParseLong(value)).ToString(Invariant);
                    break;
                case "total_keys":
                    data.Totals.Keys = V13Codecs.ParseLong(value);
                    break;
                case "total_clicks":
                    data.Totals.Clicks = V13Codecs.ParseLong(value);
                    break;
                case "total_left":
                    data.Totals.Left = V13Codecs.ParseLong(value);
                    break;
                case "total_right":
                    data.Totals.Right = V13Codecs.ParseLong(value);
                    break;
                case "total_middle":
                    data.Totals.Middle = V13Codecs.ParseLong(value);
                    break;
                case "total_xbuttons":
                    data.Totals.XButtons = V13Codecs.ParseLong(value);
                    break;
                case "total_wheel":
                    data.Totals.Wheel = V13Codecs.ParseLong(value);
                    break;
                case "total_hwheel":
                    data.Totals.HWheel = V13Codecs.ParseLong(value);
                    break;
                case "total_move":
                    data.Totals.MovePx = V13Codecs.ParseDouble(value);
                    break;
                case "total_move_m":
                    data.Totals.MoveMeters = V13Codecs.NonNegative(V13Codecs.ParseDouble(value));
                    break;
                case "total_hold_count":
                    data.Totals.HoldCount = Math.Max(0, V13Codecs.ParseLong(value));
                    break;
                case "total_hold_ms":
                    data.Totals.HoldTotalMs = V13Codecs.NonNegative(V13Codecs.ParseDouble(value));
                    break;
                case "total_hold_max":
                    data.Totals.HoldMaxMs = V13Codecs.NonNegative(V13Codecs.ParseDouble(value));
                    break;
                case "total_hold_active":
                    data.Totals.HoldActiveSeconds = V13Codecs.NonNegative(V13Codecs.ParseDouble(value));
                    break;
                case "total_hold_discarded":
                    data.Totals.HoldDiscarded = Math.Max(0, V13Codecs.ParseLong(value));
                    break;
                case "today_keys":
                    legacyToday ??= new DayRecord();
                    legacyToday.Keys = V13Codecs.ParseLong(value);
                    break;
                case "today_clicks":
                    legacyToday ??= new DayRecord();
                    legacyToday.Clicks = V13Codecs.ParseLong(value);
                    break;
                case "today_left":
                    legacyToday ??= new DayRecord();
                    legacyToday.Left = V13Codecs.ParseLong(value);
                    break;
                case "today_right":
                    legacyToday ??= new DayRecord();
                    legacyToday.Right = V13Codecs.ParseLong(value);
                    break;
                case "today_middle":
                    legacyToday ??= new DayRecord();
                    legacyToday.Middle = V13Codecs.ParseLong(value);
                    break;
                case "today_xbuttons":
                    legacyToday ??= new DayRecord();
                    legacyToday.XButtons = V13Codecs.ParseLong(value);
                    break;
                case "today_wheel":
                    legacyToday ??= new DayRecord();
                    legacyToday.Wheel = V13Codecs.ParseLong(value);
                    break;
                case "today_hwheel":
                    legacyToday ??= new DayRecord();
                    legacyToday.HWheel = V13Codecs.ParseLong(value);
                    break;
                case "today_move":
                    legacyToday ??= new DayRecord();
                    legacyToday.MovePx = V13Codecs.ParseDouble(value);
                    break;
                default:
                    known = false;
                    break;
            }

            if (known)
            {
                data.GlobalLines.Add(new KeyValuePair<string, string>(key2, value));
            }
            else
            {
                data.UnknownGlobalLines.Add(line);
            }
        }

        LoadKeyboardProfiles(data, keyboardHeaders, keyboardKeyLines);
        LoadMouseProfiles(data, mouseHeaders, mouseButtonLines, mouseDayLines);

        // 旧版只有 wear_* 行时，v3 会合成一条 legacy 档案；多设备档案优先。
        if (keyboardHeaders.Count == 0
            && hasWearKeys
            && !string.IsNullOrEmpty(wearKeys)
            && wearKeys != "-")
        {
            data.Keyboards.Add(CreateLegacyKeyboard(hardware, wearKeys));
        }

        if (mouseHeaders.Count == 0 && hasWearMouse)
        {
            string mouseName = HardwareMouseName(hardware);
            if (wearMouse != null
                && (wearMouse != "0|0|0|0"
                    || (!string.IsNullOrEmpty(mouseName) && mouseName != "主力鼠标")))
            {
                data.Mice.Add(CreateLegacyMouse(hardware, wearMouse));
            }
        }

        MergeLegacyToday(data, legacyToday, storedDate);
        return data;
    }

    public static string Save(StatsData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        StringBuilder sb = new();
        sb.AppendLine("# 键鼠统计数据文件 v13 / 正式版 " + BuildInfo.Version);
        sb.AppendLine("format=13");
        sb.AppendLine("storage_layout=monthly-v1");
        sb.AppendLine("shortcut_model_v1=" + Setting(data, "shortcut_model_v1", "2|0.5|2"));
        sb.AppendLine("idle_threshold=" + Setting(data, "idle_threshold", "60"));
        sb.AppendLine("backup_keep=" + Setting(data, "backup_keep", "7"));
        sb.AppendLine("wellbeing_v1=" + Setting(data, "wellbeing_v1", ""));
        sb.AppendLine("hardware_v1=" + Setting(data, "hardware_v1", HardwareText(data)));
        sb.AppendLine("wear_keys_v1=" + Setting(data, "wear_keys_v1", WearKeysText(data)));
        sb.AppendLine("wear_mouse_v1=" + Setting(data, "wear_mouse_v1", WearMouseText(data)));
        AppendKeyboards(sb, data);
        AppendMice(sb, data);
        sb.AppendLine("keep_days=" + Setting(data, "keep_days", "365"));
        sb.AppendLine("motion_v1=" + Setting(data, "motion_v1", "2|1|0"));
        sb.AppendLine("text_refresh_v1=" + Setting(data, "text_refresh_v1", "15"));
        sb.AppendLine("window_layout_v1=" + Setting(data, "window_layout_v1", "0|0|0"));
        sb.AppendLine("click_screen_v1=" + Setting(data, "click_screen_v1", ""));
        sb.AppendLine("daily_report_v1=" + Setting(data, "daily_report_v1", ""));
        foreach (MonthArchive archive in data.Archives.Values)
        {
            sb.AppendLine("archive_v1=" + V13Codecs.EncodeArchive(archive));
        }

        sb.AppendLine("art_theme=" + Setting(data, "art_theme", "0"));
        sb.AppendLine("keyboard_layout=" + Setting(data, "keyboard_layout", "0"));
        sb.AppendLine("all_time_peak_apm=" + Setting(data, "all_time_peak_apm", "0"));
        sb.AppendLine("mouse_dpi=" + Setting(data, "mouse_dpi", "0"));
        foreach (AppRule rule in data.AppRules)
        {
            sb.AppendLine("app_rule=" + V13Codecs.EncodeAppRule(rule));
        }

        sb.AppendLine("date=" + DateTime.Today.ToString("yyyy-MM-dd", Invariant));
        sb.AppendLine("pos_x=" + Setting(data, "pos_x", "-1"));
        sb.AppendLine("pos_y=" + Setting(data, "pos_y", "-1"));
        AppendTotals(sb, data.Totals);

        // 解析器不认识的全局行原样保留，避免静默丢字段。
        foreach (string line in data.UnknownGlobalLines)
        {
            sb.AppendLine(line);
        }

        List<KeyValuePair<string, DayRecord>> days = new(data.History);
        days.Sort((a, b) =>
        {
            int order = a.Value.Date.CompareTo(b.Value.Date);
            return order != 0 ? order : string.CompareOrdinal(a.Key, b.Key);
        });
        foreach (KeyValuePair<string, DayRecord> pair in days)
        {
            AppendDay(sb, pair.Value.Date, pair.Value);
        }

        return sb.ToString();
    }

    // ---------------------------------------------------------------- 日记录解析

    private static void ParseDayLine(DayRecord cur, string key, string value)
    {
        switch (key)
        {
            case "keys":
                cur.Keys = V13Codecs.ParseLong(value);
                break;
            case "clicks":
                cur.Clicks = V13Codecs.ParseLong(value);
                break;
            case "left":
                cur.Left = V13Codecs.ParseLong(value);
                break;
            case "right":
                cur.Right = V13Codecs.ParseLong(value);
                break;
            case "middle":
                cur.Middle = V13Codecs.ParseLong(value);
                break;
            case "xbuttons":
                cur.XButtons = V13Codecs.ParseLong(value);
                break;
            case "wheel":
                cur.Wheel = V13Codecs.ParseLong(value);
                break;
            case "hwheel":
                cur.HWheel = V13Codecs.ParseLong(value);
                break;
            case "move":
                cur.MovePx = V13Codecs.ParseDouble(value);
                break;
            case "move_m":
                cur.MoveMeters = V13Codecs.NonNegative(V13Codecs.ParseDouble(value));
                break;
            case "hk":
                V13Codecs.DecodeHourBuckets(value, cur.HourKeys);
                break;
            case "hc":
                V13Codecs.DecodeHourBuckets(value, cur.HourClicks);
                break;
            case "ch":
                V13Codecs.DecodeClickCells(value, cur.ClickCells);
                break;
            case "chv1":
                V13Codecs.DecodeClickCellsV1(value, cur.ClickCellsV1);
                break;
            case "kk":
                V13Codecs.DecodeKeyCounts(value, cur.KeyCounts);
                break;
            case "physical_keys":
                V13Codecs.DecodeKeyCounts(value, cur.PhysicalKeys, 768);
                break;
            case "physical_vks":
                V13Codecs.DecodeKeyCounts(value, cur.PhysicalVks);
                break;
            case "combo":
                V13Codecs.DecodeCombo(cur, value);
                break;
            case "app":
                V13Codecs.DecodeApp(cur, value);
                break;
            case "active_seconds":
                cur.ActiveSeconds = V13Codecs.ParseSeconds(value);
                break;
            case "app_observed_seconds":
                cur.AppObservedSeconds = V13Codecs.ParseSeconds(value);
                break;
            case "app_switches":
                cur.AppSwitches = Math.Max(0, V13Codecs.ParseLong(value));
                break;
            case "peak_apm":
                cur.PeakApm = Math.Max(0, V13Codecs.ParseLong(value));
                break;
            case "idle_seconds":
                cur.IdleSeconds = V13Codecs.ParseSeconds(value);
                break;
            case "active_hours":
                V13Codecs.DecodeHours(value, cur.ActiveHours);
                break;
            case "idle_period":
                V13Codecs.DecodeIdlePeriod(cur, value);
                break;
            case "session":
                V13Codecs.DecodeSession(cur, value);
                break;
            case "session_reason_v1":
                V13Codecs.DecodeSessionReason(cur, value);
                break;
            case "cross_hours_v1":
            case "app_hours_v1":
            case "session_coverage_v1":
            case "session_app_v1":
            case "mouse_vector_v1":
                V13Codecs.DecodeCross(cur, key, value);
                break;
            case "hold_v1":
                V13Codecs.DecodeHoldSummary(cur, value);
                break;
            case "hold_key_v1":
                V13Codecs.DecodeHoldKeys(cur, value);
                break;
            case "hold_active_v1":
                V13Codecs.DecodeHoldActive(cur, value);
                break;
            case "app_keys_v1":
                V13Codecs.DecodeAppKey(cur, value);
                break;
        }
    }

    // ---------------------------------------------------------------- 档案装配

    private static void LoadKeyboardProfiles(
        StatsData data,
        List<string> headers,
        List<string> keyLines)
    {
        foreach (string header in headers)
        {
            KeyboardProfile? profile = V13Codecs.DecodeKeyboardHeader(header);
            if (profile == null || FindKeyboard(data.Keyboards, profile.Id) != null)
            {
                continue;
            }

            data.Keyboards.Add(profile);
        }

        foreach (string line in keyLines)
        {
            int bar = line.IndexOf('|');
            if (bar <= 0)
            {
                continue;
            }

            KeyboardProfile? profile = FindKeyboard(data.Keyboards, line[..bar]);
            if (profile != null)
            {
                V13Codecs.DecodeKeyboardKeys(profile, line[(bar + 1)..]);
            }
        }
    }

    private static void LoadMouseProfiles(
        StatsData data,
        List<string> headers,
        List<string> buttonLines,
        List<string> dayLines)
    {
        foreach (string header in headers)
        {
            MouseProfile? profile = V13Codecs.DecodeMouseHeader(header);
            if (profile == null || FindMouse(data.Mice, profile.Id) != null)
            {
                continue;
            }

            data.Mice.Add(profile);
        }

        foreach (string line in buttonLines)
        {
            int bar = line.IndexOf('|');
            if (bar <= 0)
            {
                continue;
            }

            MouseProfile? profile = FindMouse(data.Mice, line[..bar]);
            if (profile != null)
            {
                V13Codecs.DecodeMouseButtons(profile, line[(bar + 1)..]);
            }
        }

        foreach (string line in dayLines)
        {
            int bar = line.IndexOf('|');
            if (bar <= 0)
            {
                continue;
            }

            MouseProfile? profile = FindMouse(data.Mice, line[..bar]);
            if (profile != null)
            {
                V13Codecs.DecodeMouseDays(profile, line[(bar + 1)..]);
            }
        }
    }

    private static KeyboardProfile? FindKeyboard(List<KeyboardProfile> profiles, string id)
    {
        foreach (KeyboardProfile profile in profiles)
        {
            if (string.Equals(profile.Id, id, StringComparison.Ordinal))
            {
                return profile;
            }
        }

        return null;
    }

    private static MouseProfile? FindMouse(List<MouseProfile> profiles, string id)
    {
        foreach (MouseProfile profile in profiles)
        {
            if (string.Equals(profile.Id, id, StringComparison.Ordinal))
            {
                return profile;
            }
        }

        return null;
    }

    private static KeyboardProfile CreateLegacyKeyboard(string? hardware, string? wearKeys)
    {
        KeyboardProfile profile = new()
        {
            Id = V13Codecs.LegacyKeyboardId,
            Legacy = true,
            Rated = V13Codecs.DefaultKeyboardRated
        };

        string[] fields = (hardware ?? "").Split('|');
        if (!string.IsNullOrEmpty(hardware))
        {
            if (fields.Length > 0)
            {
                profile.Alias = V13Codecs.DecodeStoredName(fields[0]);
            }

            if (fields.Length > 1)
            {
                profile.Rated = V13Codecs.ClampRated(V13Codecs.ParseLong(fields[1]),
                    V13Codecs.DefaultKeyboardRated);
            }

            if (fields.Length > 2)
            {
                profile.Since = V13Codecs.ParseTimeText(fields[2]);
            }

            if (fields.Length > 7)
            {
                profile.Seeded = fields[7] == "1";
            }
        }

        if (string.IsNullOrEmpty(profile.Alias) || profile.Alias == "主力键盘")
        {
            profile.Alias = "旧版单键盘";
        }

        V13Codecs.DecodeKeyboardKeys(profile, wearKeys);
        return profile;
    }

    private static MouseProfile CreateLegacyMouse(string? hardware, string? wearMouse)
    {
        MouseProfile profile = new()
        {
            Id = V13Codecs.LegacyMouseId,
            Legacy = true,
            Seeded = true,
            RatedLeft = V13Codecs.DefaultMouseRated,
            RatedRight = V13Codecs.DefaultMouseRated
        };

        string[] fields = (hardware ?? "").Split('|');
        if (fields.Length > 3)
        {
            profile.Alias = V13Codecs.DecodeStoredName(fields[3]);
        }

        if (fields.Length > 4)
        {
            profile.RatedLeft = V13Codecs.ClampRated(V13Codecs.ParseLong(fields[4]),
                V13Codecs.DefaultMouseRated);
        }

        if (fields.Length > 5)
        {
            profile.RatedRight = V13Codecs.ClampRated(V13Codecs.ParseLong(fields[5]),
                V13Codecs.DefaultMouseRated);
        }

        if (fields.Length > 6)
        {
            profile.Since = V13Codecs.ParseTimeText(fields[6]);
        }

        if (string.IsNullOrEmpty(profile.Alias) || profile.Alias == "主力鼠标")
        {
            profile.Alias = "旧版单鼠标";
        }

        V13Codecs.DecodeLegacyMouseButtons(profile, wearMouse);
        return profile;
    }

    private static string HardwareMouseName(string? hardware)
    {
        string[] fields = (hardware ?? "").Split('|');
        return fields.Length > 3 ? V13Codecs.DecodeStoredName(fields[3]) : "";
    }

    private static void MergeLegacyToday(StatsData data, DayRecord? legacy, string storedDate)
    {
        if (legacy == null
            || !DateTime.TryParseExact(storedDate, "yyyy-MM-dd", Invariant, DateTimeStyles.None, out DateTime day))
        {
            return;
        }

        legacy.Date = day;
        string key = day.ToString("yyyy-MM-dd", Invariant);
        if (data.History.TryGetValue(key, out DayRecord? existing))
        {
            existing.Keys += legacy.Keys;
            existing.Clicks += legacy.Clicks;
            existing.Left += legacy.Left;
            existing.Right += legacy.Right;
            existing.Middle += legacy.Middle;
            existing.XButtons += legacy.XButtons;
            existing.Wheel += legacy.Wheel;
            existing.HWheel += legacy.HWheel;
            existing.MovePx += legacy.MovePx;
        }
        else if (!legacy.IsEmpty)
        {
            data.History[key] = legacy;
        }
    }

    // ---------------------------------------------------------------- 保存

    private static void AppendKeyboards(StringBuilder sb, StatsData data)
    {
        foreach (KeyboardProfile profile in data.Keyboards)
        {
            if (profile.Pending || IsKeyboardPlaceholder(profile))
            {
                continue;
            }

            sb.AppendLine("keyboard_v1=" + V13Codecs.EncodeKeyboardHeader(profile));
            sb.AppendLine("keyboard_keys_v1=" + V13Codecs.EncodeKeyboardKeys(profile));
        }
    }

    private static void AppendMice(StringBuilder sb, StatsData data)
    {
        foreach (MouseProfile profile in data.Mice)
        {
            if (profile.Pending || IsMousePlaceholder(profile))
            {
                continue;
            }

            sb.AppendLine("mouse_v1=" + V13Codecs.EncodeMouseHeader(profile));
            sb.AppendLine("mouse_buttons_v1=" + V13Codecs.EncodeMouseButtons(profile));
            sb.AppendLine("mouse_days_v1=" + V13Codecs.EncodeMouseDays(profile));
        }
    }

    private static bool IsKeyboardPlaceholder(KeyboardProfile profile)
    {
        return !profile.Pending
            && !profile.Legacy
            && profile.Id == "primary"
            && string.Equals(profile.Alias ?? "", "主力键盘", StringComparison.Ordinal)
            && string.IsNullOrEmpty(profile.FriendlyName)
            && profile.TotalKeyPresses == 0;
    }

    private static bool IsMousePlaceholder(MouseProfile profile)
    {
        return !profile.Pending
            && !profile.Legacy
            && profile.Id == "mprimary"
            && string.Equals(profile.Alias ?? "", "主力鼠标", StringComparison.Ordinal)
            && string.IsNullOrEmpty(profile.FriendlyName)
            && profile.TotalClicks == 0;
    }

    private static void AppendTotals(StringBuilder sb, StatsTotals total)
    {
        sb.AppendLine("total_keys=" + total.Keys.ToString(Invariant));
        sb.AppendLine("total_clicks=" + total.Clicks.ToString(Invariant));
        sb.AppendLine("total_left=" + total.Left.ToString(Invariant));
        sb.AppendLine("total_right=" + total.Right.ToString(Invariant));
        sb.AppendLine("total_middle=" + total.Middle.ToString(Invariant));
        sb.AppendLine("total_xbuttons=" + total.XButtons.ToString(Invariant));
        sb.AppendLine("total_wheel=" + total.Wheel.ToString(Invariant));
        if (total.HWheel > 0)
        {
            sb.AppendLine("total_hwheel=" + total.HWheel.ToString(Invariant));
        }

        sb.AppendLine("total_move=" + V13Codecs.FormatMove(total.MovePx));
        sb.AppendLine("total_move_m=" + V13Codecs.FormatDouble(total.MoveMeters));
        sb.AppendLine("total_hold_count=" + total.HoldCount.ToString(Invariant));
        sb.AppendLine("total_hold_ms=" + V13Codecs.FormatDouble(total.HoldTotalMs));
        sb.AppendLine("total_hold_max=" + V13Codecs.FormatDouble(total.HoldMaxMs));
        sb.AppendLine("total_hold_active=" + V13Codecs.FormatDouble(total.HoldActiveSeconds));
        sb.AppendLine("total_hold_discarded=" + total.HoldDiscarded.ToString(Invariant));
    }

    private static void AppendDay(StringBuilder sb, DateTime day, DayRecord record)
    {
        sb.AppendLine("[day=" + day.ToString("yyyy-MM-dd", Invariant) + "]");
        sb.AppendLine("keys=" + record.Keys.ToString(Invariant));
        sb.AppendLine("clicks=" + record.Clicks.ToString(Invariant));
        sb.AppendLine("left=" + record.Left.ToString(Invariant));
        sb.AppendLine("right=" + record.Right.ToString(Invariant));
        sb.AppendLine("middle=" + record.Middle.ToString(Invariant));
        sb.AppendLine("xbuttons=" + record.XButtons.ToString(Invariant));
        sb.AppendLine("wheel=" + record.Wheel.ToString(Invariant));
        if (record.HWheel > 0)
        {
            sb.AppendLine("hwheel=" + record.HWheel.ToString(Invariant));
        }

        sb.AppendLine("move=" + V13Codecs.FormatMove(record.MovePx));
        sb.AppendLine("move_m=" + V13Codecs.FormatDouble(record.MoveMeters));
        sb.AppendLine("hk=" + V13Codecs.EncodeHourBuckets(record.HourKeys));
        sb.AppendLine("hc=" + V13Codecs.EncodeHourBuckets(record.HourClicks));
        if (record.ClickCells.Count > 0)
        {
            sb.AppendLine("ch=" + V13Codecs.EncodeClickCells(record.ClickCells));
        }

        if (record.ClickCellsV1.Count > 0)
        {
            sb.AppendLine("chv1=" + V13Codecs.EncodeClickCellsV1(record.ClickCellsV1));
        }

        sb.AppendLine("kk=" + V13Codecs.EncodeKeyCounts(record.KeyCounts));
        sb.AppendLine("physical_keys=" + V13Codecs.EncodeKeyCounts(record.PhysicalKeys));
        sb.AppendLine("physical_vks=" + V13Codecs.EncodeKeyCounts(record.PhysicalVks));
        foreach (KeyValuePair<string, long> combo in record.ComboCounts)
        {
            sb.AppendLine("combo=" + V13Codecs.EncodeCombo(combo.Key, combo.Value));
        }

        foreach (AppUsage app in record.Apps.Values)
        {
            sb.AppendLine("app=" + V13Codecs.EncodeApp(app));
        }

        sb.AppendLine("active_seconds=" + V13Codecs.FormatDouble(record.ActiveSeconds));
        sb.AppendLine("app_observed_seconds=" + V13Codecs.FormatDouble(record.AppObservedSeconds));
        sb.AppendLine("app_switches=" + record.AppSwitches.ToString(Invariant));
        sb.AppendLine("peak_apm=" + record.PeakApm.ToString(Invariant));
        sb.AppendLine("idle_seconds=" + V13Codecs.FormatDouble(record.IdleSeconds));
        sb.AppendLine("active_hours=" + V13Codecs.EncodeHours(record.ActiveHours));
        foreach (ActiveSession session in record.Sessions)
        {
            sb.AppendLine("session=" + V13Codecs.EncodeSession(session));
        }

        foreach (ActiveSession session in record.Sessions)
        {
            if (session.StartReason != SessionStartReason.Legacy)
            {
                sb.AppendLine("session_reason_v1=" + V13Codecs.EncodeSessionReason(session));
            }
        }

        foreach (ActiveSession period in record.IdlePeriods)
        {
            sb.AppendLine("idle_period=" + V13Codecs.EncodeIdlePeriod(period));
        }

        List<string> holdLines = [];
        V13Codecs.AppendHoldLines(record, holdLines);
        foreach (string line in holdLines)
        {
            sb.AppendLine(line);
        }

        foreach (string line in V13Codecs.EncodeAppKeyLines(record))
        {
            sb.AppendLine(line);
        }

        V13Codecs.EncodeCross(record, sb);
    }

    // ---------------------------------------------------------------- 主档案兼容文本

    private static string Setting(StatsData data, string key, string fallback)
    {
        if (data.Settings.TryGetValue(key, out string? value))
        {
            return value ?? fallback;
        }

        for (int i = data.GlobalLines.Count - 1; i >= 0; i--)
        {
            if (string.Equals(data.GlobalLines[i].Key, key, StringComparison.Ordinal))
            {
                return data.GlobalLines[i].Value;
            }
        }

        return fallback;
    }

    private static string HardwareText(StatsData data)
    {
        KeyboardProfile? keyboard = PrimaryKeyboard(data.Keyboards);
        MouseProfile? mouse = PrimaryMouse(data.Mice);
        string keyboardName = keyboard == null ? "" : V13Codecs.EncodeText(keyboard.DisplayName);
        long rated = keyboard?.Rated ?? V13Codecs.DefaultKeyboardRated;
        long since = keyboard == null ? 0 : keyboard.Since.Ticks;
        string mouseName = V13Codecs.EncodeText(mouse?.DisplayName ?? "主力鼠标");
        long mouseRatedLeft = mouse?.RatedLeft ?? V13Codecs.DefaultMouseRated;
        long mouseRatedRight = mouse?.RatedRight ?? V13Codecs.DefaultMouseRated;
        long mouseSince = mouse == null ? 0 : mouse.Since.Ticks;
        string seeded = keyboard != null && keyboard.Seeded ? "1" : "0";
        return keyboardName + "|"
            + rated.ToString(Invariant) + "|"
            + since.ToString(Invariant) + "|"
            + mouseName + "|"
            + mouseRatedLeft.ToString(Invariant) + "|"
            + mouseRatedRight.ToString(Invariant) + "|"
            + mouseSince.ToString(Invariant) + "|"
            + seeded;
    }

    private static string WearKeysText(StatsData data)
    {
        KeyboardProfile? keyboard = PrimaryKeyboard(data.Keyboards);
        return keyboard == null ? "-" : V13Codecs.EncodeKeyboardKeyPayload(keyboard);
    }

    private static string WearMouseText(StatsData data)
    {
        MouseProfile? mouse = PrimaryMouse(data.Mice);
        return mouse == null
            ? "0|0|0|0"
            : mouse.Left.ToString(Invariant) + "|"
                + mouse.Right.ToString(Invariant) + "|"
                + mouse.Middle.ToString(Invariant) + "|"
                + mouse.X.ToString(Invariant);
    }

    private static KeyboardProfile? PrimaryKeyboard(List<KeyboardProfile> profiles)
    {
        KeyboardProfile? firstLegacy = null;
        foreach (KeyboardProfile profile in profiles)
        {
            if (profile.Pending)
            {
                continue;
            }

            if (!profile.Legacy)
            {
                return profile;
            }

            firstLegacy ??= profile;
        }

        return firstLegacy;
    }

    private static MouseProfile? PrimaryMouse(List<MouseProfile> profiles)
    {
        MouseProfile? firstLegacy = null;
        foreach (MouseProfile profile in profiles)
        {
            if (profile.Pending || IsMousePlaceholder(profile))
            {
                continue;
            }

            if (!profile.Legacy)
            {
                return profile;
            }

            firstLegacy ??= profile;
        }

        return firstLegacy;
    }
}
