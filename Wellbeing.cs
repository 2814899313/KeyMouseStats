// ============================================================================
//  键鼠统计 - 目标与提醒  Wellbeing.cs
//
//  1.7.2 新增:
//    · 每日目标:按击键或按活跃时长,可关闭;达成当天只提示一次。
//    · 连续使用提醒:默认关闭,阈值与冷却可调;前台全屏(游戏)时抑制。
//    · 可选每日摘要:到点提示今日概况。
//
//  口径:提醒基于「连续活跃段」(空闲阈值内不打断),与空闲阈值本身是两个独立设置,
//  修改提醒不会改变任何统计口径。全部设置默认关闭,保存在数据文件的 wellbeing_v1。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace KeyMouseStats
{
    internal static class WellbeingSettings
    {
        public const int GoalOff = 0, GoalKeys = 1, GoalActive = 2;

        public static int GoalMode;
        public static double GoalTarget;
        /// <summary>已提示达成目标的日期(yyyy-MM-dd);当天不再重复提示。</summary>
        public static string GoalNotified = "";
        public static bool ContinuousEnabled;
        public static int ContinuousMinutes = 90;
        public static int CooldownMinutes = 30;
        public static DateTime LastReminder = DateTime.MinValue;
        public static bool SummaryEnabled;
        public static int SummaryHour = 22;
        public static string SummarySent = "";

        public static bool GoalEnabled { get { return GoalMode != GoalOff && GoalTarget > 0; } }
        public static bool AnyEnabled { get { return GoalEnabled || ContinuousEnabled || SummaryEnabled; } }

        /// <summary>今日进度 0—1;未设置目标时返回 NaN。</summary>
        public static double Progress(DayRecord day)
        {
            if (!GoalEnabled || day == null) return double.NaN;
            double value = GoalMode == GoalKeys ? day.Keys : day.ActiveSeconds;
            if (GoalTarget <= 0) return double.NaN;
            return Math.Max(0, value / GoalTarget);
        }

        public static bool Reached(DayRecord day)
        {
            double progress = Progress(day);
            return RangeMath.IsNumber(progress) && progress >= 1;
        }

        public static string GoalName { get { return GoalMode == GoalKeys ? "击键" : "活跃时长"; } }

        public static string TargetText
        {
            get
            {
                if (!GoalEnabled) return "未设置";
                return GoalMode == GoalKeys
                    ? Analysis.FmtCount((long)Math.Round(GoalTarget)) + " 次击键"
                    : ActivityMonitor.FormatDuration(GoalTarget);
            }
        }

        public static string UsedText(DayRecord day)
        {
            if (day == null) return "--";
            return GoalMode == GoalKeys ? Analysis.FmtCount(day.Keys) : ActivityMonitor.FormatDuration(day.ActiveSeconds);
        }

        public static string RemainingText(DayRecord day)
        {
            if (!GoalEnabled || day == null) return "";
            double remaining = GoalTarget - (GoalMode == GoalKeys ? day.Keys : day.ActiveSeconds);
            if (remaining <= 0) return "已达成";
            return GoalMode == GoalKeys
                ? "还差 " + Analysis.FmtCount((long)Math.Round(remaining)) + " 次"
                : "还差 " + ActivityMonitor.FormatDuration(remaining);
        }

        public static string Encode()
        {
            return GoalMode.ToString(CultureInfo.InvariantCulture) + "|"
                + GoalTarget.ToString("R", CultureInfo.InvariantCulture) + "|"
                + GoalNotified + "|"
                + (ContinuousEnabled ? "1" : "0") + "|"
                + ContinuousMinutes.ToString(CultureInfo.InvariantCulture) + "|"
                + CooldownMinutes.ToString(CultureInfo.InvariantCulture) + "|"
                + (LastReminder == DateTime.MinValue ? "" : LastReminder.ToString("O", CultureInfo.InvariantCulture)) + "|"
                + (SummaryEnabled ? "1" : "0") + "|"
                + SummaryHour.ToString(CultureInfo.InvariantCulture) + "|"
                + SummarySent;
        }

        public static void Load(string text)
        {
            try
            {
                string[] parts = (text ?? "").Split('|');
                if (parts.Length < 10) return;
                int mode;
                if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out mode))
                    GoalMode = mode >= GoalOff && mode <= GoalActive ? mode : GoalOff;
                double target;
                if (double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out target) && target >= 0)
                    GoalTarget = target;
                GoalNotified = parts[2] ?? "";
                ContinuousEnabled = parts[3] == "1";
                int minutes;
                if (int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes))
                    ContinuousMinutes = minutes >= 15 && minutes <= 480 ? minutes : 90;
                if (int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes))
                    CooldownMinutes = minutes >= 5 && minutes <= 240 ? minutes : 30;
                DateTime last;
                if (DateTime.TryParse(parts[6], CultureInfo.InvariantCulture, DateTimeStyles.None, out last)) LastReminder = last;
                SummaryEnabled = parts[7] == "1";
                if (int.TryParse(parts[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes))
                    SummaryHour = minutes >= 0 && minutes <= 23 ? minutes : 22;
                SummarySent = parts[9] ?? "";
            }
            catch (FormatException) { }
        }
    }

    /// <summary>前台窗口是否占满整个显示器(游戏 / 全屏视频)。用于免打扰判断。</summary>
    internal static class FullscreenWindow
    {
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO
        {
            public int Size; public RECT Monitor, Work; public int Flags;
        }
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out RECT rect);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        /// <summary>无法判断时返回 false(宁可提示,也不要静默丢弃)。</summary>
        public static bool Active()
        {
            try
            {
                IntPtr window = GetForegroundWindow();
                if (window == IntPtr.Zero) return false;
                RECT bounds;
                if (!GetWindowRect(window, out bounds)) return false;
                IntPtr monitor = MonitorFromWindow(window, 2);
                if (monitor == IntPtr.Zero) return false;
                MONITORINFO info = new MONITORINFO();
                info.Size = Marshal.SizeOf(typeof(MONITORINFO));
                if (!GetMonitorInfo(monitor, ref info)) return false;
                return bounds.Left <= info.Monitor.Left && bounds.Top <= info.Monitor.Top
                    && bounds.Right >= info.Monitor.Right && bounds.Bottom >= info.Monitor.Bottom;
            }
            catch { return false; }
        }
    }

    /// <summary>目标达成、连续使用与每日摘要的判断。全部只读状态,返回要显示的文本或 null。</summary>
    internal static class Wellbeing
    {
        /// <summary>免打扰判断。产品里查前台窗口是否全屏;测试可替换。</summary>
        internal static Func<bool> FullscreenProbe = delegate { return FullscreenWindow.Active(); };

        /// <summary>目标达成:同一天只提示一次。</summary>
        public static string GoalMessage(DateTime now, DayRecord day)
        {
            if (!WellbeingSettings.GoalEnabled || day == null) return null;
            if (!WellbeingSettings.Reached(day)) return null;
            string today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (WellbeingSettings.GoalNotified == today) return null;
            WellbeingSettings.GoalNotified = today;
            return "今日目标已达成：" + WellbeingSettings.TargetText + "（" + WellbeingSettings.GoalName + "）。";
        }

        /// <summary>连续使用提醒:达到阈值且过了冷却、且不在全屏时提示。</summary>
        public static string ContinuousMessage(DateTime now, ActiveSession session)
        {
            if (!WellbeingSettings.ContinuousEnabled || session == null) return null;
            double minutes = session.Seconds / 60.0;
            if (minutes < WellbeingSettings.ContinuousMinutes) return null;
            if (WellbeingSettings.LastReminder != DateTime.MinValue
                && (now - WellbeingSettings.LastReminder).TotalMinutes < WellbeingSettings.CooldownMinutes) return null;
            if (FullscreenProbe()) return null;
            WellbeingSettings.LastReminder = now;
            return "你已经连续使用 " + ActivityMonitor.FormatDuration(session.Seconds) + "，建议休息一下。";
        }

        /// <summary>每日摘要:到设定时间后当天只提示一次。</summary>
        public static string SummaryMessage(DateTime now)
        {
            if (!WellbeingSettings.SummaryEnabled) return null;
            if (now.Hour < WellbeingSettings.SummaryHour) return null;
            string today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (WellbeingSettings.SummarySent == today) return null;
            if (FullscreenProbe()) return null;
            WellbeingSettings.SummarySent = today;
            DayRecord day = Store.Today;
            return "今日 " + Analysis.FmtCount(day.Keys) + " 次击键 · " + Analysis.FmtCount(day.Clicks) + " 次点击 · 活跃 "
                + ActivityMonitor.FormatDuration(day.ActiveSeconds) + "。";
        }

        /// <summary>区间内达成目标的天数与连续达成天数(从区间末尾往前数)。</summary>
        public static void GoalStats(DateTime start, DateTime end, out int met, out int observed, out int streak)
        {
            met = 0; observed = 0; streak = 0;
            if (!WellbeingSettings.GoalEnabled) return;
            DateTime from, to;
            if (!RangeRules.TryNormalize(start, end, out from, out to)) return;
            bool counting = true;
            for (DateTime date = to; date >= from; date = date.AddDays(-1))
            {
                DayRecord day = Analysis.GetDay(date);
                if (day == null || day.IsEmpty) continue;
                observed++;
                if (WellbeingSettings.Reached(day))
                {
                    met++;
                    if (counting) streak++;
                }
                else counting = false;
            }
        }
    }
}
