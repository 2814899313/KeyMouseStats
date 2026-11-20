using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace KeyMouseStats
{
    /// <summary>C# 5 / .NET 4.0 的 ConcurrentDictionary 只有 TryRemove;保留旧调用点的 Remove(key) 写法。</summary>
    internal static class ConcurrentDictionaryCompat
    {
        internal static bool Remove<TKey, TValue>(this ConcurrentDictionary<TKey, TValue> dictionary, TKey key)
        {
            TValue value;
            return dictionary.TryRemove(key, out value);
        }
    }

    internal sealed class AppUsage
    {
        public string ProcessPath = "";
        public string Title = "";
        public long Keys, Clicks, Wheel;
        public double ActiveSeconds;
        /// <summary>六类键位分组计数(1.7.3 起采集);HasKeyGroups 区分「全为零」与「旧记录没有该维度」。</summary>
        public readonly long[] KeyGroups = new long[6];
        public bool HasKeyGroups;
        public string Id { get { return ProcessPath.ToLowerInvariant() + "\n" + Title; } }
        public AppUsage Copy()
        {
            AppUsage copy = new AppUsage();
            copy.ProcessPath = ProcessPath; copy.Title = Title;
            copy.Keys = Keys; copy.Clicks = Clicks; copy.Wheel = Wheel; copy.ActiveSeconds = ActiveSeconds;
            copy.HasKeyGroups = HasKeyGroups;
            for (int i = 0; i < KeyGroups.Length; i++) copy.KeyGroups[i] = KeyGroups[i];
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
        // 窗口标题缓存:同一个窗口 1 秒内只跨进程取一次(见 Capture 里的说明)。
        private static IntPtr _titleWindow;
        private static long _titleTick;
        private static string _titleText = "";

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
                // 标题按 1 秒缓存:GetWindowText 是跨进程调用,目标程序卡住时会阻塞几百毫秒,
                // 而这个回调跑在低级键鼠钩子里 —— 超过 LowLevelHooksTimeout(默认 300 ms)
                // Windows 就会静默摘钩子,表现为「鼠标点击/滚轮突然不再统计」。
                // 连续点击时没必要每次重新取标题,1 秒的误差对归因没有影响。
                long nowTick = Environment.TickCount;
                if (window != _titleWindow || nowTick - _titleTick > 1000)
                {
                    GetWindowText(window, TitleBuffer, TitleBuffer.Capacity);
                    _titleWindow = window;
                    _titleTick = nowTick;
                    _titleText = TitleBuffer.Length == 0 ? "无标题窗口" : TitleBuffer.ToString();
                }
                uint verifiedPid;
                if (GetForegroundWindow() != window || GetWindowThreadProcessId(window, out verifiedPid) == 0 || verifiedPid != pid)
                    return new AppUsage { Title = "窗口切换中 / 无法归因" };
                // v3.0:把成功解析的快照发布给钩子热路径(ForegroundApp.Cached),
                // 这样钩子里只需读一个字段,不再触发任何跨进程调用。
                AppUsage snapshot = new AppUsage { ProcessPath = _lastPath, Title = _titleText };
                _lastSnapshot = snapshot;
                return snapshot;
            }
            catch { return new AppUsage { Title = "无法识别的前台窗口" }; }
        }
        // ------------------------------------------------------------ 后台窗口解析(Step 3)
        //
        //  钩子回调里**不允许**做任何跨进程调用:GetWindowText / OpenProcess 在目标程序
        //  卡住时会阻塞几百毫秒,一旦超过 LowLevelHooksTimeout(默认 300 ms),Windows 就会
        //  静默摘掉钩子(表现为「点了没反应」)。因此钩子只取廉价的 GetForegroundWindow(),
        //  把 HWND 塞进环形缓冲;真正的 (进程路径, 窗口标题) 解析由后台写线程在这里带
        //  LRU 缓存地完成。

        /// <summary>后台解析结果:只描述窗口,不带任何计数。</summary>
        internal sealed class WindowInfo
        {
            public string ProcessPath = "";
            public string ProcessName = "";
            public string WindowTitle = "";
        }

        private sealed class HwndCacheEntry
        {
            public WindowInfo Info;
            public long StampMs;
            public long LastUsed;
        }

        /// <summary>LRU 容量上限。浏览器狂切标签时的兜底。</summary>
        internal const int CacheCapacity = 256;
        /// <summary>缓存有效期(毫秒)。</summary>
        internal const int CacheTtlMs = 1500;

        private static readonly Dictionary<IntPtr, HwndCacheEntry> HwndCache =
            new Dictionary<IntPtr, HwndCacheEntry>();
        private static readonly object CacheGate = new object();
        private static readonly object UwpGate = new object();
        private static long _cacheUse;
        private static AppUsage _lastSnapshot;
        private static readonly AppUsage UnknownSnapshot = new AppUsage { Title = "无法识别的前台窗口" };

        private delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);
        private static readonly EnumChildProc ChildProbe = ProbeUwpChild;
        private static IntPtr _uwpChildFound;

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr parent, EnumChildProc callback, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int maxCount);

        /// <summary>钩子热路径取用的最近一次已知前台快照 —— 纯字段读,零系统调用。</summary>
        internal static AppUsage Cached()
        {
            AppUsage snapshot = _lastSnapshot;
            return snapshot == null ? UnknownSnapshot : snapshot;
        }

        /// <summary>
        /// 按 HWND 解析 (进程路径, 进程名, 完整窗口标题)。带 256 条 / 1500 ms 的 LRU 缓存,
        /// 仅供后台写线程调用;绝不在钩子里调用。
        /// </summary>
        internal static WindowInfo ResolveWindowByHwnd(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return new WindowInfo { WindowTitle = "无前台窗口" };

            long now = NowMs();
            lock (CacheGate)
            {
                HwndCacheEntry hit;
                if (HwndCache.TryGetValue(hwnd, out hit) && now >= hit.StampMs && now - hit.StampMs <= CacheTtlMs)
                {
                    hit.LastUsed = ++_cacheUse;
                    return hit.Info;
                }
            }

            WindowInfo info = ResolveCore(hwnd);

            lock (CacheGate)
            {
                HwndCacheEntry entry = new HwndCacheEntry();
                entry.Info = info;
                entry.StampMs = now;
                entry.LastUsed = ++_cacheUse;
                HwndCache[hwnd] = entry;
                if (HwndCache.Count > CacheCapacity) EvictOldestEntry();
                _lastSnapshot = new AppUsage { ProcessPath = info.ProcessPath, Title = info.WindowTitle };
            }
            return info;
        }

        /// <summary>测试用:清空缓存,避免用例之间互相污染。</summary>
        internal static void ClearWindowCacheForTest()
        {
            lock (CacheGate)
            {
                HwndCache.Clear();
                _lastSnapshot = null;
            }
        }

        private static void EvictOldestEntry()
        {
            // 256 条规模,线性找最久未用即可;只在缓存满且新增时发生。
            IntPtr oldestKey = IntPtr.Zero;
            long oldestUse = long.MaxValue;
            bool found = false;
            foreach (KeyValuePair<IntPtr, HwndCacheEntry> pair in HwndCache)
            {
                if (pair.Value.LastUsed < oldestUse)
                {
                    oldestUse = pair.Value.LastUsed;
                    oldestKey = pair.Key;
                    found = true;
                }
            }
            if (found) HwndCache.Remove(oldestKey);
        }

        private static WindowInfo ResolveCore(IntPtr hwnd)
        {
            WindowInfo info = new WindowInfo();
            try
            {
                uint pid;
                if (GetWindowThreadProcessId(hwnd, out pid) == 0 || pid == 0)
                {
                    info.WindowTitle = "无法识别的前台窗口";
                    return info;
                }

                string path = QueryProcessPath(pid);

                // UWP:ApplicationFrameHost.exe 只是宿主框,真正的窗口属于
                // Windows.UI.Core.CoreWindow 子窗口所属的那个进程。
                if (IsFrameHost(path))
                {
                    IntPtr core = FindUwpCoreWindow(hwnd);
                    uint childPid;
                    if (core != IntPtr.Zero && GetWindowThreadProcessId(core, out childPid) != 0 && childPid != 0)
                    {
                        string real = QueryProcessPath(childPid);
                        if (!string.IsNullOrEmpty(real)) path = real;
                    }
                }

                info.ProcessPath = path;
                info.ProcessName = string.IsNullOrEmpty(path) ? "" : Path.GetFileName(path);
                info.WindowTitle = QueryWindowTitle(hwnd);
                return info;
            }
            catch
            {
                info.WindowTitle = "无法识别的前台窗口";
                return info;
            }
        }

        private static string QueryProcessPath(uint pid)
        {
            IntPtr process = OpenProcess(0x1000, false, pid);   // QUERY_LIMITED_INFORMATION
            if (process == IntPtr.Zero) return "";
            try
            {
                StringBuilder path = new StringBuilder(32768);
                uint size = (uint)path.Capacity;
                return QueryFullProcessImageName(process, 0, path, ref size) ? path.ToString() : "";
            }
            finally
            {
                CloseHandle(process);
            }
        }

        private static string QueryWindowTitle(IntPtr hwnd)
        {
            StringBuilder buffer = new StringBuilder(1024);
            GetWindowText(hwnd, buffer, buffer.Capacity);
            return buffer.Length == 0 ? "无标题窗口" : buffer.ToString();
        }

        private static bool IsFrameHost(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return string.Equals(Path.GetFileName(path), "ApplicationFrameHost.exe",
                StringComparison.OrdinalIgnoreCase);
        }

        private static IntPtr FindUwpCoreWindow(IntPtr frame)
        {
            lock (UwpGate)
            {
                _uwpChildFound = IntPtr.Zero;
                try { EnumChildWindows(frame, ChildProbe, IntPtr.Zero); }
                catch { }
                return _uwpChildFound;
            }
        }

        private static bool ProbeUwpChild(IntPtr child, IntPtr lParam)
        {
            try
            {
                StringBuilder name = new StringBuilder(256);
                if (GetClassName(child, name, name.Capacity) > 0 &&
                    string.Equals(name.ToString(), "Windows.UI.Core.CoreWindow", StringComparison.Ordinal))
                {
                    _uwpChildFound = child;
                    return false;   // 找到即停止枚举
                }
            }
            catch { }
            return true;
        }

        private static long NowMs()
        {
            return (long)(System.Diagnostics.Stopwatch.GetTimestamp() * 1000.0
                / System.Diagnostics.Stopwatch.Frequency);
        }
    }

    internal static class AppActivity
    {
        /// <summary>友好名缓存(进程路径 → 显示名)。列表每帧重绘都要取名字,FileVersionInfo 读盘只能发生一次。</summary>
        private static readonly Dictionary<string, string> FriendlyNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object FriendlyNameLock = new object();
        /// <summary>上限只防异常输入把缓存撑爆;正常机器上的应用数量远小于它。</summary>
        private const int FriendlyNameCacheLimit = 512;

        /// <summary>
        /// 统一的友好应用名：优先文件描述，其次常见进程映射，最后去掉 .exe 的进程名。
        /// 日报、应用统计、提示文案都从这里取，避免同一程序在不同页面显示不同名字。
        /// 结果按路径缓存在内存里：GDI+ 每帧重绘都会取一次名字，不能每次都去读文件版本信息。
        /// </summary>
        public static string FriendlyName(string processPath)
        {
            string key = processPath ?? "";
            lock (FriendlyNameLock)
            {
                string cached;
                if (FriendlyNames.TryGetValue(key, out cached)) return cached;
            }
            string name = ComputeFriendlyName(key);
            lock (FriendlyNameLock)
            {
                // 两帧同时首次命中同一路径最多各算一次,结果一致;这里只管别让缓存无限长。
                if (FriendlyNames.Count < FriendlyNameCacheLimit) FriendlyNames[key] = name;
            }
            return name;
        }

        private static string ComputeFriendlyName(string processPath)
        {
            string baseName = ProcessBaseName(processPath);
            if (baseName.Length == 0) return "未识别应用";
            string description = "";
            try { description = FileVersionInfo.GetVersionInfo(processPath).FileDescription; }
            catch (Exception) { }
            if (!string.IsNullOrWhiteSpace(description) && !IsGenericDescription(description))
                return description.Trim();
            string known;
            if (KnownNames.TryGetValue(baseName.ToLowerInvariant(), out known)) return known;
            return baseName;
        }

        public static string ProcessBaseName(string processPath)
        {
            try { return string.IsNullOrEmpty(processPath) ? "" : Path.GetFileNameWithoutExtension(processPath); }
            catch (Exception) { return ""; }
        }

        private static bool IsGenericDescription(string description)
        {
            string value = description.ToLowerInvariant();
            return value.Contains("windows") && (value.Contains("operating system") || value.Contains("application"));
        }

        private static readonly Dictionary<string, string> KnownNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "msedge", "Microsoft Edge" },
            { "chrome", "Google Chrome" },
            { "firefox", "Firefox" },
            { "chatgpt", "ChatGPT" },
            { "qq", "QQ" },
            { "weixin", "微信" },
            { "wechat", "微信" },
            { "wps", "WPS Office" },
            { "winword", "Microsoft Word" },
            { "excel", "Microsoft Excel" },
            { "powerpnt", "Microsoft PowerPoint" },
            { "code", "Visual Studio Code" },
            { "devenv", "Visual Studio" },
            { "explorer", "文件资源管理器" },
            { "steam", "Steam" },
            { "discord", "Discord" },
            { "telegram", "Telegram" },
            { "notepad", "记事本" },
            { "notepad++", "Notepad++" },
            { "vlc", "VLC" },
            { "spotify", "Spotify" },
            { "doubao", "豆包" },
            { "yuanbao", "腾讯元宝" },
            { "quark", "夸克" }
        };

        // Append categories to preserve numeric IDs in existing manual rules.
        // v3.0:0~5 的数值与语义**永久冻结**(历史 app_rule 与聚合都按数值落盘),
        // 只能向上追加。6=科研 7=写作 8=沟通 9=娱乐。
        public static readonly string[] Categories =
        {
            "编程", "游戏", "视频", "其他", "未分类", "办公",
            "科研", "写作", "沟通", "娱乐"
        };
        // C2:UI 菜单会写规则,DbWriter 后台线程会遍历规则做窗口分类;
        // ConcurrentDictionary 让 TryGetValue/foreach 与 Clear/Remove 并发安全,
        // 不引入全局锁,也不改变序列化格式(每条规则仍是一行 app_rule=...)。
        public static readonly ConcurrentDictionary<string, int> Rules =
            new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
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
            string[] research = { "arxiv", "ieee xplore", "sciencedirect", "springer", "nature", "知网", "cnki", "pubmed", "google scholar", "researchgate" };
            for (int i = 0; i < research.Length; i++)
            {
                string marker = research[i];
                if (title == marker || title.EndsWith(" - " + marker, StringComparison.Ordinal)
                    || title.EndsWith(" · " + marker, StringComparison.Ordinal) || title.EndsWith(" | " + marker, StringComparison.Ordinal)
                    || title.EndsWith("_" + marker, StringComparison.Ordinal))
                { site = marker; return 6; }
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
            if (Contains("wps.exe|et.exe|wpp.exe|wpspdf.exe|winword.exe|excel.exe|powerpnt.exe|onenote.exe|outlook.exe|acrobat.exe|acrord32.exe|notion.exe|obsidian.exe|zotero.exe|opensquilla.exe|chatgpt.exe|claude.exe|wechat.exe|weixin.exe|qq.exe|dingtalk.exe|feishu.exe|discord.exe|telegram.exe", name)) return 5;
            if (Contains("matlab.exe|spss.exe|stata.exe|stata-mp.exe|endnote.exe|mendeley.exe|jabref.exe|origin.exe|imagej.exe|chemdraw.exe", name)) return 6;
            if (Contains("typora.exe|marktext.exe|scrivener.exe|ulysses.exe|zim.exe|writemonkey.exe|ghostwriter.exe", name)) return 7;
            if (Contains("slack.exe|zoom.exe|teams.exe|msteams.exe|lark.exe|wxwork.exe|whatsapp.exe|line.exe|skype.exe", name)) return 8;
            if (Contains("steam.exe|epicgameslauncher.exe|battle.net.exe|cloudmusic.exe|neteasecloudmusic.exe|qqmusic.exe|kugou.exe|spotify.exe|foobar2000.exe", name)) return 9;
            if (Contains("explorer.exe|taskmgr.exe|systemsettings.exe|searchhost.exe|startmenuexperiencehost.exe|shellexperiencehost.exe|applicationframehost.exe|7zfm.exe|winrar.exe", name)) return 3;
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
            Record(day, snapshot, kind, -1);
        }

        /// <summary>kind:0 击键 / 1 点击 / 2 滚轮;group 为键位分组(-1 表示不计分组)。</summary>
        public static void Record(DayRecord day, AppUsage snapshot, int kind, int group)
        {
            AppUsage record = EnsureRecord(day, snapshot);
            if (kind == 0)
            {
                record.Keys++;
                if (group >= 0 && group < record.KeyGroups.Length)
                {
                    record.KeyGroups[group]++;
                    record.HasKeyGroups = true;
                }
            }
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
                        // 列表与详情抽屉/日报共用同一套友好名:按应用时不再显示 chrome.exe 这种进程名。
                        row = new Row { Id = id, Name = byWindow ? app.Title : FriendlyName(app.ProcessPath),
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
            StringBuilder csv = new StringBuilder("日期,应用,窗口标题,进程路径,分类,击键,点击,滚轮,每分钟输入(APM),分类来源,分类依据,观测活跃秒\r\n");
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
                    csv.Append(row.Legacy || row.ActiveSeconds < 1 ? "" : AppFlow.ApmText(AppFlow.ApmOf(row))).Append(',');
                    csv.Append(CsvCell(row.Source)).Append(',').Append(CsvCell(row.Reason)).Append(',');
                    csv.AppendLine(row.Legacy || day.AppObservedSeconds == 0 ? "" : row.ActiveSeconds.ToString("0.###", CultureInfo.InvariantCulture));
                }
            }
            return csv.ToString();
        }
    }
}

