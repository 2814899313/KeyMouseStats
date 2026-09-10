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
        /// <summary>按键时长(1.7.3 起采集):逐键聚合、8 档直方图与丢弃计数。</summary>
        public readonly Dictionary<int, KeyHold> Holds = new Dictionary<int, KeyHold>();
        public readonly long[] HoldBuckets = new long[HoldTracker.BucketCount];
        public long HoldCount, HoldDiscarded;
        public double HoldTotalMs, HoldMaxMs;

        public bool IsEmpty
        {
            get { return Keys == 0 && Clicks == 0 && Wheel == 0 && MovePx < 0.5 && MoveMeters == 0 && ActiveSeconds == 0 && IdleSeconds == 0 && Cross.Packets==0 && Cross.Clicks==0; }
        }

        /// <summary>合并另一份同日记录(多机合并的「相加」策略)。</summary>
        public void AddFrom(DayRecord other)
        {
            if (other == null) return;
            Keys += other.Keys; Clicks += other.Clicks; Left += other.Left; Right += other.Right;
            Middle += other.Middle; XButtons += other.XButtons; Wheel += other.Wheel;
            MovePx += other.MovePx; MoveMeters += other.MoveMeters;
            ActiveSeconds += other.ActiveSeconds; IdleSeconds += other.IdleSeconds;
            AppObservedSeconds += other.AppObservedSeconds; AppSwitches += other.AppSwitches;
            if (other.PeakApm > PeakApm) PeakApm = other.PeakApm;
            for (int i = 0; i < 24; i++)
            {
                HourKeys[i] += other.HourKeys[i];
                HourClicks[i] += other.HourClicks[i];
                ActiveHours[i] += other.ActiveHours[i];
            }
            AddCounts(KeyCounts, other.KeyCounts);
            AddCounts(PhysicalKeys, other.PhysicalKeys);
            AddCounts(PhysicalVks, other.PhysicalVks);
            foreach (KeyValuePair<string, long> combo in other.ComboCounts)
            {
                long value; ComboCounts.TryGetValue(combo.Key, out value);
                ComboCounts[combo.Key] = value + combo.Value;
            }
            foreach (ActiveSession session in other.Sessions) Sessions.Add(session.Copy());
            foreach (ActiveSession period in other.IdlePeriods) IdlePeriods.Add(period.Copy());
            foreach (AppUsage app in other.Apps.Values)
            {
                AppUsage existing;
                if (Apps.TryGetValue(app.Id, out existing))
                {
                    existing.Keys += app.Keys; existing.Clicks += app.Clicks;
                    existing.Wheel += app.Wheel; existing.ActiveSeconds += app.ActiveSeconds;
                    if (app.HasKeyGroups)
                    {
                        existing.HasKeyGroups = true;
                        for (int g = 0; g < existing.KeyGroups.Length && g < app.KeyGroups.Length; g++) existing.KeyGroups[g] += app.KeyGroups[g];
                    }
                }
                else Apps[app.Id] = app.Copy();
            }
            HoldCount += other.HoldCount;
            HoldDiscarded += other.HoldDiscarded;
            HoldTotalMs += other.HoldTotalMs;
            if (other.HoldMaxMs > HoldMaxMs) HoldMaxMs = other.HoldMaxMs;
            for (int i = 0; i < HoldBuckets.Length && i < other.HoldBuckets.Length; i++) HoldBuckets[i] += other.HoldBuckets[i];
            foreach (KeyValuePair<int, KeyHold> pair in other.Holds)
            {
                KeyHold hold;
                if (!Holds.TryGetValue(pair.Key, out hold)) { hold = new KeyHold(); Holds[pair.Key] = hold; }
                hold.Count += pair.Value.Count;
                hold.TotalMs += pair.Value.TotalMs;
                if (pair.Value.MaxMs > hold.MaxMs) hold.MaxMs = pair.Value.MaxMs;
            }
            Cross.AddFrom(other.Cross);
        }

        private static void AddCounts(Dictionary<int, long> target, Dictionary<int, long> source)
        {
            foreach (KeyValuePair<int, long> pair in source)
            {
                long value; target.TryGetValue(pair.Key, out value);
                target[pair.Key] = value + pair.Value;
            }
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
        /// <summary>保留天数;0 表示永久保留。超期数据先按月归档再移除。</summary>
        public static int KeepDays = 365;
        /// <summary>读取到的文件格式版本(缺失按 10 计)。</summary>
        public static int DetectedFormat = 10;
        /// <summary>文件由更新版本写入时为 true,此时只提示不阻止。</summary>
        public static bool FileFromNewerVersion;
        public const int FormatVersion = 11;
        /// <summary>按月归档(保留期之外的数据)。</summary>
        public static readonly Dictionary<string, MonthArchive> Archives = new Dictionary<string, MonthArchive>(StringComparer.Ordinal);
        private static string Dir = DefaultDirectory();
        private static string FilePath = Path.Combine(Dir, "stats.txt");

        private static string DefaultDirectory()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "键鼠统计");
        }

        /// <summary>
        /// 数据目录。仅供测试在首次读写之前重定向到自己的临时目录,产品代码不要修改。
        /// </summary>
        internal static string DataDirectory
        {
            get { return Dir; }
            set { Dir = value; FilePath = Path.Combine(value, "stats.txt"); }
        }

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
        /// <summary>备份轮转保留的份数。</summary>
        public static int BackupKeep = 7;

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

        /// <summary>解析结果。解析过程不触碰任何静态状态,便于导入、合并与校验。</summary>
        internal sealed class Parsed
        {
            public readonly Dictionary<DateTime, DayRecord> History = new Dictionary<DateTime, DayRecord>();
            public readonly List<string> AppRules = new List<string>();
            public Counters Total = new Counters();
            public string StoredDate = "", ShortcutModel;
            public DayRecord LegacyToday;
            public int PosX = -1, PosY = -1, ThemeId, KeyboardLayout, IdleThresholdSeconds = 60;
            public long AllTimePeakApm;
            public double MouseDpi;
            public int BackupKeep = 7;
            public string Wellbeing;
            public int KeepDays = 365, FormatVersion = 10;
            public bool HasKeepDays;
            public readonly Dictionary<string, MonthArchive> Archives = new Dictionary<string, MonthArchive>(StringComparer.Ordinal);
            public bool HasMouseDpi, HasIdleThreshold, HasBackupKeep;
        }

        private static Parsed Parse(string[] lines)
        {
            Parsed result = new Parsed();
            DayRecord cur = null;

            foreach (string raw in lines)
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
                        if (!result.History.ContainsKey(d))
                        {
                            DayRecord r = new DayRecord();
                            r.Date = d;
                            result.History[d] = r;
                        }
                        cur = result.History[d];
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
                        case "hold_v1": HoldCodec.Load(cur,val);break;
                        case "hold_key_v1": HoldCodec.LoadKeys(cur,val);break;
                        case "app_keys_v1": AppKeyCodec.Load(cur,val);break;
                    }
                }
                else
                {
                    switch (key)
                    {
                        case "date": result.StoredDate = val; break;
                        case "art_theme": result.ThemeId = ArtTheme.Validate((int)ParseL(val)); break;
                        case "keyboard_layout": result.KeyboardLayout = KeyboardHeat.Valid((int)ParseL(val)); break;
                        case "all_time_peak_apm": result.AllTimePeakApm = Math.Max(0, ParseL(val)); break;
                        case "app_rule": result.AppRules.Add(val); break;
                        case "wellbeing_v1": result.Wellbeing = val; break;
                        case "keep_days":
                            int keepDays = (int)ParseL(val);
                            result.KeepDays = keepDays <= 0 ? 0 : Math.Max(30, Math.Min(3650, keepDays));
                            result.HasKeepDays = true;
                            break;
                        case "format": result.FormatVersion = (int)ParseL(val); break;
                        case "archive_v1": MonthArchive.AddTo(result.Archives, val); break;
                        case "backup_keep":
                            int keep = (int)ParseL(val);
                            result.BackupKeep = keep >= 1 && keep <= 60 ? keep : 7;
                            result.HasBackupKeep = true;
                            break;
                        case "shortcut_model_v1": result.ShortcutModel = val;break;
                        case "idle_threshold":
                            long seconds = ParseL(val);
                            result.IdleThresholdSeconds = seconds >= 10 && seconds <= 3600 ? (int)seconds : 60;
                            result.HasIdleThreshold = true;
                            break;
                        case "pos_x": result.PosX = (int)ParseL(val); break;
                        case "pos_y": result.PosY = (int)ParseL(val); break;
                        // v1 total_*
                        case "total_keys": result.Total.Keys = ParseL(val); break;
                        case "total_clicks": result.Total.Clicks = ParseL(val); break;
                        case "total_left": result.Total.Left = ParseL(val); break;
                        case "total_right": result.Total.Right = ParseL(val); break;
                        case "total_middle": result.Total.Middle = ParseL(val); break;
                        case "total_xbuttons": result.Total.XButtons = ParseL(val); break;
                        case "total_wheel": result.Total.Wheel = ParseL(val); break;
                        case "total_move": result.Total.MovePx = ParseD(val); break;
                        case "total_move_m": result.Total.MoveMeters = NonNegative(ParseD(val)); break;
                        case "mouse_dpi":
                            double dpi = ParseD(val);
                            result.MouseDpi = MouseDistance.ValidDpi(dpi) ? dpi : 0;
                            result.HasMouseDpi = true;
                            break;
                        // v1 today_* → 暂存,稍后并入分节
                        case "today_keys":
                            if (result.LegacyToday == null) result.LegacyToday = new DayRecord();
                            result.LegacyToday.Keys = ParseL(val); break;
                        case "today_clicks":
                            if (result.LegacyToday == null) result.LegacyToday = new DayRecord();
                            result.LegacyToday.Clicks = ParseL(val); break;
                        case "today_left":
                            if (result.LegacyToday == null) result.LegacyToday = new DayRecord();
                            result.LegacyToday.Left = ParseL(val); break;
                        case "today_right":
                            if (result.LegacyToday == null) result.LegacyToday = new DayRecord();
                            result.LegacyToday.Right = ParseL(val); break;
                        case "today_middle":
                            if (result.LegacyToday == null) result.LegacyToday = new DayRecord();
                            result.LegacyToday.Middle = ParseL(val); break;
                        case "today_xbuttons":
                            if (result.LegacyToday == null) result.LegacyToday = new DayRecord();
                            result.LegacyToday.XButtons = ParseL(val); break;
                        case "today_wheel":
                            if (result.LegacyToday == null) result.LegacyToday = new DayRecord();
                            result.LegacyToday.Wheel = ParseL(val); break;
                        case "today_move":
                            if (result.LegacyToday == null) result.LegacyToday = new DayRecord();
                            result.LegacyToday.MovePx = ParseD(val); break;
                    }
                }
            }
            return result;
        }

        /// <summary>从文本解析,供导入与校验使用。</summary>
        internal static Parsed ParseText(string text)
        {
            string normalized = (text ?? "").Replace("\r\n", "\n").Replace("\r", "\n");
            return Parse(normalized.Split('\n'));
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                ApplyParsed(Parse(File.ReadAllLines(FilePath, Encoding.UTF8)), true);
            }
            catch { }
        }

        /// <summary>把解析结果写入静态状态。applySettings 为 false 时只接管日记录(导入合并用)。</summary>
        internal static void ApplyParsed(Parsed parsed, bool applySettings)
        {
            History.Clear();
            foreach (KeyValuePair<DateTime, DayRecord> day in parsed.History) History[day.Key] = day.Value;
            Total = parsed.Total;
            DetectedFormat = parsed.FormatVersion;
            FileFromNewerVersion = parsed.FormatVersion > FormatVersion;
            Archives.Clear();
            foreach (KeyValuePair<string, MonthArchive> archive in parsed.Archives) Archives[archive.Key] = archive.Value;
            if (applySettings)
            {
                ThemeId = parsed.ThemeId;
                KeyboardLayout = parsed.KeyboardLayout;
                AllTimePeakApm = parsed.AllTimePeakApm;
                if (parsed.HasMouseDpi) MouseDpi = parsed.MouseDpi;
                if (parsed.HasIdleThreshold) IdleThresholdSeconds = parsed.IdleThresholdSeconds;
                if (parsed.HasBackupKeep) BackupKeep = parsed.BackupKeep;
                if (!string.IsNullOrEmpty(parsed.Wellbeing)) WellbeingSettings.Load(parsed.Wellbeing);
                if (parsed.HasKeepDays) KeepDays = parsed.KeepDays;
                PosX = parsed.PosX; PosY = parsed.PosY;
                AppActivity.Rules.Clear();
                foreach (string rule in parsed.AppRules) AppActivity.LoadRule(rule);
                if (!string.IsNullOrEmpty(parsed.ShortcutModel)) ShortcutSavings.LoadModel(parsed.ShortcutModel);
            }

            // v1 数据迁移:把扁平 today_* 并入对应日期的分节(已存在则合并)
            DateTime legacyDay;
            if (parsed.LegacyToday != null && DateTime.TryParseExact(parsed.StoredDate, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out legacyDay))
            {
                parsed.LegacyToday.Date = legacyDay;
                DayRecord existing;
                if (History.TryGetValue(legacyDay, out existing))
                {
                    existing.Keys += parsed.LegacyToday.Keys;
                    existing.Clicks += parsed.LegacyToday.Clicks;
                    existing.Left += parsed.LegacyToday.Left;
                    existing.Right += parsed.LegacyToday.Right;
                    existing.Middle += parsed.LegacyToday.Middle;
                    existing.XButtons += parsed.LegacyToday.XButtons;
                    existing.Wheel += parsed.LegacyToday.Wheel;
                    existing.MovePx += parsed.LegacyToday.MovePx;
                }
                else if (!parsed.LegacyToday.IsEmpty)
                {
                    History[legacyDay] = parsed.LegacyToday;
                }
            }

            DailySignal.Clear();
            foreach(DayRecord record in History.Values)ActivityMonitor.RemoveLegacyPhantomSessions(record);
            RollDay(DateTime.Today);
            Prune();
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
            if (KeepDays <= 0) return;   // 永久保留,不归档也不移除
            DateTime oldest = DateTime.Today.AddDays(-(KeepDays - 1));
            List<DateTime> remove = new List<DateTime>();
            foreach (KeyValuePair<DateTime, DayRecord> kv in History)
            {
                if (kv.Key < oldest) remove.Add(kv.Key);
            }
            foreach (DateTime d in remove)
            {
                // 超期数据先折进月度归档,再移除明细,避免一年前的记录无声消失。
                MonthArchive.Fold(Archives, History[d]);
                History.Remove(d);
            }
        }

        private static double NonNegative(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) || value < 0 ? 0 : value;
        }

        // ---------------------------------------------------------------- 保存
        // 序列化在调用线程完成(它读取的是活动中的记录对象),落盘可以交给后台线程。
        // 每个快照带一个递增序号:旧快照永远不能覆盖新快照,后台写入也不会与同步写入交错。
        private static readonly object WriteGate = new object(), FileGate = new object();
        private static string _pendingText;
        private static long _pendingSequence, _saveSequence, _writtenSequence;
        private static bool _writerActive;

        /// <summary>同步保存:返回时文件已落盘。设置对话框与测试使用。</summary>
        public static void Save()
        {
            string text = Prepare();
            if (text == null) return;
            long sequence;
            lock (WriteGate)
            {
                sequence = ++_saveSequence;
                _pendingText = null;   // 同步写入的状态不旧于任何待写快照
            }
            WriteSnapshot(text, sequence);
        }

        /// <summary>异步保存:序列化仍在调用线程完成,写盘交给后台线程,不阻塞输入处理。</summary>
        public static void SaveAsync()
        {
            string text = Prepare();
            if (text == null) return;
            lock (WriteGate)
            {
                _pendingText = text;
                _pendingSequence = ++_saveSequence;
                if (_writerActive) return;   // 已有写入线程会取走这份最新快照
                _writerActive = true;
            }
            ThreadPool.QueueUserWorkItem(WriteWorker);
        }

        /// <summary>等待后台写入完成,超时返回 false。退出前调用。</summary>
        public static bool Flush(int timeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(0, timeoutMs));
            for (; ; )
            {
                lock (WriteGate) { if (_pendingText == null && !_writerActive) return true; }
                if (DateTime.UtcNow > deadline) return false;
                Thread.Sleep(5);
            }
        }

        private static string Prepare()
        {
            try { Prune(); return Serialize(); }
            catch { return null; }
        }

        private static void WriteWorker(object state)
        {
            for (; ; )
            {
                string text;
                long sequence;
                lock (WriteGate)
                {
                    text = _pendingText;
                    sequence = _pendingSequence;
                    _pendingText = null;
                    if (text == null) { _writerActive = false; return; }
                }
                WriteSnapshot(text, sequence);
            }
        }

        private static void WriteSnapshot(string text, long sequence)
        {
            // 状态锁只保护计数与待写快照;真正写盘用独立的 FileGate,
            // 这样 UI 线程排队保存时不会等在前一次磁盘写入后面。
            lock (FileGate)
            {
                lock (WriteGate)
                {
                    if (sequence <= _writtenSequence) return;   // 更新的快照已经落盘
                    _writtenSequence = sequence;
                }
                try
                {
                    if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
                    string tmp = FilePath + ".tmp";
                    File.WriteAllText(tmp, text, Encoding.UTF8);
                    if (File.Exists(FilePath)) File.Replace(tmp, FilePath, FilePath + ".bak");
                    else File.Move(tmp, FilePath);
                }
                catch { }
            }
        }

        /// <summary>导出用的文本,与落盘内容一致;失败返回 null。</summary>
        internal static string ExportPayload()
        {
            return Prepare();
        }

        private static string Serialize()
        {
            StringBuilder sb = new StringBuilder();
                sb.AppendLine("# 键鼠统计数据文件 v11 / 正式版 " + BuildInfo.Version);
                sb.AppendLine("format=11");
                sb.AppendLine("shortcut_model_v1="+ShortcutSavings.EncodeModel());
                sb.AppendLine("idle_threshold=" + IdleThresholdSeconds.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("backup_keep=" + BackupKeep.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("wellbeing_v1=" + WellbeingSettings.Encode());
                sb.AppendLine("keep_days=" + KeepDays.ToString(CultureInfo.InvariantCulture));
                foreach (MonthArchive archive in Archives.Values) sb.AppendLine("archive_v1=" + MonthArchive.Encode(archive));
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
                    foreach(string holdLine in HoldCodec.EncodeAll(r))sb.AppendLine(holdLine);
                    foreach(string appKeyLine in AppKeyCodec.EncodeAll(r))sb.AppendLine(appKeyLine);
                    CrossTelemetry.Save(sb,r);
                }

                return sb.ToString();
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
        private int _mode;   // 0 今日 / 1 目标 / 2 累计
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
            // 每天首次启动留一份带日期的备份,并按保留份数清理旧备份。
            try { DataManagement.EnsureDailyBackup(); } catch { }

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            BackColor = ArtTheme.Current.Card;
            Opacity = 0.96;
            TopMost = false;

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
            SaveNow(true);   // 用户刚改完 DPI/校准,等它真正落盘
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
                        HoldTracker.Release(ShortcutTracker.Normalize((int)released.vkCode, released.scanCode, released.flags), System.Diagnostics.Stopwatch.GetTimestamp());
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
                                HoldTracker.Press(t, vk, System.Diagnostics.Stopwatch.GetTimestamp());
                                AppActivity.Record(t, foreground, 0, KeySemantics.Group(vk));
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
                if(EuroTruckArt.Active){GraphicsState artState=g.Save();g.SetClip(path);EuroTruckArt.DrawWidget(g,new RectangleF(0,0,ClientSize.Width,ClientSize.Height));g.Restore(artState);}
                else if(WuxiaArt.Active){GraphicsState artState=g.Save();g.SetClip(path);WuxiaArt.DrawWidget(g,new RectangleF(0,0,ClientSize.Width,ClientSize.Height));g.Restore(artState);}
                if(ThemeArt.Active)
                {
                    GraphicsState state=g.Save();g.SetClip(path);ThemeArt.Sticker(g,ActivityMonitor.CurrentSession==null?3:0,new RectangleF(107*s,3*s,30*s,30*s));g.Restore(state);
                }
                using (Pen edge = new Pen(ArtTheme.Current.Line)) g.DrawPath(edge, path);
            }

            using (SolidBrush b = new SolidBrush(ArtTheme.Current.Text))
                g.DrawString("键鼠统计", _fTitle, b, 16 * s, 9 * s);

            string modeName = _mode == 2 ? "累计" : _mode == 1 ? "目标" : "今日";
            string modeText = string.Format("{0} · {1:MM-dd}", modeName, Store.Day);
            RectangleF modeRect = new RectangleF(0, 13 * s, ClientSize.Width - 16 * s, 20 * s);
            using (SolidBrush b = new SolidBrush(ArtTheme.Current.Muted))
                g.DrawString(modeText, _fMode, b, modeRect, _rightAlign);

            using (Pen pen = new Pen(ArtTheme.Current.Line))
                g.DrawLine(pen, 14 * s, 36 * s, ClientSize.Width - 14 * s, 36 * s);

            if (_mode == 1) { PaintGoal(g); return; }
            DayRecord c = _mode == 2 ? ToDayRecord(Store.Total) : Store.Today;
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

        /// <summary>目标模式:进度环 + 已用 / 目标 / 剩余。</summary>
        private void PaintGoal(Graphics g)
        {
            float s = _s;
            ArtTheme theme = ArtTheme.Current;
            DayRecord day = Store.Today;
            if (!WellbeingSettings.GoalEnabled)
            {
                using (SolidBrush text = new SolidBrush(theme.Muted))
                using (StringFormat center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                {
                    g.DrawString("未设置每日目标", _fMode, text, new RectangleF(16 * s, 78 * s, ClientSize.Width - 32 * s, 24 * s), center);
                    g.DrawString("右键小组件 → 目标 / 提醒…", _fMode, text, new RectangleF(16 * s, 108 * s, ClientSize.Width - 32 * s, 24 * s), center);
                }
                return;
            }

            double progress = WellbeingSettings.Progress(day);
            if (!RangeMath.IsNumber(progress)) progress = 0;
            bool done = progress >= 1;
            float size = 88 * s;
            RectangleF ring = new RectangleF(ClientSize.Width / 2f - size / 2f, 44 * s, size, size);
            using (Pen track = new Pen(Color.FromArgb(70, theme.Line), 9 * s)) g.DrawArc(track, ring, -90, 360);
            using (Pen arc = new Pen(done ? theme.Green : theme.Accent, 9 * s))
            {
                arc.StartCap = LineCap.Round;
                arc.EndCap = LineCap.Round;
                g.DrawArc(arc, ring, -90, (float)(360 * Math.Min(1, progress)));
            }
            using (Font percent = new Font("Segoe UI", 20 * s, FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush text = new SolidBrush(theme.Text))
            using (StringFormat center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString((progress * 100).ToString("0") + "%", percent, text, ring, center);

            using (SolidBrush text = new SolidBrush(theme.Muted))
            using (StringFormat center = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            {
                g.DrawString(WellbeingSettings.UsedText(day) + " / " + WellbeingSettings.TargetText, _fMode, text,
                    new RectangleF(12 * s, 140 * s, ClientSize.Width - 24 * s, 20 * s), center);
                g.DrawString(WellbeingSettings.RemainingText(day), _fMode, text,
                    new RectangleF(12 * s, 160 * s, ClientSize.Width - 24 * s, 20 * s), center);
                g.DrawString("目标：" + WellbeingSettings.GoalName + " · 右键修改", _fMode, text,
                    new RectangleF(12 * s, 182 * s, ClientSize.Width - 24 * s, 20 * s), center);
            }
        }

        /// <summary>目标达成、连续使用与每日摘要的提示。三者都默认关闭。</summary>
        private void CheckWellbeing()
        {
            try
            {
                DateTime now = DateTime.Now;
                string message = Wellbeing.GoalMessage(now, Store.Today);
                if (message == null) message = Wellbeing.ContinuousMessage(now, ActivityMonitor.CurrentSession);
                if (message == null) message = Wellbeing.SummaryMessage(now);
                if (message == null) return;
                if (_tray != null) _tray.ShowBalloonTip(8000, "键鼠统计", message, ToolTipIcon.Info);
                MarkDirty();
            }
            catch { }
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
                    _mode = (_mode + 1) % 3;
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
            _miTopMost.Checked = false;

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
                new ToolStripMenuItem("数据管理(备份 / 导入 / 校验)...", null, delegate { using (DataSettingsDialog dialog = new DataSettingsDialog()) dialog.ShowDialog(this); }),
                new ToolStripMenuItem("目标 / 提醒...", null, delegate { using (WellbeingSettingsDialog dialog = new WellbeingSettingsDialog()) dialog.ShowDialog(this); }),
                _miShow, hudMenu, sep1, _miTopMost, _miClickThrough, _miAutoStart,
                sep2, miResetToday, miResetAll, sep3, miExit });
        }

        private void BuildTray()
        {
            _tray = new NotifyIcon();
            _tray.Icon = MakeIcon();
            _tray.Text = "键鼠统计 " + BuildInfo.Version + " 正式版";
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
            CheckWellbeing();
            if (ActivityMonitor.Tick()) MarkDirty();
            if (previousTrend != LiveRate.TrendVersion || previousApm != LiveRate.Apm || !object.ReferenceEquals(previousSession, ActivityMonitor.CurrentSession)) _dirtyUI = true;

            if (Store.Day != DateTime.Today)
            {
                Store.SaveAsync();   // 序列化已捕获昨天,滚动日期不会影响它
                Store.RollDay(DateTime.Today);
                _dirtySave = true;
                _dirtyUI = true;
            }
            if (_dirtyUI)
            {
                _dirtyUI = false;
                Invalidate();
            }
            // 保存间隔决定磁盘写入量:每次保存都会重写整份历史(含 .bak)。
            // 30 秒意味着崩溃最多丢失 30 秒计数,写入量约为原来每 3 秒保存的十分之一。
            if (_dirtySave && (DateTime.Now - _lastSave).TotalSeconds >= SaveIntervalSeconds)
            {
                SaveNow(false);
            }
        }

        private const int SaveIntervalSeconds = 30;

        private void SaveNow(bool waitForDisk)
        {
            _lastSave = DateTime.Now;
            _dirtySave = false;
            Store.SaveAsync();
            if (waitForDisk) Store.Flush(3000);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            SaveNow(true);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            HoldTracker.DiscardPending();   // 退出时未抬起的按键不计入时长
            if(_hud!=null){_hud.Close();_hud.Dispose();}
            if (_dashWait != null) { try { _dashWait.Unregister(null); } catch { } }
            if (_kbHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_kbHook); _kbHook = IntPtr.Zero; }
            if (_msHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_msHook); _msHook = IntPtr.Zero; }
            if (_timer != null) { _timer.Stop(); _timer.Dispose(); }
            if (_tray != null) { _tray.Visible = false; Icon icon=_tray.Icon;_tray.Dispose();if(icon!=null)icon.Dispose(); }
        }
    }
}




