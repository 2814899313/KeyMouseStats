using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using KeyMouseStats;

/// <summary>
/// 1.8.0:动效时间轴回归。全部用假时钟推进,不依赖真实帧率:
/// 缓动、通道生命周期、档位降级、硬停、局部重绘范围、追值收敛,以及设置项的落盘与读回。
/// </summary>
internal static class MotionTests
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

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("MotionTests", Run);
    }

    private static void Run()
    {
        // ---- 缓动 ----
        Near(Ease.Apply(Ease.OutCubic, 0), 0, 1e-9, "out cubic starts at 0");
        Near(Ease.Apply(Ease.OutCubic, 1), 1, 1e-9, "out cubic ends at 1");
        Near(Ease.Apply(Ease.Linear, 0.25), 0.25, 1e-9, "linear stays linear");
        Check(Ease.Apply(Ease.OutCubic, 0.5) > 0.5, "out cubic front-loads the progress");
        Check(Ease.Apply(Ease.InOutQuad, 0.25) < 0.25, "in-out quad eases in first");
        Check(Ease.Apply(Ease.OutQuad, -5) == 0 && Ease.Apply(Ease.OutQuad, 9) == 1, "progress clamps outside 0..1");

        // ---- 通道:按真实时间推进,与帧率无关 ----
        Motion.ResetForTests();
        Motion.OverrideSystemReducedMotion(false);
        int savedLevel = Store.MotionLevel;
        bool savedFollow = Store.MotionFollowSystem;
        bool savedHotkey = Store.GlobalHotkey;
        Store.MotionLevel = Motion.LevelFull;
        Store.MotionFollowSystem = false;

        Motion.UseFakeClock(1000000);
        Motion.Start("probe", 200, Ease.Linear);
        Near(Motion.Progress("probe"), 0, 1e-9, "a fresh channel starts at 0");
        Check(Motion.Running("probe") && Motion.Active, "a fresh channel is running");
        Motion.AdvanceFakeClock(100);
        Near(Motion.Progress("probe"), 0.5, 1e-9, "half the duration is half the progress");
        Motion.AdvanceFakeClock(100);
        Near(Motion.Progress("probe"), 1, 1e-9, "the channel reaches 1");
        Check(!Motion.Running("probe") && !Motion.Active, "a finished channel stops driving frames");
        Near(Motion.Progress("unknown"), 1, 1e-9, "an unknown channel reports done, so callers draw the final state");
        Check(Motion.Linear("unknown") == 1, "an unknown channel reports a finished timeline");

        // ---- 重启通道:悬停脉冲这类反复触发 ----
        Motion.Start("pulse", 100, Ease.Linear);
        Motion.AdvanceFakeClock(80);
        Motion.Start("pulse", 100, Ease.Linear);
        Near(Motion.Progress("pulse"), 0, 1e-9, "restarting a channel rewinds it");
        Motion.AdvanceFakeClock(100);
        Near(Motion.Progress("pulse"), 1, 1e-9, "the restarted channel finishes on time");

        // ---- 一次性庆祝:播完就结束,不会自己重播(1.8.0 目标达成用) ----
        Motion.StopAll();
        Motion.Start("celebrate", 900, Ease.OutCubic);
        Check(Motion.Running("celebrate") && Motion.Active, "the celebration starts running");
        Motion.AdvanceFakeClock(450);
        Check(Motion.Progress("celebrate") > 0.5, "out cubic is already past half way at the half point");
        Motion.AdvanceFakeClock(450);
        Near(Motion.Progress("celebrate"), 1, 1e-9, "the celebration reaches the end");
        Check(!Motion.Running("celebrate") && !Motion.Active, "a celebration is one shot and stops driving frames");
        Motion.AdvanceFakeClock(5000);
        Check(!Motion.Running("celebrate"), "a finished celebration never re-arms itself");

        // ---- 局部重绘范围 ----
        Motion.StopAll();
        Rectangle whole = new Rectangle(0, 0, 1000, 700);
        Check(Motion.InvalidateRegion(whole) == Rectangle.Empty, "no channels means nothing to repaint");
        Motion.Start("a", 100, Ease.Linear, new Rectangle(10, 20, 30, 40));
        Motion.Start("b", 100, Ease.Linear, new Rectangle(100, 200, 50, 60));
        Check(Motion.InvalidateRegion(whole) == Rectangle.Union(new Rectangle(10, 20, 30, 40), new Rectangle(100, 200, 50, 60)),
            "two rect channels union their repaint region");
        Motion.Start("page", 100, Ease.Linear);
        Check(Motion.InvalidateRegion(whole) == whole, "a page-level channel asks for the whole window");

        // ---- 停表与清理 ----
        Motion.Paused = true;
        Check(!Motion.Active, "a paused timeline does not ask for frames");
        Motion.Paused = false;
        Motion.AdvanceFakeClock(200);
        Motion.Sweep();
        Check(Motion.ActiveCount == 0, "sweeping drops the finished channels");

        // ---- 档位:关闭 / 精简 / 跟随系统 ----
        Store.MotionLevel = Motion.LevelOff;
        Motion.Start("off", 500, Ease.Linear);
        Near(Motion.Progress("off"), 1, 1e-9, "with motion off a channel is immediately done");
        Check(!Motion.Active && !Motion.Enabled, "with motion off nothing drives frames");
        Store.MotionLevel = Motion.LevelReduced;
        Motion.StopAll();
        Motion.Start("reduced", 500, Ease.Linear);
        Motion.AdvanceFakeClock(120);
        Near(Motion.Progress("reduced"), 1, 1e-9, "the reduced level caps durations at 120 ms");
        Store.MotionLevel = Motion.LevelFull;
        Store.MotionFollowSystem = true;
        Motion.OverrideSystemReducedMotion(true);
        Check(Motion.Level == Motion.LevelReduced, "the system reduce-motion preference downgrades the level");
        Store.MotionFollowSystem = false;
        Check(Motion.Level == Motion.LevelFull, "without the follow flag the user level wins");
        Motion.OverrideSystemReducedMotion(false);
        Check(Motion.AllowsPageMotion, "the full level allows page level motion");
        Store.MotionLevel = Motion.LevelReduced;
        Check(!Motion.AllowsPageMotion, "the reduced level keeps page motion out");
        Store.MotionLevel = Motion.LevelFull;
        Check(Motion.LevelName(Motion.LevelOff) == "关闭" && Motion.LevelName(Motion.LevelReduced) == "精简" && Motion.LevelName(Motion.LevelFull) == "完整",
            "level names are user facing");

        // ---- 帧间隔:目标 16–40 ms,慢帧留余量 ----
        Check(Motion.FrameInterval(0) == 16, "an unknown frame time floors the interval");
        Check(Motion.FrameInterval(1) == 16, "a cheap frame does not spin faster than 60 fps");
        Check(Motion.FrameInterval(20) == 28, "the next interval follows the measured frame cost");
        Check(Motion.FrameInterval(1000) == 40, "an expensive frame falls back to 25 fps");

        // ---- 追值:按半衰期收敛,与帧率无关 ----
        Motion.ForgetChase("chase");
        Near(Motion.Approach("chase", 10, 100), 10, 1e-9, "the first chase sample lands on the target");
        Motion.AdvanceFakeClock(100);
        Near(Motion.Approach("chase", 20, 100), 15, 0.5, "one half life covers half the remaining distance");
        Check(Motion.ChaseActive("chase", 20, 0.5), "the chase reports that it is still moving");
        for (int i = 0; i < 40; i++) { Motion.AdvanceFakeClock(100); Motion.Approach("chase", 20, 100); }
        Near(Motion.Approach("chase", 20, 100), 20, 0.01, "the chase settles on the target");
        Check(!Motion.ChaseActive("chase", 20, 0.01), "a settled chase stops asking for frames");
        Motion.ForgetChase("chase");
        Near(Motion.ChaseValue("chase", 7), 7, 1e-9, "a forgotten chase falls back");

        Motion.ResetForTests();
        Store.MotionLevel = savedLevel;
        Store.MotionFollowSystem = savedFollow;
        Store.GlobalHotkey = savedHotkey;

        // ---- 设置项落盘与读回(解析层,不碰磁盘) ----
        Store.Parsed parsed = Store.ParseText("format=11\nmotion_v1=1|0|1\n");
        Check(parsed.HasMotion && parsed.MotionLevel == Motion.LevelReduced, "motion level parses");
        Check(!parsed.MotionFollowSystem && parsed.GlobalHotkey, "follow-system and hotkey flags parse");
        Store.Parsed legacy = Store.ParseText("format=11\nart_theme=0\n");
        Check(!legacy.HasMotion, "a file without the motion line keeps the current settings");
        Check(legacy.MotionLevel == Motion.LevelFull && legacy.MotionFollowSystem, "defaults favour the full level and following the system");
        Store.Parsed broken = Store.ParseText("format=11\nmotion_v1=9|1|0\n");
        Check(broken.MotionLevel == Motion.LevelFull, "an out-of-range level falls back to full");

        // ---- 真实落盘往返(用新的数据目录,避免 File.Replace) ----
        string directory = Path.Combine(Path.GetTempPath(), "MotionTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Store.DataDirectory = directory;
        try
        {
            Store.MotionLevel = Motion.LevelReduced;
            Store.MotionFollowSystem = false;
            Store.GlobalHotkey = true;
            Store.Save();
            Store.MotionLevel = Motion.LevelFull;
            Store.MotionFollowSystem = true;
            Store.GlobalHotkey = false;
            Store.Load();
            Check(Store.MotionLevel == Motion.LevelReduced && !Store.MotionFollowSystem && Store.GlobalHotkey,
                "motion settings survive a save/load round trip");
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch { }
        }

        Console.WriteLine("PASS: " + _checks + " motion checks");
    }
}
