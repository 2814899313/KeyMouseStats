// ============================================================================
//  KeyMouseStats v3.0 · Milestone 2 / Step 2  SampleData.cs
//
//  纯内存「示例数据预览模式」。
//
//  三条硬约束(与 v3-milestone2-ux.md 的 D3 拍板一致):
//    1. 全程只在内存里替换 Store.History / Today / Total 的**引用**,不新建任何文件;
//    2. Active 期间 Store.Save / SaveAsync 与 ShadowPipeline.Enqueue 直接短路,
//       哪怕 PersistenceArmed 为真也绝不落盘;
//    3. Active 期间实时钩子不向示例记录累加增量 —— 退出时对真实引用快照原样还原。
//
//  示例点击坐标一律落在**真实显示器矩形内**:热区聚合会按 Screen.AllScreens 裁剪,
//  合成坐标一旦越界会被整格丢弃,新用户就会看到一片空白。
//
//  C# 5:无 $"…" / ?. / => 成员 / nameof / out var / 元组。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace KeyMouseStats
{
    internal static class SampleData
    {
        /// <summary>示例覆盖的天数(含今天)。</summary>
        internal const int DayCount = 14;

        private static HistoryMap _savedHistory;
        private static DayRecord _savedToday;
        private static Counters _savedTotal;
        private static DateTime _savedDay;

        /// <summary>是否处于示例预览模式。不持久化,进程退出即失效。</summary>
        internal static bool Active { get; private set; }

        /// <summary>进入示例模式。重复调用无副作用。</summary>
        internal static void Enter()
        {
            if (Active) return;
            _savedHistory = Store.History;
            _savedToday = Store.Today;
            _savedTotal = Store.Total;
            _savedDay = Store.Day;

            HistoryMap built = Build();
            Store.History = built;
            Store.InvalidateHistorySnapshot();
            Store.Total = Sum(built);
            Store.Day = DateTime.Today;
            DayRecord live;
            if (!built.TryGetValue(DateTime.Today, out live))
            {
                live = new DayRecord();
                live.Date = DateTime.Today;
                built[DateTime.Today] = live;
            }
            Store.Today = live;
            Active = true;
        }

        /// <summary>退出示例模式,把真实引用快照原样还回去。重复调用无副作用。</summary>
        internal static void Exit()
        {
            if (!Active) return;
            Store.History = _savedHistory;
            Store.InvalidateHistorySnapshot();
            Store.Today = _savedToday;
            Store.Total = _savedTotal;
            Store.Day = _savedDay;
            _savedHistory = null;
            _savedToday = null;
            _savedTotal = null;
            Active = false;
        }

        internal static void Toggle()
        {
            if (Active) Exit(); else Enter();
        }

        /// <summary>
        /// 全新安装且没有任何记录。
        /// 注意:Store 的静态构造会预置「今天」这条空记录,所以**不能用 History.Count 判空**,
        /// 必须看全量按键数 + 逐日 IsEmpty。
        /// </summary>
        internal static bool IsFreshEmpty(IEnumerable<DayRecord> days, long totalKeys)
        {
            if (totalKeys > 0) return false;
            if (days == null) return true;
            foreach (DayRecord day in days)
            {
                if (day == null) continue;
                if (!day.IsEmpty) return false;
            }
            return true;
        }

        /// <summary>当前进程是否处于「全新零数据」状态。示例模式期间恒为 false。</summary>
        internal static bool IsFreshNow()
        {
            if (Active) return false;
            return IsFreshEmpty(Store.SnapshotHistory(), Store.Total.Keys);
        }

        // ---------------------------------------------------------------- 生成

        private static HistoryMap Build()
        {
            HistoryMap map = new HistoryMap();
            Random random = new Random(20261216);
            DateTime today = DateTime.Today;
            for (int back = DayCount - 1; back >= 0; back--)
            {
                DateTime date = today.AddDays(-back);
                map[date] = BuildDay(date, back, random);
            }
            return map;
        }

        private static DayRecord BuildDay(DateTime date, int back, Random random)
        {
            DayRecord day = new DayRecord();
            day.Date = date;

            bool weekend = date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;
            // 今天只过了大半天,量按「已经过去的时间」折减,看起来才像真的。
            double elapsed = back == 0 ? Math.Max(0.25, DateTime.Now.TimeOfDay.TotalHours / 24.0) : 1.0;
            double baseScale = weekend ? 0.45 : 0.85 + random.NextDouble() * 0.4;
            long keys = (long)Math.Round(9200 * baseScale * elapsed);
            long clicks = (long)Math.Round(1450 * baseScale * elapsed);
            if (keys < 60) keys = 60;
            if (clicks < 10) clicks = 10;

            // 按键时长分布:早 9-12、下午 14-18、晚间 20-23 三峰。
            int[] curve = { 1, 1, 1, 1, 1, 1, 2, 3, 5, 9, 10, 8, 5, 7, 9, 10, 8, 6, 4, 6, 9, 11, 9, 5 };
            long curveSum = 0;
            for (int i = 0; i < 24; i++) curveSum += curve[i];
            for (int h = 0; h < 24; h++)
            {
                long share = keys * curve[h] / Math.Max(1, curveSum);
                day.HourKeys[h] = share;
                day.HourClicks[h] = clicks * curve[h] / Math.Max(1, curveSum);
            }
            day.Keys = keys;

            // 按键细分:常用键 + 修饰键 + 数字,保证「全键热力」「按键排行」「组合动作」都有料。
            int[] vks = { 0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49, 0x4A, 0x4B,
                          0x53, 0x54, 0x52, 0x4E, 0x20, 0x0D, 0x08, 0x1B, 0x2E, 0x09,
                          0x10, 0x11, 0x12, 0x25, 0x26, 0x27, 0x28, 0x31, 0x32, 0x33, 0x34 };
            long assigned = 0;
            for (int i = 0; i < vks.Length; i++)
            {
                long count = Math.Max(1, keys / (vks.Length + i) + random.Next(0, 40));
                if (i == 0x20 - 0x20) count = keys / 6;         // A 键最常用
                if (assigned + count > keys) count = Math.Max(1, keys - assigned);
                if (assigned >= keys) break;
                day.KeyCounts[vks[i]] = count;
                day.PhysicalKeys[vks[i]] = count;
                day.PhysicalVks[vks[i]] = count;
                assigned += count;
            }
            day.Keys = Math.Max(keys, assigned);

            // 组合动作。
            string[] combos = { "Ctrl+C", "Ctrl+V", "Ctrl+S", "Ctrl+Z", "Alt+Tab", "Win+E", "Ctrl+Shift+S", "Ctrl+F" };
            for (int i = 0; i < combos.Length; i++)
                ShortcutStats.Add(day, combos[i]);
            for (int i = 0; i < combos.Length; i++)
            {
                long n;
                day.ComboCounts.TryGetValue(combos[i], out n);
                day.ComboCounts[combos[i]] = n + 3 + random.Next(0, 9);
            }

            // 应用使用:覆盖编程 / 游戏 / 视频 / 办公 / 写作 / 娱乐 / 沟通 / 科研 八类。
            string[][] apps = new string[][]
            {
                new string[] { @"C:\Apps\Code.exe",        "KeyMouseStats — Visual Studio Code" },
                new string[] { @"C:\Apps\WindowsTerminal.exe", "pwsh — 键鼠统计" },
                new string[] { @"C:\Apps\chrome.exe",      "GitHub · Pull requests" },
                new string[] { @"C:\Apps\chrome.exe",      "论文草稿 - arXiv" },
                new string[] { @"C:\Apps\typora.exe",      "设计笔记.md" },
                new string[] { @"C:\Apps\wps.exe",         "季度汇报.docx" },
                new string[] { @"C:\Apps\slack.exe",       "Slack — #general" },
                new string[] { @"C:\Apps\matlab.exe",      "MATLAB R2024b" },
                new string[] { @"C:\Apps\cloudmusic.exe",  "网易云音乐" },
                new string[] { @"C:\Apps\steam.exe",       "Steam" }
            };
            for (int i = 0; i < apps.Length; i++)
            {
                AppUsage usage = new AppUsage();
                usage.ProcessPath = apps[i][0];
                usage.Title = apps[i][1];
                int weight = 2 + random.Next(0, 6);
                for (int k = 0; k < weight; k++) AppActivity.Record(day, usage, 0);
                for (int k = 0; k < 2; k++) AppActivity.Record(day, usage, 1);
            }
            day.AppObservedSeconds = Math.Round(3600 * baseScale * elapsed + random.Next(0, 1800), 1);
            day.ActiveSeconds = Math.Round(day.AppObservedSeconds * 0.78, 1);
            day.IdleSeconds = Math.Round(day.AppObservedSeconds * 0.22, 1);
            day.AppSwitches = 40 + random.Next(0, 120);
            day.PeakApm = 120 + random.Next(0, 220);

            // 三键热区:按真实显示器矩形分布,越界会被聚合整格丢弃。
            long left = clicks * 70 / 100;
            long right = clicks * 22 / 100;
            long middle = clicks * 5 / 100;
            long xbutton = Math.Max(0, clicks - left - right - middle);
            day.Left = left;
            day.Right = right;
            day.Middle = middle;
            day.XButtons = xbutton;
            day.Clicks = left + right + middle + xbutton;
            ScatterClicks(day, random, 1, left);
            ScatterClicks(day, random, 2, right);
            ScatterClicks(day, random, 3, middle);
            ScatterClicks(day, random, 4, xbutton);

            day.Wheel = clicks * 3 + random.Next(0, 200);
            day.MovePx = clicks * 900 + random.Next(0, 60000);
            day.MoveMeters = Math.Round(day.MovePx / 900.0, 2);

            // 连续使用段:上午 / 下午 / 晚间各一段。
            AddSession(day, date, 9, 30, 75 + random.Next(0, 60), day.PeakApm);
            AddSession(day, date, 14, 10, 95 + random.Next(0, 80), day.PeakApm);
            AddSession(day, date, 20, 20, 110 + random.Next(0, 90), day.PeakApm);

            // 鼠标交叉数据(相对鼠标方向),让「鼠标动作」视图有内容。
            day.Cross.Direction[0] = 300 + random.Next(0, 60);
            day.Cross.Direction[1] = 280 + random.Next(0, 60);
            day.Cross.Direction[2] = 160 + random.Next(0, 60);
            day.Cross.Direction[3] = 170 + random.Next(0, 60);
            day.Cross.PathCounts = 800 + random.Next(0, 400);
            day.Cross.DragCounts = 20 + random.Next(0, 40);
            day.Cross.Clicks = day.Clicks;
            day.Cross.MoveClicks = (int)(day.Clicks * 8 / 10);
            day.Cross.Packets = 400000 + random.Next(0, 200000);
            day.Cross.CalibratedPackets = 390000 + random.Next(0, 200000);
            day.Cross.Flicks = 4000 + random.Next(0, 4000);
            day.Cross.Reversals = 300 + random.Next(0, 300);

            return day;
        }

        private static void ScatterClicks(DayRecord day, Random random, int button, long count)
        {
            if (count <= 0) return;
            Screen[] screens = Screen.AllScreens;
            if (screens == null || screens.Length == 0) return;
            for (long i = 0; i < count; i++)
            {
                Screen screen = screens[(int)(i % screens.Length)];
                Rectangle area = screen.Bounds;
                int x = area.Left + random.Next(0, Math.Max(1, area.Width));
                int y = area.Top + random.Next(0, Math.Max(1, area.Height));
                ClickHeat.ResetDedupe();
                ClickHeat.Record(day, x, y, button);
            }
        }

        private static void AddSession(DayRecord day, DateTime date, int hour, int minute, double seconds, long peak)
        {
            ActiveSession session = new ActiveSession();
            session.Start = date.AddHours(hour).AddMinutes(minute);
            session.End = session.Start.AddSeconds(seconds);
            session.Seconds = seconds;
            session.RateMeasured = true;
            session.PeakApm = peak;
            session.LastApm = Math.Max(0, peak / 2);
            day.Sessions.Add(session);
            day.IdlePeriods.Add(session);       // 只为让「空闲段」相关视图不空
        }

        private static Counters Sum(Dictionary<DateTime, DayRecord> map)
        {
            Counters total = new Counters();
            foreach (DayRecord day in map.Values)
            {
                if (day == null) continue;
                total.Keys += day.Keys;
                total.Clicks += day.Clicks;
                total.Left += day.Left;
                total.Right += day.Right;
                total.Middle += day.Middle;
                total.XButtons += day.XButtons;
                total.Wheel += day.Wheel;
                total.MovePx += day.MovePx;
                total.MoveMeters += day.MoveMeters;
                total.HoldCount += day.Holds.Count;
                total.HoldActiveSeconds += day.ActiveSeconds;
            }
            return total;
        }
    }
}
