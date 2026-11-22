// ============================================================================
//  tests/FocusBreakToastTests.cs  ·  v3.3 微休息提示几何、按钮联动与渲染
//  不显示真实窗口;按钮回调直接接到 FocusBreak 状态机。
// ============================================================================
using System;
using System.Drawing;
using KeyMouseStats;

internal static class FocusBreakToastTests
{
    private static int _checks;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); _checks++; }
    private static void Equal(long actual, long expected, string name)
    {
        Check(actual == expected, name + " (expected " + expected + ", got " + actual + ")");
    }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("FocusBreakToastTests", Run);
    }

    private static void Run()
    {
        Layout();
        ButtonActions();
        Render();
        Console.WriteLine("FocusBreakToastTests: " + _checks + " 项断言通过");
    }

    private static void Layout()
    {
        Size size = FocusBreakToastLayout.Size(1f);
        Equal(size.Width, 360, "Toast 宽度");
        Equal(size.Height, 132, "Toast 高度");
        Size scaled = FocusBreakToastLayout.Size(1.5f);
        Equal(scaled.Width, 540, "1.5x Toast 宽度");
        Equal(scaled.Height, 198, "1.5x Toast 高度");

        Rectangle work = new Rectangle(0, 0, 1920, 1080);
        Rectangle bounds = FocusBreakToastLayout.ToastBounds(work, 1f);
        Check(work.Contains(bounds), "Toast 必须落在工作区内");
        RectangleF snooze = FocusBreakToastLayout.SnoozeRect(size, 1f);
        RectangleF mute = FocusBreakToastLayout.MuteRect(size, 1f);
        RectangleF close = FocusBreakToastLayout.CloseRect(size, 1f);
        Equal(FocusBreakToastLayout.HitTest(size, 1f,
            new Point((int)(snooze.X + snooze.Width / 2), (int)(snooze.Y + snooze.Height / 2))), 1, "延后按钮命中");
        Equal(FocusBreakToastLayout.HitTest(size, 1f,
            new Point((int)(mute.X + mute.Width / 2), (int)(mute.Y + mute.Height / 2))), 2, "静音按钮命中");
        Equal(FocusBreakToastLayout.HitTest(size, 1f,
            new Point((int)(close.X + close.Width / 2), (int)(close.Y + close.Height / 2))), 3, "关闭按钮命中");
        Equal(FocusBreakToastLayout.HitTest(size, 1f, new Point(2, 2)), 0, "空白区不命中");
    }

    private static void ButtonActions()
    {
        DateTime now = new DateTime(2026, 11, 21, 16, 0, 0);
        FocusBreak monitor = new FocusBreak();
        bool snoozed = false;
        bool muted = false;
        using (FocusBreakToast toast = new FocusBreakToast(
            delegate { snoozed = true; monitor.Snooze(now); },
            delegate { muted = true; monitor.MuteToday(now); }))
        {
            toast.ActivateActionForTest(1);
            Check(snoozed, "延后按钮必须触发回调");
            Check(monitor.IsSnoozed(now), "延后按钮必须写入 FocusBreak.SnoozeUntil");
            toast.ActivateActionForTest(2);
            Check(muted, "静音按钮必须触发回调");
            Check(monitor.IsMuted(now), "静音按钮必须写入 FocusBreak.MutedDay");
            Check(!monitor.IsSnoozed(now), "静音后延后状态不再有意义");
        }
    }

    private static void Render()
    {
        int savedTheme = Store.ThemeId;
        try
        {
            Store.ThemeId = 0;
            using (FocusBreakToast toast = new FocusBreakToast(null, null))
            {
                toast.PrepareForTest(45, "Code.exe", 1f);
                using (Bitmap bitmap = new Bitmap(toast.Width, toast.Height))
                {
                    toast.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                    Check(bitmap.Width == 360 && bitmap.Height == 132, "Toast 离屏尺寸");
                }
                toast.PrepareForTest(45, "Code.exe", 1.5f);
                using (Bitmap bitmap = new Bitmap(toast.Width, toast.Height))
                {
                    toast.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                    Check(bitmap.Width == 540 && bitmap.Height == 198, "1.5x Toast 离屏尺寸");
                }
            }
        }
        finally
        {
            Store.ThemeId = savedTheme;
        }
    }
}
