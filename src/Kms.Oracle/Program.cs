using System.Globalization;
using System.Text;
using Kms.Core.Model;
using Kms.Storage.Migration;
using Kms.Storage.Text;

namespace Kms.Oracle;

/// <summary>
/// 把 v4 的解析结果导出成与 v3 <c>OracleDump.exe</c> 完全相同的规范化文本，
/// 供逐行 diff。用法：Kms.Oracle text &lt;v13.txt&gt; &lt;out.dump&gt;
/// </summary>
public static class Program
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static int Main(string[] args)
    {
        if (args.Length == 3 && string.Equals(args[0], "migrate", StringComparison.OrdinalIgnoreCase))
        {
            MigrationOutcome outcome = V2Migrator.Run(args[1], args[2]);
            Console.WriteLine(outcome.Describe());
            return outcome.Failed ? 1 : 0;
        }

        if (args.Length == 3 && string.Equals(args[0], "save", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                StatsData data = TextStatsCodec.Load(File.ReadAllText(args[1], Encoding.UTF8));
                File.WriteAllText(args[2], TextStatsCodec.Save(data), new UTF8Encoding(false));
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }

        // 把 SQLite（v3 或 v4）读成 StatsData 后导出规范化文本，供同口径 diff。
        if (args.Length == 3 && string.Equals(args[0], "dbtext", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                StatsData data = Kms.Storage.Sqlite.SqliteStatsReader.Read(args[1]);
                File.WriteAllText(args[2], Dump(data), new UTF8Encoding(false));
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }

        if (args.Length != 3 || !string.Equals(args[0], "text", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("用法: Kms.Oracle text <v13.txt> <out.dump> | Kms.Oracle save <v13.txt> <out.txt> | Kms.Oracle dbtext <kmstats.db> <out.dump> | Kms.Oracle migrate <sourceRoot> <dbRoot>");
            return 2;
        }

        try
        {
            string text = File.ReadAllText(args[1], Encoding.UTF8);
            StatsData data = TextStatsCodec.Load(text);
            File.WriteAllText(args[2], Dump(data), new UTF8Encoding(false));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    public static string Dump(StatsData data)
    {
        StringBuilder sb = new();
        sb.Append("format=").Append(data.FormatVersion).Append('\n');
        sb.Append("history=").Append(data.History.Count).Append('\n');
        sb.Append("rules=").Append(data.AppRules.Count).Append('\n');
        sb.Append("archives=").Append(data.Archives.Count).Append('\n');

        DumpTotals(sb, data.Totals);

        foreach ((string key, DayRecord day) in data.History.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            DumpDay(sb, key, day);
        }

        foreach ((string key, MonthArchive archive) in data.Archives.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            DumpArchive(sb, key, archive);
        }

        sb.Append("setting.idle_threshold=").Append(Setting(data, "idle_threshold", "60")).Append('\n');
        sb.Append("setting.keep_days=").Append(Setting(data, "keep_days", "365")).Append('\n');
        sb.Append("setting.theme=").Append(Setting(data, "art_theme", "0")).Append('\n');
        sb.Append("setting.keyboard_layout=").Append(Setting(data, "keyboard_layout", "0")).Append('\n');
        sb.Append("setting.mouse_dpi=").Append(R(ParseDouble(Setting(data, "mouse_dpi", "0")))).Append('\n');
        sb.Append("setting.all_time_peak_apm=").Append(Setting(data, "all_time_peak_apm", "0")).Append('\n');
        return sb.ToString();
    }

    private static string Setting(StatsData data, string key, string fallback)
    {
        string value = fallback;
        foreach ((string lineKey, string lineValue) in data.GlobalLines)
        {
            if (string.Equals(lineKey, key, StringComparison.Ordinal))
            {
                value = lineValue;
            }
        }

        return value;
    }

    private static double ParseDouble(string value) =>
        double.TryParse(value, NumberStyles.Float, Inv, out double parsed) ? parsed : 0;

    private static void DumpTotals(StringBuilder sb, StatsTotals t)
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

    private static void DumpDay(StringBuilder sb, string day, DayRecord d)
    {
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

        foreach ((int cell, long count) in d.ClickCells.OrderBy(pair => pair.Key))
        {
            sb.Append("  click=").Append(cell).Append(':').Append(count).Append('\n');
        }

        foreach ((long cell, long count) in d.ClickCellsV1.OrderBy(pair => pair.Key))
        {
            sb.Append("  clickv1=").Append(cell).Append(':').Append(count).Append('\n');
        }

        foreach ((string id, AppUsage app) in d.Apps.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            _ = id;
            sb.Append("  app=")
              .Append(B64(app.ProcessPath)).Append('|')
              .Append(B64(app.Title)).Append('|')
              .Append(app.Keys).Append('|')
              .Append(app.Clicks).Append('|')
              .Append(app.Wheel).Append('|')
              .Append(R(app.ActiveSeconds)).Append('|')
              .Append(app.HasKeyGroups ? "1" : "0").Append('|')
              .Append(string.Join(",", app.KeyGroups.Select(value => value.ToString(Inv))))
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
        foreach ((string path, double[] hours) in d.Cross.AppHours.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            sb.Append("  app_hours=").Append(B64(path)).Append('|').Append(JoinDoubles(hours)).Append('\n');
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

        foreach ((int vk, KeyHold hold) in d.Holds.OrderBy(pair => pair.Key))
        {
            sb.Append("  hold=").Append(vk).Append(':')
              .Append(hold.Count).Append(':')
              .Append(R(hold.TotalMs)).Append(':')
              .Append(R(hold.MaxMs)).Append('\n');
        }
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

    private static void DumpArchive(StringBuilder sb, string key, MonthArchive archive)
    {
        sb.Append("archive=").Append(key).Append('|')
          .Append(archive.Days).Append('|').Append(archive.Keys).Append('|').Append(archive.Clicks).Append('|')
          .Append(archive.Wheel).Append('|').Append(archive.Combos).Append('|')
          .Append(R(archive.ActiveSeconds)).Append('|').Append(R(archive.MoveMeters)).Append('|')
          .Append(archive.Sessions).Append('|').Append(archive.HoldCount).Append('|')
          .Append(R(archive.HoldTotalMs)).Append('|').Append(R(archive.HoldMaxMs)).Append('|')
          .Append(R(archive.HoldActiveSeconds)).Append('|').Append(archive.HoldDiscarded).Append('|');
        foreach ((string path, long keys) in archive.AppKeys.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            sb.Append(B64(path)).Append(':').Append(keys).Append(',');
        }

        sb.Append('\n');
    }

    private static string JoinIndexed(long[] values)
    {
        List<string> parts = [];
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] != 0)
            {
                parts.Add(i.ToString(Inv) + ":" + values[i].ToString(Inv));
            }
        }

        return parts.Count == 0 ? "-" : string.Join(",", parts);
    }

    private static string JoinLongs(long[] values) =>
        string.Join(",", values.Select(value => value.ToString(Inv)));

    private static string JoinCounts(Dictionary<int, long> values) =>
        values.Count == 0
            ? "-"
            : string.Join(",", values.OrderBy(pair => pair.Key)
                .Select(pair => pair.Key.ToString(Inv) + ":" + pair.Value.ToString(Inv)));

    private static string JoinCombos(Dictionary<string, long> values) =>
        values.Count == 0
            ? "-"
            : string.Join(",", values.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Key + ":" + pair.Value.ToString(Inv)));

    private static string JoinDoubles(double[] values) =>
        string.Join(",", values.Select(R));

    private static string R(double value) => value.ToString("G17", Inv);

    private static string B64(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));
}
