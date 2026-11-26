// ============================================================================
//  v3 Oracle：把 v13 文本解析结果导出成可 diff 的规范化文本。
//  该文件由 tests/ 目录编译，因此 test.bat 会生成 InternalsVisibleTo("OracleDump")。
//  用法：OracleDump.exe <v13.txt> <out.dump>
//  注意：只能用 C# 5 语法（.NET Framework 自带 csc）。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using KeyMouseStats;

internal static class OracleDump
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("用法: OracleDump.exe <v13.txt> <out.dump>");
            return 2;
        }

        try
        {
            string text = File.ReadAllText(args[0], Encoding.UTF8);
            KeyMouseStats.Store.Parsed parsed = KeyMouseStats.Store.ParseText(text);
            File.WriteAllText(args[1], Dump(parsed), new UTF8Encoding(false));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static string Dump(KeyMouseStats.Store.Parsed parsed)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("format=").Append(parsed.FormatVersion).Append('\n');
        sb.Append("history=").Append(parsed.History.Count).Append('\n');
        sb.Append("rules=").Append(parsed.AppRules.Count).Append('\n');
        sb.Append("archives=").Append(parsed.Archives.Count).Append('\n');

        DumpTotals(sb, parsed.Total);

        foreach (KeyValuePair<DateTime, DayRecord> pair in parsed.History.OrderBy(GetDateKey))
        {
            DumpDay(sb, pair.Key, pair.Value);
        }

        foreach (KeyValuePair<string, MonthArchive> pair in parsed.Archives.OrderBy(GetArchiveKey, StringComparer.Ordinal))
        {
            DumpArchive(sb, pair.Key, pair.Value);
        }

        sb.Append("setting.idle_threshold=").Append(parsed.IdleThresholdSeconds).Append('\n');
        sb.Append("setting.keep_days=").Append(parsed.KeepDays).Append('\n');
        sb.Append("setting.theme=").Append(parsed.ThemeId).Append('\n');
        sb.Append("setting.keyboard_layout=").Append(parsed.KeyboardLayout).Append('\n');
        sb.Append("setting.mouse_dpi=").Append(R(parsed.MouseDpi)).Append('\n');
        sb.Append("setting.all_time_peak_apm=").Append(parsed.AllTimePeakApm).Append('\n');

        return sb.ToString();
    }

    private static DateTime GetDateKey(KeyValuePair<DateTime, DayRecord> pair)
    {
        return pair.Key;
    }

    private static string GetArchiveKey(KeyValuePair<string, MonthArchive> pair)
    {
        return pair.Key;
    }

    private static void DumpTotals(StringBuilder sb, KeyMouseStats.Counters t)
    {
        sb.Append("total.keys=").Append(t.Keys).Append('\n');
        sb.Append("total.clicks=").Append(t.Clicks).Append('\n');
        sb.Append("total.left=").Append(t.Left).Append('\n');
        sb.Append("total.right=").Append(t.Right).Append('\n');
        sb.Append("total.middle=").Append(t.Middle).Append('\n');
        sb.Append("total.xbuttons=").Append(t.XButtons).Append('\n');
        sb.Append("total.wheel=").Append(t.Wheel).Append('\n');
        sb.Append("total.hwheel=").Append(t.HWheel).Append('\n');
        sb.Append("total.move=").Append(R(t.MovePx)).Append('\n');
        sb.Append("total.move_m=").Append(R(t.MoveMeters)).Append('\n');
        sb.Append("total.hold_count=").Append(t.HoldCount).Append('\n');
        sb.Append("total.hold_ms=").Append(R(t.HoldTotalMs)).Append('\n');
        sb.Append("total.hold_max=").Append(R(t.HoldMaxMs)).Append('\n');
        sb.Append("total.hold_active=").Append(R(t.HoldActiveSeconds)).Append('\n');
        sb.Append("total.hold_discarded=").Append(t.HoldDiscarded).Append('\n');
    }

    private static void DumpDay(StringBuilder sb, DateTime date, DayRecord d)
    {
        string day = date.ToString("yyyy-MM-dd", Inv);
        sb.Append("day=").Append(day).Append('\n');
        sb.Append("  keys=").Append(d.Keys).Append('\n');
        sb.Append("  clicks=").Append(d.Clicks).Append('\n');
        sb.Append("  left=").Append(d.Left).Append('\n');
        sb.Append("  right=").Append(d.Right).Append('\n');
        sb.Append("  middle=").Append(d.Middle).Append('\n');
        sb.Append("  xbuttons=").Append(d.XButtons).Append('\n');
        sb.Append("  wheel=").Append(d.Wheel).Append('\n');
        sb.Append("  hwheel=").Append(d.HWheel).Append('\n');
        sb.Append("  move=").Append(R(d.MovePx)).Append('\n');
        sb.Append("  move_m=").Append(R(d.MoveMeters)).Append('\n');
        sb.Append("  active=").Append(R(d.ActiveSeconds)).Append('\n');
        sb.Append("  idle=").Append(R(d.IdleSeconds)).Append('\n');
        sb.Append("  app_observed=").Append(R(d.AppObservedSeconds)).Append('\n');
        sb.Append("  app_switches=").Append(d.AppSwitches).Append('\n');
        sb.Append("  peak_apm=").Append(d.PeakApm).Append('\n');
        sb.Append("  hold_count=").Append(d.HoldCount).Append('\n');
        sb.Append("  hold_ms=").Append(R(d.HoldTotalMs)).Append('\n');
        sb.Append("  hold_max=").Append(R(d.HoldMaxMs)).Append('\n');
        sb.Append("  hold_active=").Append(R(d.HoldActiveSeconds)).Append('\n');
        sb.Append("  hold_discarded=").Append(d.HoldDiscarded).Append('\n');
        sb.Append("  hold_buckets=").Append(JoinLongs(d.HoldBuckets)).Append('\n');

        sb.Append("  hk=").Append(JoinIndexed(d.HourKeys)).Append('\n');
        sb.Append("  hc=").Append(JoinIndexed(d.HourClicks)).Append('\n');
        sb.Append("  active_hours=").Append(JoinDoubles(d.ActiveHours)).Append('\n');
        sb.Append("  kk=").Append(JoinCounts(d.KeyCounts)).Append('\n');
        sb.Append("  physical_keys=").Append(JoinCounts(d.PhysicalKeys)).Append('\n');
        sb.Append("  physical_vks=").Append(JoinCounts(d.PhysicalVks)).Append('\n');
        sb.Append("  combo=").Append(JoinCombos(d.ComboCounts)).Append('\n');

        foreach (KeyValuePair<int, long> cell in d.ClickCells.OrderBy(GetCellKey))
        {
            sb.Append("  click=").Append(cell.Key).Append(':').Append(cell.Value).Append('\n');
        }

        foreach (KeyValuePair<long, long> cell in d.ClickCellsV1.OrderBy(GetCellV1Key))
        {
            sb.Append("  clickv1=").Append(cell.Key).Append(':').Append(cell.Value).Append('\n');
        }

        foreach (KeyValuePair<string, AppUsage> app in d.Apps.OrderBy(GetAppKey, StringComparer.Ordinal))
        {
            sb.Append("  app=")
              .Append(B64(app.Value.ProcessPath)).Append('|')
              .Append(B64(app.Value.Title)).Append('|')
              .Append(app.Value.Keys).Append('|')
              .Append(app.Value.Clicks).Append('|')
              .Append(app.Value.Wheel).Append('|')
              .Append(R(app.Value.ActiveSeconds)).Append('|')
              .Append(app.Value.HasKeyGroups ? "1" : "0").Append('|')
              .Append(string.Join(",", app.Value.KeyGroups.Select(FormatLong).ToArray()))
              .Append('\n');
        }

        foreach (ActiveSession session in d.Sessions)
        {
            DumpSession(sb, "  session", session);
        }

        foreach (ActiveSession session in d.IdlePeriods)
        {
            DumpSession(sb, "  idle_period", session);
        }

        sb.Append("  cross_hours=").Append(JoinDoubles(d.Cross.ObservedHours)).Append('\n');
        foreach (KeyValuePair<string, double[]> hours in d.Cross.AppHours.OrderBy(GetHoursKey, StringComparer.Ordinal))
        {
            sb.Append("  app_hours=").Append(B64(hours.Key)).Append('|').Append(JoinDoubles(hours.Value)).Append('\n');
        }

        sb.Append("  cross_scalar=")
          .Append(JoinDoubles(d.Cross.Direction)).Append('|')
          .Append(R(d.Cross.PathCounts)).Append('|')
          .Append(R(d.Cross.DragCounts)).Append('|')
          .Append(d.Cross.Packets).Append('|')
          .Append(d.Cross.CalibratedPackets).Append('|')
          .Append(d.Cross.Flicks).Append('|')
          .Append(d.Cross.Reversals).Append('|')
          .Append(d.Cross.Clicks).Append('|')
          .Append(d.Cross.MoveClicks).Append('\n');

        foreach (KeyValuePair<int, KeyHold> hold in d.Holds.OrderBy(GetHoldKey))
        {
            sb.Append("  hold=").Append(hold.Key).Append(':')
              .Append(hold.Value.Count).Append(':')
              .Append(R(hold.Value.TotalMs)).Append(':')
              .Append(R(hold.Value.MaxMs)).Append('\n');
        }
    }

    private static int GetCellKey(KeyValuePair<int, long> pair) { return pair.Key; }
    private static long GetCellV1Key(KeyValuePair<long, long> pair) { return pair.Key; }
    private static string GetAppKey(KeyValuePair<string, AppUsage> pair) { return pair.Key; }
    private static string GetHoursKey(KeyValuePair<string, double[]> pair) { return pair.Key; }
    private static int GetHoldKey(KeyValuePair<int, KeyHold> pair) { return pair.Key; }

    private static string FormatLong(long value)
    {
        return value.ToString(Inv);
    }

    private static void DumpSession(StringBuilder sb, string prefix, ActiveSession session)
    {
        sb.Append(prefix).Append('=')
          .Append(session.Start.ToString("O", Inv)).Append('|')
          .Append(session.End.ToString("O", Inv)).Append('|')
          .Append(R(session.Seconds)).Append('|')
          .Append((int)session.StartReason).Append('|')
          .Append(session.RateMeasured ? "1" : "0").Append('|')
          .Append(session.PeakApm).Append('|')
          .Append(R(session.PeakOffsetSeconds)).Append('|')
          .Append(session.LastApm).Append('|')
          .Append(R(session.CrossObservedSeconds)).Append('\n');
    }

    private static void DumpArchive(StringBuilder sb, string key, MonthArchive a)
    {
        sb.Append("archive=").Append(key).Append('|')
          .Append(a.Days).Append('|').Append(a.Keys).Append('|').Append(a.Clicks).Append('|')
          .Append(a.Wheel).Append('|').Append(a.Combos).Append('|')
          .Append(R(a.ActiveSeconds)).Append('|').Append(R(a.MoveMeters)).Append('|')
          .Append(a.Sessions).Append('|').Append(a.HoldCount).Append('|')
          .Append(R(a.HoldTotalMs)).Append('|').Append(R(a.HoldMaxMs)).Append('|')
          .Append(R(a.HoldActiveSeconds)).Append('|').Append(a.HoldDiscarded).Append('|');
        foreach (KeyValuePair<string, long> app in a.AppKeys.OrderBy(GetArchiveAppKey, StringComparer.Ordinal))
        {
            sb.Append(B64(app.Key)).Append(':').Append(app.Value).Append(',');
        }
        sb.Append('\n');
    }

    private static string GetArchiveAppKey(KeyValuePair<string, long> pair) { return pair.Key; }

    private static string JoinIndexed(long[] values)
    {
        List<string> parts = new List<string>();
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] != 0) parts.Add(i.ToString(Inv) + ":" + values[i].ToString(Inv));
        }
        return parts.Count == 0 ? "-" : string.Join(",", parts.ToArray());
    }

    private static string JoinLongs(long[] values)
    {
        List<string> parts = new List<string>();
        for (int i = 0; i < values.Length; i++) parts.Add(values[i].ToString(Inv));
        return string.Join(",", parts.ToArray());
    }

    private static string JoinCounts(Dictionary<int, long> values)
    {
        if (values.Count == 0) return "-";
        return string.Join(",", values.OrderBy(GetCountKey).Select(FormatCount).ToArray());
    }

    private static int GetCountKey(KeyValuePair<int, long> pair) { return pair.Key; }

    private static string FormatCount(KeyValuePair<int, long> pair)
    {
        return pair.Key.ToString(Inv) + ":" + pair.Value.ToString(Inv);
    }

    private static string JoinCombos(Dictionary<string, long> values)
    {
        if (values.Count == 0) return "-";
        return string.Join(",", values.OrderBy(GetComboKey, StringComparer.Ordinal).Select(FormatCombo).ToArray());
    }

    private static string GetComboKey(KeyValuePair<string, long> pair) { return pair.Key; }

    private static string FormatCombo(KeyValuePair<string, long> pair)
    {
        return pair.Key + ":" + pair.Value.ToString(Inv);
    }

    private static string JoinDoubles(double[] values)
    {
        List<string> parts = new List<string>();
        for (int i = 0; i < values.Length; i++) parts.Add(R(values[i]));
        return string.Join(",", parts.ToArray());
    }

    private static string R(double value) { return value.ToString("G17", Inv); }

    private static string B64(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value == null ? "" : value));
    }
}
