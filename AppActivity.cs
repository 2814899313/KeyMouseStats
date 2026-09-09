using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace KeyMouseStats
{
    internal sealed class AppUsage
    {
        public string ProcessPath = "";
        public string Title = "";
        public long Keys, Clicks, Wheel;
        public double ActiveSeconds;
        public string Id { get { return ProcessPath.ToLowerInvariant() + "\n" + Title; } }
        public AppUsage Copy()
        {
            AppUsage copy = new AppUsage();
            copy.ProcessPath = ProcessPath; copy.Title = Title;
            copy.Keys = Keys; copy.Clicks = Clicks; copy.Wheel = Wheel; copy.ActiveSeconds = ActiveSeconds;
            return copy;
        }
        public string AppName
        {
            get { return ProcessPath.Length == 0 ? "未识别应用" : Path.GetFileName(ProcessPath); }
        }
    }

    internal static class ForegroundApp
    {
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref uint size);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

        private static IntPtr _lastWindow;
        private static uint _lastPid;
        private static string _lastPath = "";
        private static DateTime _expires;
        private static readonly StringBuilder TitleBuffer = new StringBuilder(513);

        // Capture on the same UI thread as the low-level hooks. Never read files or enumerate processes here.
        public static AppUsage Capture()
        {
            try
            {
                IntPtr window = GetForegroundWindow();
                uint pid;
                if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out pid) == 0 || pid == 0)
                    return new AppUsage { Title = "无法识别的前台窗口" };
                if (window != _lastWindow || pid != _lastPid || DateTime.UtcNow >= _expires)
                {
                    _lastWindow = window; _lastPid = pid; _lastPath = "";
                    IntPtr process = OpenProcess(0x1000, false, pid); // QUERY_LIMITED_INFORMATION
                    if (process != IntPtr.Zero)
                    {
                        try
                        {
                            StringBuilder path = new StringBuilder(32768);
                            uint size = (uint)path.Capacity;
                            if (QueryFullProcessImageName(process, 0, path, ref size)) _lastPath = path.ToString();
                        }
                        finally { CloseHandle(process); }
                    }
                    _expires = DateTime.UtcNow.AddSeconds(2);
                }
                TitleBuffer.Length = 0;
                GetWindowText(window, TitleBuffer, TitleBuffer.Capacity);
                uint verifiedPid;
                if (GetForegroundWindow() != window || GetWindowThreadProcessId(window, out verifiedPid) == 0 || verifiedPid != pid)
                    return new AppUsage { Title = "窗口切换中 / 无法归因" };
                string title = TitleBuffer.Length == 0 ? "无标题窗口" : TitleBuffer.ToString();
                return new AppUsage { ProcessPath = _lastPath, Title = title };
            }
            catch { return new AppUsage { Title = "无法识别的前台窗口" }; }
        }
    }

    internal static class AppActivity
    {
        // Append categories to preserve numeric IDs in existing manual rules.
        public static readonly string[] Categories = { "编程", "游戏", "视频", "其他", "未分类", "办公" };
        public static readonly Dictionary<string, int> Rules = new Dictionary<string, int>(StringComparer.Ordinal);
        public const string UnattributedId = "__unattributed__";
        public const string OverflowTitle = "其他窗口（当日标题数量已达上限）";

        public static string AppRuleKey(string path) { return "app:" + path.ToLowerInvariant(); }
        public static string WindowRuleKey(AppUsage app) { return "window:" + app.Id; }
        public static string TitleRuleKey(string path, string keyword)
        { return "title:" + path.ToLowerInvariant() + "\n" + keyword.Trim().ToLowerInvariant(); }

        internal sealed class Classification
        {
            public int Category;
            public string Source, Reason;
            public Classification(int category, string source, string reason)
            { Category = category; Source = source; Reason = reason; }
        }

        public static int Category(AppUsage app)
        { return Explain(app).Category; }

        public static Classification Explain(AppUsage app)
        {
            int category;
            if (Rules.TryGetValue(WindowRuleKey(app), out category)) return new Classification(category, "窗口规则", "手动指定此精确窗口标题");
            string prefix = "title:" + app.ProcessPath.ToLowerInvariant() + "\n";
            string best = null; int bestCategory = 4;
            foreach (KeyValuePair<string, int> rule in Rules)
            {
                if (!rule.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                string keyword = rule.Key.Substring(prefix.Length);
                if (keyword.Length == 0 || app.Title.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (best == null || keyword.Length > best.Length || keyword.Length == best.Length && string.CompareOrdinal(keyword, best) < 0)
                { best = keyword; bestCategory = rule.Value; }
            }
            if (best != null) return new Classification(bestCategory, "关键词规则", "标题包含“" + best + "”（限定此应用）");
            if (Rules.TryGetValue(AppRuleKey(app.ProcessPath), out category)) return new Classification(category, "应用规则", "手动指定此应用的默认用途");
            string site;
            int siteCategory = BrowserCategory(app, out site);
            if (siteCategory != 4) return new Classification(siteCategory, "站点推测", "标题显示“" + site + "”；仅推测，可用规则纠正");
            category = DefaultCategory(app);
            return new Classification(category, category == 4 ? "待标注" : "应用预设",
                category == 4 ? "没有匹配规则；可设置应用默认用途或标题关键词" : "根据应用名 / 路径预设，可按实际用途调整");
        }

        private static int BrowserCategory(AppUsage app, out string site)
        {
            site = "";
            if (!Contains("chrome.exe|msedge.exe|firefox.exe|brave.exe|opera.exe|vivaldi.exe", app.AppName.ToLowerInvariant())) return 4;
            string title = app.Title.ToLowerInvariant().Trim();
            foreach (string suffix in new string[] { " - google chrome", " - microsoft edge", " — mozilla firefox", " - brave", " - opera", " - vivaldi" })
                if (title.EndsWith(suffix, StringComparison.Ordinal)) { title = title.Substring(0, title.Length - suffix.Length).Trim(); break; }
            string[] markers = { "github", "stack overflow", "microsoft learn", "mdn web docs", "youtube", "哔哩哔哩_bilibili", "bilibili", "腾讯视频", "爱奇艺" };
            for (int i = 0; i < markers.Length; i++)
            {
                string marker = markers[i];
                if (title == marker || title.EndsWith(" - " + marker, StringComparison.Ordinal)
                    || title.EndsWith(" · " + marker, StringComparison.Ordinal) || title.EndsWith(" | " + marker, StringComparison.Ordinal)
                    || title.EndsWith("_" + marker, StringComparison.Ordinal))
                { site = marker; return i < 4 ? 0 : 2; }
            }
            return 4;
        }

        private static int DefaultCategory(AppUsage app)
        {
            string name = app.AppName.ToLowerInvariant();
            string path = app.ProcessPath.Replace('/', '\\').ToLowerInvariant();
            // Codex's Windows package uses ChatGPT.exe as its executable name.
            if (name == "chatgpt.exe" && path.IndexOf("\\openai.codex_", StringComparison.Ordinal) >= 0) return 0;
            if (Contains("codex.exe|antigravity.exe|trae.exe|windsurf.exe|sublime_text.exe|eclipse.exe|clion64.exe|goland64.exe|datagrip64.exe|rstudio.exe|windowsterminal.exe|powershell.exe|pwsh.exe|cmd.exe", name)) return 0;
            if (Contains("code.exe|cursor.exe|devenv.exe|pycharm64.exe|idea64.exe|webstorm64.exe|rider64.exe|notepad++.exe", name)) return 0;
            if (Contains("cs2.exe|dota2.exe|league of legends.exe|valorant-win64-shipping.exe|overwatch.exe|genshinimpact.exe|starrail.exe", name)) return 1;
            if (Contains("vlc.exe|potplayer64.exe|potplayer.exe|mpv.exe|bilibili.exe", name)) return 2;
            if (Contains("wps.exe|et.exe|wpp.exe|wpspdf.exe|winword.exe|excel.exe|powerpnt.exe|onenote.exe|outlook.exe|acrobat.exe|acrord32.exe|notion.exe|obsidian.exe|zotero.exe|opensquilla.exe|chatgpt.exe|claude.exe", name)) return 5;
            if (Contains("explorer.exe|taskmgr.exe|systemsettings.exe|searchhost.exe|startmenuexperiencehost.exe|shellexperiencehost.exe|applicationframehost.exe|wechat.exe|weixin.exe|qq.exe|dingtalk.exe|feishu.exe|discord.exe|telegram.exe|7zfm.exe|winrar.exe", name)) return 3;
            if (name == "键鼠统计.exe" || name == "keymousestats.exe"
                || name.StartsWith("keymousestats.", StringComparison.Ordinal) && name.EndsWith(".exe", StringComparison.Ordinal)) return 3;
            return 4;
        }
        private static bool Contains(string choices, string name)
        {
            foreach (string choice in choices.Split('|')) if (choice == name) return true;
            return false;
        }

        public static void Record(DayRecord day, AppUsage snapshot, int kind)
        {
            AppUsage record = EnsureRecord(day, snapshot);
            if (kind == 0) record.Keys++;
            else if (kind == 1) record.Clicks++;
            else record.Wheel++;
        }
        public static AppUsage EnsureRecord(DayRecord day, AppUsage snapshot)
        {
            string id = snapshot.Id;
            AppUsage record;
            if (!day.Apps.TryGetValue(id, out record))
            {
                // Prevent rapidly changing browser/document titles growing the data without bound.
                if (day.Apps.Count >= 2000)
                {
                    snapshot = new AppUsage { Title = OverflowTitle };
                    id = snapshot.Id;
                }
                if (!day.Apps.TryGetValue(id, out record))
                {
                    record = new AppUsage { ProcessPath = snapshot.ProcessPath, Title = snapshot.Title };
                    day.Apps[id] = record;
                }
            }
            return record;
        }

        public static string Encode(string value) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)); }
        public static string Decode(string value) { return Encoding.UTF8.GetString(Convert.FromBase64String(value)); }
        public static string Serialize(AppUsage app)
        {
            return Encode(app.ProcessPath) + "|" + Encode(app.Title) + "|" + app.Keys.ToString(CultureInfo.InvariantCulture)
                + "|" + app.Clicks.ToString(CultureInfo.InvariantCulture) + "|" + app.Wheel.ToString(CultureInfo.InvariantCulture)
                + "|" + app.ActiveSeconds.ToString("R", CultureInfo.InvariantCulture);
        }
        public static void LoadRecord(DayRecord day, string value)
        {
            try
            {
                string[] parts = value.Split('|');
                if (parts.Length != 5 && parts.Length != 6) return;
                long keys, clicks, wheel;
                if (!long.TryParse(parts[2], out keys) || !long.TryParse(parts[3], out clicks)
                    || !long.TryParse(parts[4], out wheel) || keys < 0 || clicks < 0 || wheel < 0) return;
                AppUsage app = new AppUsage { ProcessPath = Decode(parts[0]), Title = Decode(parts[1]), Keys = keys, Clicks = clicks, Wheel = wheel };
                if (parts.Length == 6) app.ActiveSeconds = ActivityMonitor.ParseSeconds(parts[5]);
                day.Apps[app.Id] = app;
            }
            catch (FormatException) { }
        }
        public static void LoadRule(string value)
        {
            try
            {
                string[] parts = value.Split('|');
                int category;
                if (parts.Length == 2 && int.TryParse(parts[1], out category) && category >= 0 && category < Categories.Length)
                    Rules[Decode(parts[0])] = category;
            }
            catch (FormatException) { }
        }

        internal sealed class Row
        {
            public string Id, Name, Detail, ProcessPath;
            public string Source, Reason;
            public AppUsage Window;
            public long Keys, Clicks, Wheel;
            public double ActiveSeconds;
            public int Category;
            public bool Legacy;
        }

        public static List<Row> Aggregate(IEnumerable<DayRecord> days, bool byWindow, out long[] categoryKeys)
        {
            categoryKeys = new long[Categories.Length];
            Dictionary<string, Row> rows = new Dictionary<string, Row>(StringComparer.Ordinal);
            long missingKeys = 0, missingClicks = 0, missingWheel = 0;
            foreach (DayRecord day in days)
            {
                long keys = 0, clicks = 0, wheel = 0;
                foreach (AppUsage app in day.Apps.Values)
                {
                    Classification classification = Explain(app);
                    int category = classification.Category;
                    categoryKeys[category] += app.Keys;
                    keys += app.Keys; clicks += app.Clicks; wheel += app.Wheel;
                    string id = byWindow ? app.Id : app.ProcessPath.ToLowerInvariant();
                    Row row;
                    if (!rows.TryGetValue(id, out row))
                    {
                        row = new Row { Id = id, Name = byWindow ? app.Title : app.AppName,
                            Detail = byWindow ? app.AppName : app.ProcessPath, ProcessPath = app.ProcessPath,
                            Window = byWindow ? app : null, Category = category, Source = classification.Source, Reason = classification.Reason };
                        rows[id] = row;
                    }
                    if (row.Category != category) { row.Category = -1; row.Source = "按窗口细分"; row.Reason = "此应用包含多种用途，切换“按窗口”查看"; }
                    else if (row.Reason != classification.Reason) { row.Source = "多条规则"; row.Reason = "多个窗口经不同规则归入同一用途，切换“按窗口”查看"; }
                    row.Keys += app.Keys; row.Clicks += app.Clicks; row.Wheel += app.Wheel;
                    row.ActiveSeconds += app.ActiveSeconds;
                }
                missingKeys += Math.Max(0, day.Keys - keys);
                missingClicks += Math.Max(0, day.Clicks - clicks);
                missingWheel += Math.Max(0, day.Wheel - wheel);
            }
            if (missingKeys > 0 || missingClicks > 0 || missingWheel > 0)
                rows[UnattributedId] = new Row { Id = UnattributedId, Name = "旧数据 / 未归因",
                    Detail = "启用应用统计前的记录，无法追溯用途", Category = 4, Legacy = true, Source = "无法追溯", Reason = "历史记录未采集前台窗口，不能补分类",
                    Keys = missingKeys, Clicks = missingClicks, Wheel = missingWheel };
            categoryKeys[4] += missingKeys;
            List<Row> result = new List<Row>(rows.Values);
            result.Sort(delegate(Row a, Row b)
            {
                int order = b.Keys.CompareTo(a.Keys);
                if (order == 0) order = b.Clicks.CompareTo(a.Clicks);
                if (order == 0) order = b.Wheel.CompareTo(a.Wheel);
                return order == 0 ? string.CompareOrdinal(a.Id, b.Id) : order;
            });
            return result;
        }

        public static string CsvCell(string text)
        {
            text = text ?? "";
            // Window titles are external input: avoid spreadsheet formula execution.
            string trimmed = text.TrimStart();
            if (trimmed.Length > 0 && "=+-@".IndexOf(trimmed[0]) >= 0) text = "'" + text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        public static string Export(IEnumerable<DayRecord> days)
        {
            StringBuilder csv = new StringBuilder("日期,应用,窗口标题,进程路径,分类,击键,点击,滚轮,分类来源,分类依据,观测活跃秒\r\n");
            List<DayRecord> sorted = new List<DayRecord>(days);
            sorted.Sort(delegate(DayRecord a, DayRecord b) { return a.Date.CompareTo(b.Date); });
            foreach (DayRecord day in sorted)
            {
                long[] ignored;
                foreach (Row row in Aggregate(new DayRecord[] { day }, true, out ignored))
                {
                    csv.Append(day.Date.ToString("yyyy-MM-dd")).Append(',');
                    csv.Append(CsvCell(row.Legacy ? row.Name : row.Window.AppName)).Append(',');
                    csv.Append(CsvCell(row.Legacy ? "" : row.Window.Title)).Append(',');
                    csv.Append(CsvCell(row.ProcessPath)).Append(',');
                    csv.Append(CsvCell(Categories[row.Category])).Append(',');
                    csv.Append(row.Keys).Append(',').Append(row.Clicks).Append(',').Append(row.Wheel).Append(',');
                    csv.Append(CsvCell(row.Source)).Append(',').Append(CsvCell(row.Reason)).Append(',');
                    csv.AppendLine(row.Legacy || day.AppObservedSeconds == 0 ? "" : row.ActiveSeconds.ToString("0.###", CultureInfo.InvariantCulture));
                }
            }
            return csv.ToString();
        }
    }
}

