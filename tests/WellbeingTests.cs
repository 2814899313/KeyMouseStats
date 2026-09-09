using System;
using System.Collections.Generic;
using KeyMouseStats;

/// <summary>
/// 目标与提醒回归:设置编解码、进度与达成、提醒阈值与冷却、免打扰、每日摘要与达成统计。
/// 只使用内存中的合成数据与可替换的全屏判断,不接触用户的真实数据。
/// </summary>
internal static class WellbeingTests
{
    private static int _checks;
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        _checks++;
    }
    private static void Near(double actual, double expected, double tolerance, string name)
    {
        Check(Math.Abs(actual - expected) <= tolerance, name + " (expected " + expected + ", got " + actual + ")");
    }

    private static void Reset()
    {
        WellbeingSettings.GoalMode = WellbeingSettings.GoalOff;
        WellbeingSettings.GoalTarget = 0;
        WellbeingSettings.GoalNotified = "";
        WellbeingSettings.ContinuousEnabled = false;
        WellbeingSettings.ContinuousMinutes = 90;
        WellbeingSettings.CooldownMinutes = 30;
        WellbeingSettings.LastReminder = DateTime.MinValue;
        WellbeingSettings.SummaryEnabled = false;
        WellbeingSettings.SummaryHour = 22;
        WellbeingSettings.SummarySent = "";
        Wellbeing.FullscreenProbe = delegate { return false; };
    }

    private static DayRecord Day(DateTime date, long keys, double active)
    {
        DayRecord day = new DayRecord();
        day.Date = date;
        day.Keys = keys;
        day.ActiveSeconds = active;
        return day;
    }

    private static void Main()
    {
        // ---- 设置编解码往返 ----
        Reset();
        WellbeingSettings.GoalMode = WellbeingSettings.GoalKeys;
        WellbeingSettings.GoalTarget = 20000;
        WellbeingSettings.GoalNotified = "2026-09-09";
        WellbeingSettings.ContinuousEnabled = true;
        WellbeingSettings.ContinuousMinutes = 120;
        WellbeingSettings.CooldownMinutes = 45;
        WellbeingSettings.LastReminder = new DateTime(2026, 9, 9, 10, 30, 0);
        WellbeingSettings.SummaryEnabled = true;
        WellbeingSettings.SummaryHour = 21;
        WellbeingSettings.SummarySent = "2026-09-08";
        string encoded = WellbeingSettings.Encode();
        Reset();
        WellbeingSettings.Load(encoded);
        Check(WellbeingSettings.GoalMode == WellbeingSettings.GoalKeys, "mode round trip");
        Near(WellbeingSettings.GoalTarget, 20000, 1e-9, "target round trip");
        Check(WellbeingSettings.GoalNotified == "2026-09-09", "notified date round trip");
        Check(WellbeingSettings.ContinuousEnabled && WellbeingSettings.ContinuousMinutes == 120, "continuous round trip");
        Check(WellbeingSettings.CooldownMinutes == 45, "cooldown round trip");
        Check(WellbeingSettings.LastReminder == new DateTime(2026, 9, 9, 10, 30, 0), "last reminder round trip");
        Check(WellbeingSettings.SummaryEnabled && WellbeingSettings.SummaryHour == 21, "summary round trip");
        Check(WellbeingSettings.SummarySent == "2026-09-08", "summary sent round trip");

        // 越界值必须被收敛到安全范围
        WellbeingSettings.Load("9|1000||1|9999|9999||1|99|");
        Check(WellbeingSettings.GoalMode == WellbeingSettings.GoalOff, "invalid goal mode falls back to off");
        Check(WellbeingSettings.ContinuousMinutes == 90 && WellbeingSettings.CooldownMinutes == 30, "out-of-range minutes fall back");
        Check(WellbeingSettings.SummaryHour == 22, "out-of-range hour falls back");
        Reset();

        // ---- 进度 ----
        DayRecord day = Day(DateTime.Today, 10000, 7200);
        Check(double.IsNaN(WellbeingSettings.Progress(day)), "no goal means no progress");
        WellbeingSettings.GoalMode = WellbeingSettings.GoalKeys;
        WellbeingSettings.GoalTarget = 20000;
        Near(WellbeingSettings.Progress(day), 0.5, 1e-12, "key goal progress");
        Check(!WellbeingSettings.Reached(day), "half of the goal is not reached");
        Check(WellbeingSettings.TargetText.Contains("2万"), "target text shows the goal");
        Check(WellbeingSettings.UsedText(day).Contains("1万"), "used text shows today's value");
        Check(WellbeingSettings.RemainingText(day).Contains("还差"), "remaining text before the goal");
        day.Keys = 25000;
        Near(WellbeingSettings.Progress(day), 1.25, 1e-12, "progress may exceed one");
        Check(WellbeingSettings.Reached(day), "over the goal is reached");
        Check(WellbeingSettings.RemainingText(day) == "已达成", "remaining text after the goal");

        WellbeingSettings.GoalMode = WellbeingSettings.GoalActive;
        WellbeingSettings.GoalTarget = 4 * 3600;
        day = Day(DateTime.Today, 0, 2 * 3600);
        Near(WellbeingSettings.Progress(day), 0.5, 1e-12, "active-time goal progress");
        Check(WellbeingSettings.TargetText.Contains("4 时"), "active target text uses duration");
        Reset();

        // ---- 目标达成提示:当天只一次 ----
        WellbeingSettings.GoalMode = WellbeingSettings.GoalKeys;
        WellbeingSettings.GoalTarget = 100;
        DateTime now = new DateTime(2026, 9, 9, 15, 0, 0);
        Check(Wellbeing.GoalMessage(now, Day(now, 50, 0)) == null, "no message before the goal");
        string first = Wellbeing.GoalMessage(now, Day(now, 150, 0));
        Check(first != null && first.Contains("已达成"), "goal message on reaching");
        Check(Wellbeing.GoalMessage(now, Day(now, 200, 0)) == null, "goal message is sent once per day");
        Check(WellbeingSettings.GoalNotified == "2026-09-09", "notified date is recorded");
        Reset();

        // ---- 连续使用提醒 ----
        WellbeingSettings.ContinuousEnabled = true;
        WellbeingSettings.ContinuousMinutes = 15;
        WellbeingSettings.CooldownMinutes = 30;
        ActiveSession shortSession = new ActiveSession { Start = now.AddMinutes(-10), End = now, Seconds = 10 * 60 };
        Check(Wellbeing.ContinuousMessage(now, shortSession) == null, "no reminder below the threshold");
        ActiveSession longSession = new ActiveSession { Start = now.AddMinutes(-20), End = now, Seconds = 20 * 60 };
        string reminder = Wellbeing.ContinuousMessage(now, longSession);
        Check(reminder != null && reminder.Contains("连续使用"), "reminder above the threshold");
        Check(Wellbeing.ContinuousMessage(now, longSession) == null, "cooldown suppresses the next reminder");
        Check(Wellbeing.ContinuousMessage(now.AddMinutes(31), longSession) != null, "reminder returns after the cooldown");
        WellbeingSettings.LastReminder = DateTime.MinValue;
        Wellbeing.FullscreenProbe = delegate { return true; };
        Check(Wellbeing.ContinuousMessage(now, longSession) == null, "fullscreen suppresses the reminder");
        Wellbeing.FullscreenProbe = delegate { return false; };
        Check(Wellbeing.ContinuousMessage(now, longSession) != null, "reminder returns when not fullscreen");
        Reset();

        // ---- 每日摘要 ----
        Store.History.Clear();
        Store.Total = new Counters();
        Store.RollDay(DateTime.Today);
        Store.Today.Keys = 1234;
        Store.Today.Clicks = 56;
        Store.Today.ActiveSeconds = 3600;
        WellbeingSettings.SummaryEnabled = true;
        WellbeingSettings.SummaryHour = 22;
        Check(Wellbeing.SummaryMessage(new DateTime(2026, 9, 9, 21, 0, 0)) == null, "summary waits for its hour");
        string summary = Wellbeing.SummaryMessage(new DateTime(2026, 9, 9, 22, 30, 0));
        Check(summary != null && summary.Contains("1,234"), "summary reports today");
        Check(Wellbeing.SummaryMessage(new DateTime(2026, 9, 9, 23, 0, 0)) == null, "summary is sent once per day");
        Wellbeing.FullscreenProbe = delegate { return true; };
        WellbeingSettings.SummarySent = "";
        Check(Wellbeing.SummaryMessage(new DateTime(2026, 9, 9, 23, 0, 0)) == null, "fullscreen suppresses the summary");
        Reset();

        // ---- 达成统计 ----
        Store.History.Clear();
        Store.Total = new Counters();
        Store.RollDay(DateTime.Today);
        WellbeingSettings.GoalMode = WellbeingSettings.GoalKeys;
        WellbeingSettings.GoalTarget = 1000;
        for (int i = 0; i < 10; i++)
        {
            if (i == 4) continue;                      // 缺口
            DateTime date = DateTime.Today.AddDays(-1 - i);
            Store.History[date] = Day(date, i < 3 ? 2000 : 100, 0);   // 最近 3 天达成
        }
        int met, observed, streak;
        Wellbeing.GoalStats(DateTime.Today.AddDays(-10), DateTime.Today.AddDays(-1), out met, out observed, out streak);
        Check(observed == 9, "goal stats skip missing days");
        Check(met == 3, "goal stats count reached days");
        Check(streak == 3, "streak counts back from the end");
        WellbeingSettings.GoalMode = WellbeingSettings.GoalOff;
        Wellbeing.GoalStats(DateTime.Today.AddDays(-10), DateTime.Today.AddDays(-1), out met, out observed, out streak);
        Check(met == 0 && observed == 0 && streak == 0, "no goal means no goal stats");
        Reset();

        // ---- 全屏判断自身不能抛异常 ----
        bool probe = FullscreenWindow.Active();
        Check(probe == true || probe == false, "fullscreen probe returns a boolean");

        Store.History.Clear();
        Store.Total = new Counters();
        Store.RollDay(DateTime.Today);
        Console.WriteLine("PASS: " + _checks + " wellbeing checks");
    }
}
