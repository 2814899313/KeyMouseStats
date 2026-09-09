using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using KeyMouseStats;

internal static class ShortcutTests
{
    private static int _checks;
    private static void Check(bool pass, string name) { if (!pass) throw new Exception(name); _checks++; }
    private static string Down(ShortcutTracker tracker, int key) { return tracker.Process(Native.WM_KEYDOWN, key, 0, 0); }
    private static void Up(ShortcutTracker tracker, int key) { tracker.Process(Native.WM_KEYUP, key, 0, 0); }
    private static void Main()
    {
        ShortcutTracker tracker = new ShortcutTracker();
        bool firstPress;
        string plain = tracker.Process(Native.WM_KEYDOWN, 0x57, 0, 0, out firstPress);
        Check(firstPress && plain == null, "plain W first down counts without a combo");
        int repeatedPresses = 0;
        for (int i = 0; i < 300; i++)
        {
            tracker.Process(Native.WM_KEYDOWN, 0x57, 0, 0, out firstPress);
            if (firstPress) repeatedPresses++;
        }
        Check(repeatedPresses == 0, "holding W never adds single-key counts on autorepeat");
        tracker.Process(Native.WM_KEYDOWN, 0x41, 0, 0, out firstPress);
        Check(firstPress, "another key counts while W remains held");
        tracker.Process(Native.WM_KEYUP, 0x57, 0, 0, out firstPress);
        Check(!firstPress, "release itself never counts");
        tracker.Process(Native.WM_KEYDOWN, 0x57, 0, 0, out firstPress);
        Check(firstPress, "release then press W counts once more");
        tracker.Reset();
        tracker.Process(Native.WM_SYSKEYDOWN, 18, 0, 0, out firstPress);
        Check(firstPress, "Alt counts as a physical key press");
        tracker.Process(Native.WM_SYSKEYDOWN, 0xA4, 0, 0, out firstPress);
        Check(!firstPress, "normalized modifier repeat suppressed");
        Check(tracker.Process(Native.WM_SYSKEYDOWN, 9, 0, 0, out firstPress) == "alt_tab" && firstPress, "combo and single-key action share first-down decision");
        Check(tracker.Process(Native.WM_SYSKEYDOWN, 9, 0, 0, out firstPress) == null && !firstPress, "held Alt+Tab suppresses both counters");
        tracker.Process(Native.WM_SYSKEYUP, 9, 0, 0, out firstPress);
        Check(tracker.Process(Native.WM_SYSKEYDOWN, 9, 0, 0, out firstPress) == "alt_tab" && firstPress, "system key release rearms both counters");
        tracker.Seed(delegate(int key) { return key == 0x57; });
        tracker.Process(Native.WM_KEYDOWN, 0x57, 0, 0, out firstPress);
        Check(!firstPress, "startup-held key is not counted again");
        tracker.Process(Native.WM_KEYDOWN, 0x100, 0, 0, out firstPress);
        Check(!firstPress, "invalid key cannot enter physical-key totals");
        tracker.Reset();
        Check(Down(tracker, 0x43) == null, "plain key ignored"); Up(tracker, 0x43);
        Check(Down(tracker, 0xA2) == null, "modifier alone ignored");
        Check(Down(tracker, 0x43) == "ctrl_c", "Ctrl+C");
        for (int i = 0; i < 100; i++) Check(Down(tracker, 0x43) == null, "autorepeat suppressed");
        Up(tracker, 0x43);
        Check(Down(tracker, 0x43) == "ctrl_c", "release and repress counts again");
        Up(tracker, 0x43); Down(tracker, 0xA3); Up(tracker, 0xA2);
        Check(Down(tracker, 0x56) == "ctrl_v", "right Ctrl remains after left Ctrl released");
        Up(tracker, 0x56); Up(tracker, 0xA3);
        Check(Down(tracker, 0x56) == null, "no stale Ctrl after both released");
        tracker.Reset();
        tracker.Process(Native.WM_SYSKEYDOWN, 0xA4, 0, 0);
        Check(tracker.Process(Native.WM_SYSKEYDOWN, 9, 0, 0) == "alt_tab", "system Alt+Tab");
        tracker.Process(Native.WM_SYSKEYUP, 9, 0, 0);
        Check(tracker.Process(Native.WM_SYSKEYDOWN, 9, 0, 0) == "alt_tab", "Alt held across repeated Tab presses");
        tracker.Process(Native.WM_SYSKEYUP, 9, 0, 0); tracker.Process(Native.WM_SYSKEYUP, 0xA4, 0, 0);
        Check(Down(tracker, 9) == null, "system keyup clears Alt");
        tracker.Reset(); Down(tracker, 0x5C); Down(tracker, 0xA1);
        Check(Down(tracker, 0x53) == "win_shift_s", "Win+Shift+S with right modifiers");
        Check(ShortcutStats.Display("win_shift_s") == "Win+Shift+S", "display modifier order");
        tracker.Reset(); Down(tracker, 0xA0); Down(tracker, 0xA2);
        Check(Down(tracker, 0x53) == "ctrl_shift_s", "modifier press order canonicalized");
        tracker.Reset();
        Check(Down(tracker, 0x53) == null, "lock/suspend reset clears held state");
        tracker.Reset(); Down(tracker, 0x43); Down(tracker, 0xA2);
        Check(Down(tracker, 0x43) == null, "modifier added during ordinary-key hold does not fabricate action");
        tracker.Seed(delegate(int key) { return key == 0xA2 || key == 0x43; });
        Check(Down(tracker, 0x43) == null, "startup held key repeats suppressed"); Up(tracker, 0x43);
        Check(Down(tracker, 0x56) == "ctrl_v", "startup held modifier recognized");
        Check(ShortcutTracker.Normalize(16, 0x36, 0) == 0xA1 && ShortcutTracker.Normalize(17, 0, 1) == 0xA3
            && ShortcutTracker.Normalize(18, 0, 0) == 0xA4, "generic modifier normalization");
        tracker.Reset(); Down(tracker, 0xA2);
        Check(Down(tracker, 0x70) == "ctrl_vk70" && ShortcutStats.Display("ctrl_vk70") == "Ctrl+F1", "function key");
        Up(tracker, 0x70);
        Check(Down(tracker, 0x6B) == "ctrl_vk6b", "numpad distinct identity");
        Check(!ShortcutStats.IsValid("ctrl_ctrl_c") && !ShortcutStats.IsValid("banana_c")
            && !ShortcutStats.IsValid("shift_ctrl_s") && !ShortcutStats.IsValid("ctrl_vka2"), "malformed identifiers rejected");

        DayRecord first = new DayRecord { Date = DateTime.Today.AddDays(-1) };
        DayRecord second = new DayRecord { Date = DateTime.Today };
        ShortcutStats.Add(first, "ctrl_c"); ShortcutStats.Add(second, "ctrl_c"); ShortcutStats.Add(second, "alt_tab");
        ShortcutStats.Add(second, null);
        List<KeyValuePair<string, long>> rank = ShortcutStats.Ranking(new DayRecord[] { first, second });
        Check(rank.Count == 2 && rank[0].Key == "Ctrl+C" && rank[0].Value == 2, "multi-day ranking");
        Check(ShortcutStats.Ranking(new DayRecord[] { second })[0].Value == 1, "today scope");
        Check(second.Keys == 0 && second.KeyCounts.Count == 0, "combo counts do not inflate single-key totals");
        ShortcutStats.Load(second, "win_shift_s:5"); ShortcutStats.Load(second, "bad:55"); ShortcutStats.Load(second, "ctrl_x:-2");
        Check(second.ComboCounts.Count == 3 && second.ComboCounts["win_shift_s"] == 5, "load validation");
        Check(ShortcutStats.Export(new DayRecord[] { second }).Contains("\"Win+Shift+S\",win_shift_s,5"), "CSV full combo export");

        string directory = Path.Combine(Path.GetTempPath(), "ShortcutTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); string file = Path.Combine(directory, "stats.txt");
        Store.DataDirectory = directory;
        try
        {
            Store.History.Clear(); Store.Total = new Counters(); Store.RollDay(DateTime.Today);
            Store.Today.Keys = 12; Store.Today.KeyCounts[67] = 4;
            ShortcutStats.Add(Store.Today, "ctrl_c"); ShortcutStats.Add(Store.Today, "win_shift_s");
            Store.Save(); Store.History.Clear(); Store.RollDay(DateTime.Today); Store.Load();
            Check(Store.Today.ComboCounts.Count == 2 && Store.Today.ComboCounts["ctrl_c"] == 1, "v6 save/load");
            Check(Store.Today.Keys == 12 && Store.Today.KeyCounts[67] == 4, "existing key data preserved");
            File.WriteAllText(file, "[day=" + DateTime.Today.ToString("yyyy-MM-dd") + "]\nkeys=12\nkk=67:4\n");
            Store.History.Clear(); Store.RollDay(DateTime.Today); Store.Load();
            Check(Store.Today.ComboCounts.Count == 0 && Store.Today.Keys == 12, "legacy data has no invented combos");
        }
        finally
        {
            foreach (string name in new string[] { "stats.txt", "stats.txt.tmp", "stats.txt.bak" })
                if (File.Exists(Path.Combine(directory, name))) File.Delete(Path.Combine(directory, name));
            Directory.Delete(directory);
        }
        Console.WriteLine("PASS: " + _checks + " shortcut checks; " + (IntPtr.Size * 8) + "-bit");
    }
}
