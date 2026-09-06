// ============================================================================
//  键鼠统计桌面小组件  KeyMouseWidget.cs
//  悬浮小组件 + 数据采集(全局钩子)+ 数据持久化(v2:按天历史/小时分布/按键细分)
//
//  编译:双击 build.bat(使用 Windows 自带的 .NET Framework csc,无需安装任何东西)
//  数据保存在:%AppData%\键鼠统计\stats.txt
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace KeyMouseStats
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main()
        {
            AppDomain.CurrentDomain.UnhandledException+=delegate(object sender,UnhandledExceptionEventArgs error)
            { LogFailure("Unhandled",error.ExceptionObject as Exception); };
            try { SetProcessDPIAware(); } catch { }

            bool createdNew;
            Mutex mutex = new Mutex(true, "KeyMouseStats_Widget_SingleInstance", out createdNew);
            if (!createdNew)
            {
                // 已在运行:请求打开详细统计面板
                try
                {
                    using (EventWaitHandle ev = EventWaitHandle.OpenExisting("KeyMouseStats_DashboardEvent"))
                    {
                        ev.Set();
                    }
                }
                catch
                {
                    ThemeMessage.Show("键鼠统计小组件已经在运行了(可查看系统托盘)。",
                        "键鼠统计", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }

            bool eventCreated;
            EventWaitHandle dashEvent = new EventWaitHandle(
                false, EventResetMode.AutoReset, "KeyMouseStats_DashboardEvent", out eventCreated);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            StatsWidget widget = new StatsWidget();
            widget.ListenForDashboard(dashEvent);
            Application.Run(widget);

            GC.KeepAlive(mutex);
            GC.KeepAlive(dashEvent);
        }
        private static readonly object LogGate=new object();
        internal static void LogFailure(string context,Exception error)
        {
            try
            {
                lock(LogGate)
                {
                    string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"KeyMouseStats");
                    Directory.CreateDirectory(folder);string path=Path.Combine(folder,"diagnostics.log");
                    if(File.Exists(path) && new FileInfo(path).Length>1048576)File.Delete(path);
                    File.AppendAllText(path,DateTime.Now.ToString("O")+" "+context+Environment.NewLine+(error==null?"Unknown error":error.ToString())+Environment.NewLine);
                }
            }
            catch { }
        }
    }

    /// <summary>全量累计(仅总数,不含细分)。</summary>
    internal sealed class Counters
    {
        public long Keys;
        public long Clicks;
        public long Left;
        public long Right;
        public long Middle;
        public long XButtons;
        public long Wheel;
        public double MovePx;
        public double MoveMeters;
    }

    /// <summary>单日完整记录(含小时分布与按键细分)。</summary>
    internal sealed class DayRecord
    {
        public DateTime Date;
        public long Keys;
        public long Clicks;
        public long Left;
        public long Right;
        public long Middle;
        public long XButtons;
        public long Wheel;
        public double MovePx;
        public double MoveMeters;
        public long[] HourKeys = new long[24];
        public long[] HourClicks = new long[24];
        public Dictionary<int, long> KeyCounts = new Dictionary<int, long>();
        public Dictionary<int, long> PhysicalKeys = new Dictionary<int, long>();
        public Dictionary<int, long> PhysicalVks = new Dictionary<int, long>();
        public Dictionary<string, long> ComboCounts = new Dictionary<string, long>(StringComparer.Ordinal);
        public Dictionary<string, AppUsage> Apps = new Dictionary<string, AppUsage>(StringComparer.Ordinal);
        public double ActiveSeconds, IdleSeconds;
        public double[] ActiveHours = new double[24];
        public List<ActiveSession> Sessions = new List<ActiveSession>();
        public List<ActiveSession> IdlePeriods = new List<ActiveSession>();
        public double AppObservedSeconds;
        public long AppSwitches, PeakApm;
        public readonly CrossDay Cross=new CrossDay();

        public bool IsEmpty
        {
            get { return Keys == 0 && Clicks == 0 && Wheel == 0 && MovePx < 0.5 && MoveMeters == 0 && ActiveSeconds == 0 && IdleSeconds == 0 && Cross.Packets==0 && Cross.Clicks==0; }
        }
    }

    /// <summary>实时速率(最近 60 秒滑动窗口)。</summary>
    internal static class LiveRate
    {
        private static readonly long[] _kb = new long[60];
        private static readonly long[] _ms = new long[60];
        private static int _idx;
        private static readonly long[] _trend = NewTrend();
        private static int _trendIndex;
        public static long TrendVersion { get; private set; }
        private static long[] NewTrend(){long[] values=new long[300];for(int i=0;i<300;i++)values[i]=-1;return values;}
        public static long[] Trend(){long[] values=new long[300];for(int i=0;i<300;i++)values[i]=_trend[(_trendIndex+1+i)%300];values[299]=Apm;return values;}
        private static long _rateStart = System.Diagnostics.Stopwatch.GetTimestamp();
        public static double ActiveSeconds { get { return Store.Today.ActiveSeconds; } }
        public static long Apm { get { return KeysPerMin + ClicksPerMin; } }

        public static void AddKey() { Tick(); _kb[_idx]++; RateMemory.Add(DateTime.Now); }
        public static void AddClick() { Tick(); _ms[_idx]++; RateMemory.Add(DateTime.Now); }
        public static void Reset()
        { Array.Clear(_kb, 0, 60); Array.Clear(_ms, 0, 60); _idx = 0; _trendIndex=0;for(int i=0;i<300;i++)_trend[i]=-1;TrendVersion++; _rateStart = System.Diagnostics.Stopwatch.GetTimestamp(); RateMemory.Clear(); }

        public static void Tick(){TickAt(System.Diagnostics.Stopwatch.GetTimestamp());}
        internal static void TickAt(long now)
        {
            long frequency=System.Diagnostics.Stopwatch.Frequency;
            long steps=(now-_rateStart)/frequency;
            if(steps<=0)return;
            _trend[_trendIndex]=Apm;
            bool gap=steps>5;
            if(steps>=300)
            {
                Array.Clear(_kb,0,60);Array.Clear(_ms,0,60);
                for(int i=0;i<300;i++)_trend[i]=-1;
                _idx=0;_trendIndex=0;
            }
            else for(int i=0;i<steps;i++)
            {
                _idx=(_idx+1)%60;_kb[_idx]=0;_ms[_idx]=0;
                _trendIndex=(_trendIndex+1)%300;
                _trend[_trendIndex]=gap?-1:Apm;
            }
            _trend[_trendIndex]=Apm;
            _rateStart+=steps*frequency;TrendVersion++;
        }
        public static void PaintTrend(Graphics g,RectangleF rect,Color color)
        {
            long[] values=Trend();long peak=1;
            foreach(long value in values)peak=Math.Max(peak,value);
            using(Pen baseline=new Pen(Color.FromArgb(45,color)))g.DrawLine(baseline,rect.Left,rect.Bottom,rect.Right,rect.Bottom);
            using(Pen line=new Pen(color,1.6f))
            {
                PointF previous=PointF.Empty;bool hasPrevious=false;
                for(int i=0;i<values.Length;i++)
                {
                    if(values[i]<0){hasPrevious=false;continue;}
                    PointF point=new PointF(rect.Left+rect.Width*i/299f,rect.Bottom-(float)(rect.Height*values[i]/peak));
                    if(hasPrevious)g.DrawLine(line,previous,point);
                    previous=point;hasPrevious=true;
                }
                if(hasPrevious)using(SolidBrush dot=new SolidBrush(color))g.FillEllipse(dot,previous.X-2,previous.Y-2,4,4);
            }
        }
        public static long KeysPerMin
        {
            get { long s = 0; for (int i = 0; i < 60; i++) s += _kb[i]; return s; }
        }

        public static long ClicksPerMin
        {
            get { long s = 0; for (int i = 0; i < 60; i++) s += _ms[i]; return s; }
        }
    }

    /// <summary>
    /// 数据持久化 v2:key=value 文本。
    /// 头部为 pos/total,其后为若干 [day=yyyy-MM-dd] 分节(按天历史)。
    /// 兼容读取 v1(扁平 today_* / total_*)。
    /// </summary>
    internal static class Store
    {
        private const int KeepDays = 365;
        private static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "键鼠统计");
        private static readonly string FilePath = Path.Combine(Dir, "stats.txt");

        public static Dictionary<DateTime, DayRecord> History = new Dictionary<DateTime, DayRecord>();
        public static DayRecord Today = new DayRecord();
        public static Counters Total = new Counters();
        public static DateTime Day = DateTime.Today;
        public static int ThemeId;
        public static int KeyboardLayout;
        public static long AllTimePeakApm;
        public static int PosX = -1;
        public static int PosY = -1;
        public static double MouseDpi; // 0: 用户尚未设置，不猜测硬件 DPI。
        public static int IdleThresholdSeconds = 60;

        static Store()
        {
            Today.Date = DateTime.Today;
            History[DateTime.Today] = Today;
        }

        public static void RollDay(DateTime newDay)
        {
            Day = newDay;
            DayRecord rec;
            if (!History.TryGetValue(newDay, out rec))
            {
                rec = new DayRecord();
                rec.Date = newDay;
                History[newDay] = rec;
            }
            Today = rec;
        }

        private static long ParseL(string s)
        {
            long v; long.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out v); return v;
        }
        private static double ParseD(string s)
        {
            double v; double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out v); return v;
        }

        public static void Load()
        {
            ThemeId = 0;
            KeyboardLayout = 0;
            AllTimePeakApm = 0;
            try
            {
                if (!File.Exists(FilePath)) return;

                string storedDate = "";
                Counters total = new Counters();
                DayRecord legacyToday = null;   // v1 扁平格式里的 today_*
                int posX = -1, posY = -1;
                DayRecord cur = null;

                foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;

                    if (line.StartsWith("[day=") && line.EndsWith("]"))
                    {
                        DateTime d;
                        string ds = line.Substring(5, line.Length - 6).Trim();
                        if (DateTime.TryParseExact(ds, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                DateTimeStyles.None, out d))
                        {
                            if (!History.ContainsKey(d))
                            {
                                DayRecord r = new DayRecord();
                                r.Date = d;
                                History[d] = r;
                            }
                            cur = History[d];
                        }
                        continue;
                    }

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();

                    if (cur != null)
                    {
                        switch (key)
                        {
                            case "keys": cur.Keys = ParseL(val); break;
                            case "clicks": cur.Clicks = ParseL(val); break;
                            case "left": cur.Left = ParseL(val); break;
                            case "right": cur.Right = ParseL(val); break;
                            case "middle": cur.Middle = ParseL(val); break;
                            case "xbuttons": cur.XButtons = ParseL(val); break;
                            case "wheel": cur.Wheel = ParseL(val); break;
                            case "move": cur.MovePx = ParseD(val); break;
                            case "move_m": cur.MoveMeters = NonNegative(ParseD(val)); break;
                            case "hk": ParseHourBuckets(val, cur.HourKeys); break;
                            case "hc": ParseHourBuckets(val, cur.HourClicks); break;
                            case "kk": ParseKeyCounts(val, cur.KeyCounts); break;
                            case "physical_keys": ParseKeyCounts(val, cur.PhysicalKeys, 768); break;
                            case "physical_vks": ParseKeyCounts(val, cur.PhysicalVks); break;
                            case "combo": ShortcutStats.Load(cur, val); break;
                            case "app": AppActivity.LoadRecord(cur, val); break;
                            case "active_seconds": cur.ActiveSeconds = ActivityMonitor.ParseSeconds(val); break;
                            case "app_observed_seconds": cur.AppObservedSeconds = ActivityMonitor.ParseSeconds(val); break;
                            case "app_switches": cur.AppSwitches = Math.Max(0, ParseL(val)); break;
                            case "peak_apm": cur.PeakApm = Math.Max(0, ParseL(val)); break;
                            case "idle_seconds": cur.IdleSeconds = ActivityMonitor.ParseSeconds(val); break;
                            case "active_hours": ActivityMonitor.LoadHours(cur.ActiveHours, val); break;
                            case "idle_period": ActivityMonitor.LoadPeriod(cur, val, true); break;
                            case "session": ActivityMonitor.LoadSession(cur, val); break;
                            case "session_reason_v1": ActivityMonitor.LoadSessionReason(cur,val);break;
                            case "cross_hours_v1": case "app_hours_v1": case "session_coverage_v1": case "session_app_v1": case "mouse_vector_v1": CrossTelemetry.Load(cur,key,val);break;
                        }
                    }
                    else
                    {
                        switch (key)
                        {
                            case "date": storedDate = val; break;
                            case "art_theme": ThemeId = ArtTheme.Validate((int)ParseL(val)); break;
                            case "keyboard_layout": KeyboardLayout = KeyboardHeat.Valid((int)ParseL(val)); break;
                            case "all_time_peak_apm": AllTimePeakApm = Math.Max(0, ParseL(val)); break;
                            case "app_rule": AppActivity.LoadRule(val); break;
                            case "shortcut_model_v1": ShortcutSavings.LoadModel(val);break;
                            case "idle_threshold":
                                long seconds = ParseL(val);
                                IdleThresholdSeconds = seconds >= 10 && seconds <= 3600 ? (int)seconds : 60;
                                break;
                            case "pos_x": posX = (int)ParseL(val); break;
                            case "pos_y": posY = (int)ParseL(val); break;
                            // v1 total_*
                            case "total_keys": total.Keys = ParseL(val); break;
                            case "total_clicks": total.Clicks = ParseL(val); break;
                            case "total_left": total.Left = ParseL(val); break;
                            case "total_right": total.Right = ParseL(val); break;
                            case "total_middle": total.Middle = ParseL(val); break;
                            case "total_xbuttons": total.XButtons = ParseL(val); break;
                            case "total_wheel": total.Wheel = ParseL(val); break;
                            case "total_move": total.MovePx = ParseD(val); break;
                            case "total_move_m": total.MoveMeters = NonNegative(ParseD(val)); break;
                            case "mouse_dpi":
                                double dpi = ParseD(val);
                                MouseDpi = MouseDistance.ValidDpi(dpi) ? dpi : 0;
                                break;
                            // v1 today_* → 暂存,稍后并入分节
                            case "today_keys":
                                if (legacyToday == null) legacyToday = new DayRecord();
                                legacyToday.Keys = ParseL(val); break;
                            case "today_clicks":
                                if (legacyToday == null) legacyToday = new DayRecord();
                                legacyToday.Clicks = ParseL(val); break;
                            case "today_left":
                                if (legacyToday == null) legacyToday = new DayRecord();
                                legacyToday.Left = ParseL(val); break;
                            case "today_right":
                                if (legacyToday == null) legacyToday = new DayRecord();
                                legacyToday.Right = ParseL(val); break;
                            case "today_middle":
                                if (legacyToday == null) legacyToday = new DayRecord();
                                legacyToday.Middle = ParseL(val); break;
                            case "today_xbuttons":
                                if (legacyToday == null) legacyToday = new DayRecord();
                                legacyToday.XButtons = ParseL(val); break;
                            case "today_wheel":
                                if (legacyToday == null) legacyToday = new DayRecord();
                                legacyToday.Wheel = ParseL(val); break;
                            case "today_move":
                                if (legacyToday == null) legacyToday = new DayRecord();
                                legacyToday.MovePx = ParseD(val); break;
                        }
                    }
                }

                DailySignal.Clear();
                Total = total;
                PosX = posX; PosY = posY;

                // v1 数据迁移:把扁平 today_* 并入对应日期的分节(已存在则合并)
                DateTime legacyDay;
                if (legacyToday != null && DateTime.TryParseExact(storedDate, "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out legacyDay))
                {
                    legacyToday.Date = legacyDay;
                    DayRecord existing;
                    if (History.TryGetValue(legacyDay, out existing))
                    {
                        existing.Keys += legacyToday.Keys;
                        existing.Clicks += legacyToday.Clicks;
                        existing.Left += legacyToday.Left;
                        existing.Right += legacyToday.Right;
                        existing.Middle += legacyToday.Middle;
                        existing.XButtons += legacyToday.XButtons;
                        existing.Wheel += legacyToday.Wheel;
                        existing.MovePx += legacyToday.MovePx;
                    }
                    else if (!legacyToday.IsEmpty)
                    {
                        History[legacyDay] = legacyToday;
                    }
                }

                foreach(DayRecord record in History.Values)ActivityMonitor.RemoveLegacyPhantomSessions(record);
                RollDay(DateTime.Today);
                Prune();
            }
            catch { }
        }

        private static void ParseHourBuckets(string val, long[] buckets)
        {
            string[] parts = val.Split(',');
            foreach (string p in parts)
            {
                int c = p.IndexOf(':');
                if (c <= 0) continue;
                int h = (int)ParseL(p.Substring(0, c).Trim());
                long v = ParseL(p.Substring(c + 1).Trim());
                if (h >= 0 && h < 24) buckets[h] += v;
            }
        }

        private static void ParseKeyCounts(string val, Dictionary<int, long> dict, int limit = 256)
        {
            string[] parts = val.Split(',');
            foreach (string p in parts)
            {
                int c = p.IndexOf(':');
                if (c <= 0) continue;
                int vk = (int)ParseL(p.Substring(0, c).Trim());
                long v = ParseL(p.Substring(c + 1).Trim());
                if (vk > 0 && vk < limit && v > 0)
                {
                    long old; dict.TryGetValue(vk, out old);
                    dict[vk] = old + v;
                }
            }
        }

        private static void Prune()
        {
            DateTime oldest = DateTime.Today.AddDays(-(KeepDays - 1));
            List<DateTime> remove = new List<DateTime>();
            foreach (KeyValuePair<DateTime, DayRecord> kv in History)
            {
                if (kv.Key < oldest) remove.Add(kv.Key);
            }
            foreach (DateTime d in remove) History.Remove(d);
        }

        private static double NonNegative(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) || value < 0 ? 0 : value;
        }

        public static void Save()
        {
            try
            {
                Prune();
                if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# 键鼠统计数据文件 v10 / 正式版 1.3.1（趋势对比 / 交叉归因）");
                sb.AppendLine("shortcut_model_v1="+ShortcutSavings.EncodeModel());
                sb.AppendLine("idle_threshold=" + IdleThresholdSeconds.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("art_theme=" + ArtTheme.Validate(ThemeId).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("keyboard_layout=" + KeyboardHeat.Valid(KeyboardLayout).ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("all_time_peak_apm=" + AllTimePeakApm.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("mouse_dpi=" + MouseDpi.ToString("R", CultureInfo.InvariantCulture));
                foreach (KeyValuePair<string, int> rule in AppActivity.Rules)
                    sb.AppendLine("app_rule=" + AppActivity.Encode(rule.Key) + "|" + rule.Value.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("date=" + Day.ToString("yyyy-MM-dd"));
                sb.AppendLine("pos_x=" + PosX.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("pos_y=" + PosY.ToString(CultureInfo.InvariantCulture));
                AppendTotals(sb, Total);

                List<DateTime> days = new List<DateTime>(History.Keys);
                days.Sort();
                foreach (DateTime d in days)
                {
                    DayRecord r = History[d];
                    if (r.IsEmpty && d != Day) continue;
                    sb.AppendLine("[day=" + d.ToString("yyyy-MM-dd") + "]");
                    sb.AppendLine("keys=" + r.Keys.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("clicks=" + r.Clicks.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("left=" + r.Left.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("right=" + r.Right.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("middle=" + r.Middle.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("xbuttons=" + r.XButtons.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("wheel=" + r.Wheel.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("move=" + r.MovePx.ToString("0.###", CultureInfo.InvariantCulture));
                    sb.AppendLine("move_m=" + r.MoveMeters.ToString("R", CultureInfo.InvariantCulture));
                    sb.AppendLine("hk=" + EncodeHourBuckets(r.HourKeys));
                    sb.AppendLine("hc=" + EncodeHourBuckets(r.HourClicks));
                    sb.AppendLine("kk=" + EncodeKeyCounts(r.KeyCounts));
                    sb.AppendLine("physical_keys=" + EncodeKeyCounts(r.PhysicalKeys));
                    sb.AppendLine("physical_vks=" + EncodeKeyCounts(r.PhysicalVks));
                    foreach (KeyValuePair<string, long> combo in r.ComboCounts)
                        sb.AppendLine("combo=" + combo.Key + ":" + combo.Value.ToString(CultureInfo.InvariantCulture));
                    foreach (AppUsage app in r.Apps.Values)
                        sb.AppendLine("app=" + AppActivity.Serialize(app));
                    sb.AppendLine("active_seconds=" + r.ActiveSeconds.ToString("R", CultureInfo.InvariantCulture));
                    sb.AppendLine("app_observed_seconds=" + r.AppObservedSeconds.ToString("R", CultureInfo.InvariantCulture));
                    sb.AppendLine("app_switches=" + r.AppSwitches.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("peak_apm=" + r.PeakApm.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("idle_seconds=" + r.IdleSeconds.ToString("R", CultureInfo.InvariantCulture));
                    sb.AppendLine("active_hours=" + ActivityMonitor.EncodeHours(r.ActiveHours));
                    foreach (ActiveSession session in r.Sessions)
                        sb.AppendLine("session=" + ActivityMonitor.EncodeSession(session));
                    foreach(ActiveSession session in r.Sessions)if(session.StartReason!=SessionStartReason.Legacy)sb.AppendLine("session_reason_v1="+session.Start.Ticks+"|"+(int)session.StartReason);
                    foreach (ActiveSession period in r.IdlePeriods)
                        sb.AppendLine("idle_period=" + period.Start.ToString("O", CultureInfo.InvariantCulture) + "|"
                            + period.End.ToString("O", CultureInfo.InvariantCulture) + "|" + period.Seconds.ToString("R", CultureInfo.InvariantCulture));
                    CrossTelemetry.Save(sb,r);
                }

                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, FilePath + ".bak");
                else File.Move(tmp, FilePath);
            }
            catch { }
        }

        private static void AppendTotals(StringBuilder sb, Counters c)
        {
            sb.AppendLine("total_keys=" + c.Keys.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("total_clicks=" + c.Clicks.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("total_left=" + c.Left.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("total_right=" + c.Right.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("total_middle=" + c.Middle.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("total_xbuttons=" + c.XButtons.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("total_wheel=" + c.Wheel.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("total_move=" + c.MovePx.ToString("0.###", CultureInfo.InvariantCulture));
            sb.AppendLine("total_move_m=" + c.MoveMeters.ToString("R", CultureInfo.InvariantCulture));
        }

        private static string EncodeHourBuckets(long[] buckets)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < buckets.Length; i++)
            {
                if (buckets[i] == 0) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(i.ToString(CultureInfo.InvariantCulture));
                sb.Append(':');
                sb.Append(buckets[i].ToString(CultureInfo.InvariantCulture));
            }
            return sb.Length == 0 ? "-" : sb.ToString();
        }

        private static string EncodeKeyCounts(Dictionary<int, long> dict)
        {
            if (dict.Count == 0) return "-";
            List<int> vks = new List<int>(dict.Keys);
            vks.Sort();
            StringBuilder sb = new StringBuilder();
            foreach (int vk in vks)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append(vk.ToString(CultureInfo.InvariantCulture));
                sb.Append(':');
                sb.Append(dict[vk].ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }

    /// <summary>Win32 低级钩子相关。</summary>
    internal static class Native
    {
        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        public const int WH_KEYBOARD_LL = 13;
        public const int WH_MOUSE_LL = 14;

        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        public static bool IsKeyDown(int key) { return GetAsyncKeyState(key) < 0; }
        public const int WM_MOUSEMOVE = 0x0200;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_RBUTTONDOWN = 0x0204;
        public const int WM_MBUTTONDOWN = 0x0207;
        public const int WM_XBUTTONDOWN = 0x020B;
        public const int WM_MOUSEWHEEL = 0x020A;
        public const int WM_MOUSEHWHEEL = 0x020E;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }
    }

    /// <summary>悬浮小组件窗体。</summary>
    internal sealed class StatsWidget : Form
    {
        private const int BaseW = 248;
        private const int BaseH = 208;
        private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string RunValueName = "KeyMouseStatsWidget";

        private readonly Native.HookProc _kbProc;
        private readonly Native.HookProc _msProc;
        private IntPtr _kbHook = IntPtr.Zero;
        private IntPtr _msHook = IntPtr.Zero;

        private float _s = 1f;
        private bool _showTotal = false;
        private bool _clickThrough = false;
        private bool _initialized = false;
        private Dashboard _dash;
        private readonly RawMouseInput _rawMouse = new RawMouseInput();
        private readonly MouseVectorTracker _mouseVectors=new MouseVectorTracker();
        private readonly ShortcutTracker _combos = new ShortcutTracker();
        private DistanceSettings _distanceSettings;

        private bool _dragging = false;
        private bool _dragMoved = false;
        private Point _dragFormStart;
        private Point _dragMouseStart;

        private int _lastX, _lastY;
        private bool _lastValid = false;
        private bool _dirtyUI = false;
        private bool _dirtySave = false;
        private DateTime _lastSave = DateTime.Now;

        private NotifyIcon _tray;
        private ContextMenuStrip _menu;
        private ToolStripMenuItem _miShow;
        private ToolStripMenuItem _miTopMost;
        private ToolStripMenuItem _miClickThrough;
        private ToolStripMenuItem _miAutoStart;
        private System.Windows.Forms.Timer _timer;
        private Font _fTitle, _fMode, _fLabel, _fValue;
        private StringFormat _rightAlign;
        private ToolTip _tip;
        private RegisteredWaitHandle _dashWait;

        public StatsWidget()
        {
            Store.Load();

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            BackColor = ArtTheme.Current.Card;
            Opacity = 0.96;
            TopMost = true;

            _kbProc = KbHookProc;
            _msProc = MsHookProc;

            BuildMenu();
            BuildTray();
            ContextMenuStrip = _menu;

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 250;
            _timer.Tick += OnTick;

            _rightAlign = new StringFormat();
            _rightAlign.Alignment = StringAlignment.Far;
            _rightAlign.LineAlignment = StringAlignment.Near;

            _tip = new ToolTip();
            _tip.SetToolTip(this, "键鼠统计小组件\r\n单击:切换 今日 / 累计\r\n双击:详细统计面板\r\n拖动:移动位置\r\n右键:菜单");
        }

        public void ListenForDashboard(EventWaitHandle ev)
        {
            _dashWait = ThreadPool.RegisterWaitForSingleObject(ev,
                delegate { try { BeginInvoke(new MethodInvoker(OpenDashboard)); } catch { } },
                null, -1, false);
        }

        private void OpenDashboard()
        {
            try
            {
                if (_dash == null || _dash.IsDisposed)
                {
                    _dash = new Dashboard();
                    _dash.Owner = this;
                }
                if (_dash.Visible) _dash.Activate();
                else _dash.Show();
                _dash.BringToFront();
            }
            catch { }
        }

        // ---------------------------------------------------------------- 窗口

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _rawMouse.Register(Handle); // 鼠标穿透会重建句柄，需要重新注册。
            ActivityMonitor.Register(Handle);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            _rawMouse.Unregister();
            ActivityMonitor.Unregister(Handle);
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            ActivityMonitor.Message(m.Msg, m.WParam);
            if(m.Msg==0x02B1||m.Msg==0x0218)_mouseVectors.Reset();
            if (_combos != null && (m.Msg == 0x02B1 || m.Msg == 0x0218
                && (m.WParam.ToInt64() == 4 || m.WParam.ToInt64() == 7 || m.WParam.ToInt64() == 18))) _combos.Reset();
            if (m.Msg == 0x00FF && _rawMouse != null)
            {
                double counts;
                if (_rawMouse.Read(m.LParam, out counts))
                {
                    bool calibrating = _distanceSettings != null && _distanceSettings.Collect(counts);
                    if(calibrating)_mouseVectors.Reset();
                    else if(counts>0)
                    {
                        if(Store.Day!=DateTime.Today)Store.RollDay(DateTime.Today);
                        _mouseVectors.Move(Store.Today,VectorNow(),_rawMouse.LastDevice,_rawMouse.LastDx,_rawMouse.LastDy,Store.MouseDpi);MarkDirty();
                    }
                    if (!calibrating && MouseDistance.ValidDpi(Store.MouseDpi) && counts > 0)
                    {
                        if (Store.Day != DateTime.Today) Store.RollDay(DateTime.Today);
                        double meters = MouseDistance.ToMeters(counts, Store.MouseDpi);
                        Store.Today.MoveMeters += meters;
                        Store.Total.MoveMeters += meters;
                        MarkDirty();
                    }
                }
            }
            // WM_INPUT 前台消息必须交给默认窗口过程清理。
            base.WndProc(ref m);
        }

        private void OpenDistanceSettings()
        {
            using (DistanceSettings dialog = new DistanceSettings(_rawMouse))
            {
                _distanceSettings = dialog;
                try { dialog.ShowDialog(this); }
                finally { _distanceSettings = null; }
            }
            MarkDirty();
            SaveNow();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80;  // WS_EX_TOOLWINDOW:不出现在 Alt+Tab
                if (_clickThrough) cp.ExStyle |= 0x20 | 0x80000;
                return cp;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (_initialized) return;
            _initialized = true;

            try
            {
                using (Graphics g = CreateGraphics())
                {
                    if (g.DpiX > 96f) _s = g.DpiX / 96f;
                }
            }
            catch { }

            ClientSize = new Size((int)Math.Round(BaseW * _s), (int)Math.Round(BaseH * _s));
            ApplyRoundedRegion();

            _fTitle = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
            _fMode = new Font("Microsoft YaHei UI", 8.25f);
            _fLabel = new Font("Microsoft YaHei UI", 8.25f);
            _fValue = new Font("Segoe UI", 13.5f, FontStyle.Bold);

            Rectangle vs = SystemInformation.VirtualScreen;
            bool posOk = Store.PosX != -1 &&
                vs.Contains(new Point(Store.PosX + ClientSize.Width / 2, Store.PosY + 20));
            if (posOk)
            {
                Location = new Point(Store.PosX, Store.PosY);
            }
            else
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(wa.Right - Width - (int)(24 * _s),
                                     wa.Bottom - Height - (int)(24 * _s));
            }

            InstallHooks();
            if (!_rawMouse.Registered)
                _tray.ShowBalloonTip(5000, "移动距离无法采集", _rawMouse.Status, ToolTipIcon.Warning);
            else if (!MouseDistance.ValidDpi(Store.MouseDpi))
                _tray.ShowBalloonTip(6000, "请设置鼠标 DPI", "右键菜单 → 鼠标 DPI / 距离校准，设置后开始估算实际移动距离。", ToolTipIcon.Info);
            _timer.Start();
        }

        private void ApplyRoundedRegion()
        {
            try
            {
                using (GraphicsPath p = RoundedPath(new Rectangle(Point.Empty, ClientSize), (int)Math.Round(16 * _s)))
                {
                    Region old = Region;
                    Region = new Region(p);
                    if (old != null) old.Dispose();
                }
            }
            catch { }
        }

        private void InstallHooks()
        {
            _combos.Seed(Native.IsKeyDown);
            IntPtr hMod = Native.GetModuleHandle(null);
            _kbHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _kbProc, hMod, 0);
            _msHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _msProc, hMod, 0);
            if (_kbHook == IntPtr.Zero || _msHook == IntPtr.Zero)
            {
                if (_tray != null)
                    _tray.ShowBalloonTip(5000, "键鼠统计",
                        "统计钩子安装失败(可能被安全软件拦截),计数可能不准确。", ToolTipIcon.Warning);
            }
        }

        // ---------------------------------------------------------------- 钩子

        private void MarkDirty()
        {
            _dirtyUI = true;
            _dirtySave = true;
        }

        private IntPtr KbHookProc(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                try
                {
                    int msg = unchecked((int)wParam.ToInt64());
                    if (msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP)
                    {
                        Native.KBDLLHOOKSTRUCT released = (Native.KBDLLHOOKSTRUCT)
                            Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                        _combos.Process(msg, (int)released.vkCode, released.scanCode, released.flags);
                    }
                    if (msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN)
                    {
                        Native.KBDLLHOOKSTRUCT info = (Native.KBDLLHOOKSTRUCT)
                            Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                        if (info.vkCode != 0)
                        {
                            int vk = (int)info.vkCode;
                            bool firstPress;
                            string combo = _combos.Process(msg, vk, info.scanCode, info.flags, out firstPress);
                            if (firstPress)
                            {
                                DateTime now = DateTime.Now;
                                if (Store.Day != now.Date) Store.RollDay(now.Date);
                                AppUsage foreground = ForegroundApp.Capture();
                                vk = ShortcutTracker.Normalize(vk, info.scanCode, info.flags);
                                DayRecord t = Store.Today;
                                ShortcutStats.Add(t, combo);
                                t.Keys++;
                                t.HourKeys[now.Hour]++;
                                long c;
                                t.KeyCounts.TryGetValue(vk, out c);
                                t.KeyCounts[vk] = c + 1;
                                KeyboardHeat.Record(t, vk, info.scanCode, info.flags);
                                Store.Total.Keys++;
                                AppActivity.Record(t, foreground, 0);
                                LiveRate.AddKey();
                                MarkDirty();
                            }
                        }
                    }
                }
                catch { }
            }
            return Native.CallNextHookEx(_kbHook, code, wParam, lParam);
        }

        private IntPtr MsHookProc(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                try
                {
                    int msg = unchecked((int)wParam.ToInt64());
                    Native.MSLLHOOKSTRUCT info = (Native.MSLLHOOKSTRUCT)
                        Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                    DateTime now = DateTime.Now;
                    if (Store.Day != now.Date) Store.RollDay(now.Date);
                    bool isClick = msg == Native.WM_LBUTTONDOWN || msg == Native.WM_RBUTTONDOWN
                        || msg == Native.WM_MBUTTONDOWN || msg == Native.WM_XBUTTONDOWN;
                    bool isWheel = msg == Native.WM_MOUSEWHEEL || msg == Native.WM_MOUSEHWHEEL;
                    if (isClick || isWheel) AppActivity.Record(Store.Today, ForegroundApp.Capture(), isClick ? 1 : 2);
                    DayRecord t = Store.Today;
                    Counters g = Store.Total;
                    int button=msg==0x0201||msg==0x0202?1:msg==0x0204||msg==0x0205?2:msg==0x0207||msg==0x0208?3:msg==0x020B||msg==0x020C?4+(int)((info.mouseData>>16)&0xffff):0;
                    if(button!=0&&(!isClick||_rawMouse.Registered&&_rawMouse.RelativeSeen&&!(_distanceSettings!=null&&_distanceSettings.Collect(0))))_mouseVectors.Button(t,VectorNow(),button,isClick);
                    int h = now.Hour;
                    switch (msg)
                    {
                        case Native.WM_MOUSEMOVE:
                            if (_lastValid)
                            {
                                int dx = info.pt.X - _lastX;
                                int dy = info.pt.Y - _lastY;
                                double d = Math.Sqrt((double)dx * dx + (double)dy * dy);
                                t.MovePx += d;
                                g.MovePx += d;
                            }
                            _lastX = info.pt.X;
                            _lastY = info.pt.Y;
                            _lastValid = true;
                            MarkDirty();
                            break;
                        case Native.WM_LBUTTONDOWN:
                            t.Clicks++; t.Left++; t.HourClicks[h]++;
                            g.Clicks++; g.Left++;
                            LiveRate.AddClick(); MarkDirty(); break;
                        case Native.WM_RBUTTONDOWN:
                            t.Clicks++; t.Right++; t.HourClicks[h]++;
                            g.Clicks++; g.Right++;
                            LiveRate.AddClick(); MarkDirty(); break;
                        case Native.WM_MBUTTONDOWN:
                            t.Clicks++; t.Middle++; t.HourClicks[h]++;
                            g.Clicks++; g.Middle++;
                            LiveRate.AddClick(); MarkDirty(); break;
                        case Native.WM_XBUTTONDOWN:
                            t.Clicks++; t.XButtons++; t.HourClicks[h]++;
                            g.Clicks++; g.XButtons++;
                            LiveRate.AddClick(); MarkDirty(); break;
                        case Native.WM_MOUSEWHEEL:
                        case Native.WM_MOUSEHWHEEL:
                            t.Wheel++;
                            g.Wheel++;
                            MarkDirty(); break;
                    }
                }
                catch { }
            }
            return Native.CallNextHookEx(_msHook, code, wParam, lParam);
        }

        // ---------------------------------------------------------------- 绘制
        private static long VectorNow(){return (long)(System.Diagnostics.Stopwatch.GetTimestamp()*(1000.0/System.Diagnostics.Stopwatch.Frequency));}

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            float s = _s;

            using (GraphicsPath path = RoundedPath(new Rectangle(Point.Empty, ClientSize), (int)Math.Round(16 * s)))
            {
                using (SolidBrush bg = new SolidBrush(ArtTheme.Current.Card)) g.FillPath(bg, path);
                if(WuxiaArt.Active){GraphicsState artState=g.Save();g.SetClip(path);WuxiaArt.DrawWidget(g,new RectangleF(0,0,ClientSize.Width,ClientSize.Height));g.Restore(artState);}
                if(ThemeArt.Active)
                {
                    GraphicsState state=g.Save();g.SetClip(path);ThemeArt.Sticker(g,ActivityMonitor.CurrentSession==null?3:0,new RectangleF(107*s,3*s,30*s,30*s));g.Restore(state);
                }
                using (Pen edge = new Pen(ArtTheme.Current.Line)) g.DrawPath(edge, path);
            }

            using (SolidBrush b = new SolidBrush(ArtTheme.Current.Text))
                g.DrawString("键鼠统计", _fTitle, b, 16 * s, 9 * s);

            string modeText = string.Format("{0} · {1:MM-dd}", _showTotal ? "累计" : "今日", Store.Day);
            RectangleF modeRect = new RectangleF(0, 13 * s, ClientSize.Width - 16 * s, 20 * s);
            using (SolidBrush b = new SolidBrush(ArtTheme.Current.Muted))
                g.DrawString(modeText, _fMode, b, modeRect, _rightAlign);

            using (Pen pen = new Pen(ArtTheme.Current.Line))
                g.DrawLine(pen, 14 * s, 36 * s, ClientSize.Width - 14 * s, 36 * s);

            DayRecord c = _showTotal ? ToDayRecord(Store.Total) : Store.Today;
            float col2 = ClientSize.Width / 2f + 6;

            DrawCard(g, 18 * s, 46 * s, ArtTheme.Current.Accent, "键盘击键", Analysis.FmtCount(c.Keys));
            DrawCard(g, 18 * s, 92 * s, ArtTheme.Current.Green, "鼠标点击", Analysis.FmtCount(c.Clicks));
            DrawCard(g, col2, 46 * s, ArtTheme.Current.Orange, "滚轮滚动", Analysis.FmtCount(c.Wheel));
            DrawCard(g, col2, 92 * s, ArtTheme.Current.Purple, "鼠标路程(估算)",
                !_rawMouse.Registered ? "采集不可用" : Analysis.FmtDistance(c.MoveMeters));
            using (Pen line = new Pen(ArtTheme.Current.Line)) g.DrawLine(line, 14 * s, 137 * s, ClientSize.Width - 14 * s, 137 * s);
            ActiveSession current = ActivityMonitor.CurrentSession;
            string live = "APM " + LiveRate.Apm + " · 本段 " + (current == null ? "--" : ActivityMonitor.FormatDuration(current.Seconds));
            using (SolidBrush text = new SolidBrush(ArtTheme.Current.Muted))
            using (StringFormat format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(live, _fMode, text, new RectangleF(16 * s, 143 * s, ClientSize.Width - 32 * s, 22 * s), format);
            LiveRate.PaintTrend(g,new RectangleF(16*s,172*s,ClientSize.Width-32*s,19*s),ArtTheme.Current.Cyan);
            using(SolidBrush label=new SolidBrush(ArtTheme.Current.Muted))g.DrawString("最近 5 分钟 APM",_fMode,label,16*s,192*s);
        }

        private static DayRecord ToDayRecord(Counters c)
        {
            DayRecord r = new DayRecord();
            r.Keys = c.Keys; r.Clicks = c.Clicks; r.Wheel = c.Wheel; r.MovePx = c.MovePx;
            r.MoveMeters = c.MoveMeters;
            return r;
        }

        private void DrawCard(Graphics g, float x, float y, Color dot, string label, string value)
        {
            float s = _s;
            if(WuxiaArt.Active)
            {
                RectangleF panel=new RectangleF(x-5*s,y-3*s,ClientSize.Width/2f-17*s,42*s);
                using(SolidBrush shade=new SolidBrush(Color.FromArgb(116,ArtTheme.Current.Sidebar)))g.FillRectangle(shade,panel);
                using(Pen edge=new Pen(Color.FromArgb(145,ArtTheme.Current.Orange),.8f))g.DrawRectangle(edge,panel.X,panel.Y,panel.Width,panel.Height);
            }
            using (SolidBrush db = new SolidBrush(dot))
                g.FillEllipse(db, x, y + 4.5f * s, 7 * s, 7 * s);
            using (SolidBrush lb = new SolidBrush(ArtTheme.Current.Muted))
                g.DrawString(label, _fLabel, lb, x + 13 * s, y);
            using (SolidBrush vb = new SolidBrush(ArtTheme.Current.Text))
                g.DrawString(value, _fValue, vb, x, y + 15 * s);
        }

        internal static GraphicsPath RoundedPath(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = Math.Max(2, radius * 2);
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ---------------------------------------------------------------- 交互

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _dragMoved = false;
                _dragMouseStart = Control.MousePosition;
                _dragFormStart = Location;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
            {
                Point now = Control.MousePosition;
                int dx = now.X - _dragMouseStart.X;
                int dy = now.Y - _dragMouseStart.Y;
                if (Math.Abs(dx) > 4 || Math.Abs(dy) > 4) _dragMoved = true;
                if (_dragMoved) Location = new Point(_dragFormStart.X + dx, _dragFormStart.Y + dy);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && _dragging)
            {
                _dragging = false;
                if (_dragMoved)
                {
                    Store.PosX = Location.X;
                    Store.PosY = Location.Y;
                    _dirtySave = true;
                }
                else
                {
                    _showTotal = !_showTotal;
                    _dirtyUI = true;
                }
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            OpenDashboard();
        }

        // ---------------------------------------------------------------- 菜单 / 托盘

        private GameHud _hud;
        private GameHud Hud { get { if(_hud==null||_hud.IsDisposed)_hud=new GameHud();return _hud; } }
        private void BuildMenu()
        {
            _menu = new ContextMenuStrip();
            NikkiArt.Menu(_menu);

            ToolStripMenuItem miDash = new ToolStripMenuItem("详细统计面板", null,
                delegate { OpenDashboard(); });
            miDash.Font = new Font(SystemFonts.MenuFont, FontStyle.Bold);

            ToolStripMenuItem hudMenu=new ToolStripMenuItem("游戏 HUD");
            ToolStripMenuItem hudShow=new ToolStripMenuItem("显示 HUD（鼠标穿透）");
            hudShow.Click+=delegate{if(Hud.Visible)Hud.Hide();else Hud.Show();hudShow.Checked=Hud.Visible;};
            ToolStripMenuItem hudEdit=new ToolStripMenuItem("调整位置（临时取消穿透）");
            hudEdit.Click+=delegate{Hud.Editing=!Hud.Editing;hudEdit.Checked=Hud.Editing;if(!Hud.Visible)Hud.Show();hudShow.Checked=true;};
            hudMenu.DropDownOpening+=delegate{hudShow.Checked=_hud!=null&&!_hud.IsDisposed&&_hud.Visible;hudEdit.Checked=_hud!=null&&!_hud.IsDisposed&&_hud.Editing;};
            hudMenu.DropDownItems.Add(hudShow);hudMenu.DropDownItems.Add(hudEdit);
            hudMenu.DropDownItems.Add("移到当前屏幕左上角",null,delegate{Hud.Place(false);});
            hudMenu.DropDownItems.Add("移到当前屏幕右上角",null,delegate{Hud.Place(true);});
            ToolStripMenuItem alpha=new ToolStripMenuItem("不透明度");
            foreach(int level in new[]{50,70,80,95}){int opacity=level;alpha.DropDownItems.Add(level+"%",null,delegate{Hud.Opacity=opacity/100.0;});}
            hudMenu.DropDownItems.Add(alpha);
            hudMenu.DropDownItems.Add(new ToolStripMenuItem("适用于窗口化 / 无边框游戏"){Enabled=false});

            _miShow = new ToolStripMenuItem("隐藏小组件", null, delegate { ToggleVisible(); });

            ToolStripSeparator sep1 = new ToolStripSeparator();

            _miTopMost = new ToolStripMenuItem("窗口置顶", null, delegate
            {
                _miTopMost.Checked = !_miTopMost.Checked;
                TopMost = _miTopMost.Checked;
            });
            _miTopMost.Checked = true;

            _miClickThrough = new ToolStripMenuItem("鼠标穿透(点击穿过小组件)", null, delegate
            {
                _miClickThrough.Checked = !_miClickThrough.Checked;
                SetClickThrough(_miClickThrough.Checked);
            });

            _miAutoStart = new ToolStripMenuItem("开机自动启动", null, delegate
            {
                bool on = !_miAutoStart.Checked;
                SetAutoStart(on);
                _miAutoStart.Checked = GetAutoStart();
            });
            _miAutoStart.Checked = GetAutoStart();

            ToolStripSeparator sep2 = new ToolStripSeparator();

            ToolStripMenuItem miResetToday = new ToolStripMenuItem("重置今日统计", null, delegate
            {
                Store.RollDay(DateTime.Today);
                Store.Today = new DayRecord();
                Store.Today.Date = DateTime.Today;
                Store.History[DateTime.Today] = Store.Today;
                ActivityMonitor.Reset();
                LiveRate.Reset();
                _combos.Seed(Native.IsKeyDown);
                _dirtySave = true;
                _dirtyUI = true;
            });

            ToolStripMenuItem miResetAll = new ToolStripMenuItem("重置全部统计...", null, delegate
            {
                DialogResult r = ThemeMessage.Show(this,
                    "确定要清空全部统计吗?\n今日和历史数据都会归零。",
                    "重置全部统计", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) return;
                Store.History.Clear();
                Store.Total = new Counters();
                Store.RollDay(DateTime.Today);
                ActivityMonitor.Reset();_mouseVectors.Reset();DailySignal.Clear();
                Store.AllTimePeakApm = 0; LiveRate.Reset();
                _combos.Seed(Native.IsKeyDown);
                _dirtySave = true;
                _dirtyUI = true;
            });

            ToolStripSeparator sep3 = new ToolStripSeparator();

            ToolStripMenuItem miExit = new ToolStripMenuItem("退出", null, delegate { Close(); });

            _menu.Items.AddRange(new ToolStripItem[] {
                miDash, new ToolStripMenuItem("鼠标 DPI / 距离校准...", null, delegate { OpenDistanceSettings(); }), new ToolStripSeparator(),
                new ToolStripMenuItem("空闲阈值 / 活跃时长...", null, delegate { using (ActivitySettings dialog = new ActivitySettings()) dialog.ShowDialog(this); }),
                new ToolStripMenuItem("今日连续使用段...", null, delegate { using (SessionDetails dialog = new SessionDetails()) dialog.ShowDialog(this); }),
                _miShow, hudMenu, sep1, _miTopMost, _miClickThrough, _miAutoStart,
                sep2, miResetToday, miResetAll, sep3, miExit });
        }

        private void BuildTray()
        {
            _tray = new NotifyIcon();
            _tray.Icon = MakeIcon();
            _tray.Text = "键鼠统计 1.3.1 正式版";
            _tray.Visible = true;
            _tray.ContextMenuStrip = _menu;
            _tray.MouseDoubleClick += delegate { ToggleVisible(); };
        }

        private void ToggleVisible()
        {
            if (Visible)
            {
                Hide();
                _miShow.Text = "显示小组件";
            }
            else
            {
                Show();
                _miShow.Text = "隐藏小组件";
            }
        }

        private void SetClickThrough(bool on)
        {
            _clickThrough = on;
            RecreateHandle();
        }

        private static bool GetAutoStart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                {
                    return k != null && k.GetValue(RunValueName) != null;
                }
            }
            catch { return false; }
        }

        private static void SetAutoStart(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (k == null) return;
                    if (on) k.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue(RunValueName, false);
                }
            }
            catch { }
        }

        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
        internal void RefreshTheme()
        {
            NikkiArt.SyncTheme();
            BackColor=ArtTheme.Current.Card;
            if(_tray!=null) { Icon previous=_tray.Icon;_tray.Icon=MakeIcon();if(previous!=null)previous.Dispose(); }
            if(_tip!=null) { _tip.BackColor=ArtTheme.Current.Card;_tip.ForeColor=ArtTheme.Current.Text; }
            Invalidate();
        }
        internal static Icon MakeIcon()
        {
            try
            {
                using(Bitmap bmp = new Bitmap(32, 32))
                {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (GraphicsPath p = RoundedPath(new Rectangle(1, 1, 30, 30), 8))
                    using (SolidBrush b = new SolidBrush(ArtTheme.Current.Accent))
                        g.FillPath(b, p);
                    using (SolidBrush w = new SolidBrush(Color.White))
                    {
                        if(ThemeArt.Dark)ThemeArt.Sticker(g,0,new RectangleF(3,3,26,26));
                        else if(NikkiArt.Active)NikkiArt.Star(g,16,16,11,Color.White);
                        else {
                        for (int r = 0; r < 3; r++)
                            for (int c = 0; c < 6; c++)
                                g.FillRectangle(w, 6 + c * 4, 7 + r * 4, 3, 3);
                        g.FillRectangle(w, 6, 21, 21, 3);
                        }
                    }
                }
                IntPtr handle=bmp.GetHicon();
                try { using(Icon icon=Icon.FromHandle(handle)) return (Icon)icon.Clone(); }
                finally { DestroyIcon(handle); }
                }
            }
            catch { return (Icon)SystemIcons.Application.Clone(); }
        }

        // ---------------------------------------------------------------- 定时 / 生命周期

        private int _artRevision;
        private void OnTick(object sender, EventArgs e)
        {
            int revision=System.Threading.Volatile.Read(ref ThemeImages.Revision);
            if(revision!=_artRevision)
            {
                _artRevision=revision;RefreshTheme();
                foreach(Form form in Application.OpenForms)if(form.Visible)form.Invalidate();
            }
            long previousTrend=LiveRate.TrendVersion;
            long previousApm = LiveRate.Apm;
            ActiveSession previousSession = ActivityMonitor.CurrentSession;
            LiveRate.Tick();
            if (ActivityMonitor.Tick()) MarkDirty();
            if (previousTrend != LiveRate.TrendVersion || previousApm != LiveRate.Apm || !object.ReferenceEquals(previousSession, ActivityMonitor.CurrentSession)) _dirtyUI = true;

            if (Store.Day != DateTime.Today)
            {
                Store.Save();
                Store.RollDay(DateTime.Today);
                _dirtySave = true;
                _dirtyUI = true;
            }
            if (_dirtyUI)
            {
                _dirtyUI = false;
                Invalidate();
            }
            if (_dirtySave && (DateTime.Now - _lastSave).TotalSeconds >= 3)
            {
                SaveNow();
            }
        }

        private void SaveNow()
        {
            _lastSave = DateTime.Now;
            _dirtySave = false;
            Store.Save();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            SaveNow();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if(_hud!=null){_hud.Close();_hud.Dispose();}
            if (_dashWait != null) { try { _dashWait.Unregister(null); } catch { } }
            if (_kbHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_kbHook); _kbHook = IntPtr.Zero; }
            if (_msHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_msHook); _msHook = IntPtr.Zero; }
            if (_timer != null) { _timer.Stop(); _timer.Dispose(); }
            if (_tray != null) { _tray.Visible = false; Icon icon=_tray.Icon;_tray.Dispose();if(icon!=null)icon.Dispose(); }
        }
    }
}




