// ============================================================================
//  tests/KeysHealthRender.cs  ·  v3.4.1 现代外设健康视图
//  100/150/175%、多键盘/多鼠标、编辑宿主生命周期与旧版归并。
// ============================================================================
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using KeyMouseStats;

internal static class KeysHealthRender
{
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); _checks++; }
    private static void SetField(object target, string name, object value) { target.GetType().GetField(name, Private).SetValue(target, value); }

    private static void Main()
    {
        Environment.ExitCode = KeyMouseStats.Tests.TestDiagnostics.Run("KeysHealthRender", Run);
    }

    private static KeyboardProfile AddKeyboard(string id, string alias, string friendly,
        bool builtIn, long rated, DateTime since, long handle)
    {
        KeyboardProfile pending = PeripheralWear.CreatePendingProfile(new IntPtr(handle));
        pending.Alias = alias;
        pending.Rated = rated;
        pending.Since = since;
        KeyboardIdentity identity = new KeyboardIdentity();
        identity.Id = id;
        identity.InstanceKey = "TEST\\" + id;
        identity.HardwareKey = "TEST\\" + id;
        identity.DevicePath = "TEST\\" + id;
        identity.FriendlyName = friendly;
        identity.BuiltIn = builtIn;
        KeyboardProfile profile = PeripheralWear.ResolvePending(pending, identity);
        profile.Alias = alias;
        profile.Rated = rated;
        profile.Since = since;
        profile.Seeded = true;
        return profile;
    }

    private static MouseProfile AddMouse(string id, string alias, string friendly,
        bool builtIn, long ratedLeft, long ratedRight, DateTime since, long handle, bool legacy)
    {
        MouseProfile pending = PeripheralMouseWear.CreatePendingProfile(new IntPtr(handle));
        pending.Alias = alias;
        pending.RatedLeft = ratedLeft;
        pending.RatedRight = ratedRight;
        pending.Since = since;
        MouseIdentity identity = new MouseIdentity();
        identity.Id = id;
        identity.InstanceKey = "MTEST\\" + id;
        identity.HardwareKey = "MTEST\\" + id;
        identity.DevicePath = "MTEST\\" + id;
        identity.FriendlyName = friendly;
        identity.BuiltIn = builtIn;
        MouseProfile profile = PeripheralMouseWear.ResolvePending(pending, identity);
        profile.Alias = alias;
        profile.RatedLeft = ratedLeft;
        profile.RatedRight = ratedRight;
        profile.Since = since;
        profile.Seeded = true;
        if (legacy)
        {
            profile.Id = PeripheralMouseWear.LegacyId;
            profile.Legacy = true;
        }
        return profile;
    }

    private static void Clicks(MouseProfile profile, DateTime day, int left, int right, int middle, int x)
    {
        for (int i = 0; i < left; i++) PeripheralMouseWear.CountClick(profile, 1, day);
        for (int i = 0; i < right; i++) PeripheralMouseWear.CountClick(profile, 2, day);
        for (int i = 0; i < middle; i++) PeripheralMouseWear.CountClick(profile, 3, day);
        for (int i = 0; i < x; i++) PeripheralMouseWear.CountClick(profile, 4, day);
    }

    private static void Run()
    {
        string output = Environment.GetEnvironmentVariable("CLICKHEAT_PREVIEW_DIR");
        if (string.IsNullOrEmpty(output)) output = "previews";
        string isolated = Path.Combine(Path.GetTempPath(), "KeysHealth-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolated);
        Directory.CreateDirectory(output);
        Store.DataDirectory = isolated;
        Store.ThemeId = 0;
        PeripheralWear.BeginLoad();

        KeyboardProfile builtIn = AddKeyboard("builtin:test", "笔记本键盘", "笔记本内置键盘",
            true, 1000, new DateTime(2025, 1, 1), 0x101);
        KeyboardProfile external = AddKeyboard("dev:test", "家里 · 外接键盘", "USB Keyboard",
            false, 1000, new DateTime(2025, 2, 1), 0x102);
        KeyboardProfile legacy = AddKeyboard("legacy", "旧版单键盘", "升级前累计",
            false, 1000, new DateTime(2024, 1, 1), 0x103);
        legacy.Legacy = true;
        for (int i = 0; i < 92; i++) PeripheralWear.CountKey(builtIn, 32);
        for (int i = 0; i < 61; i++) PeripheralWear.CountKey(builtIn, 13);
        for (int i = 0; i < 43; i++) PeripheralWear.CountKey(builtIn, 8);
        for (int i = 0; i < 25; i++) PeripheralWear.CountKey(builtIn, 65);
        for (int i = 0; i < 7; i++) PeripheralWear.CountKey(builtIn, 83);
        for (int i = 0; i < 40; i++) PeripheralWear.CountKey(external, 65);
        for (int i = 0; i < 18; i++) PeripheralWear.CountKey(external, 83);
        for (int i = 0; i < 31; i++) PeripheralWear.CountKey(legacy, 32);

        MouseProfile home = AddMouse("mouse:test:home", "家里 · MX Master 3S", "Logitech MX Master 3S",
            false, 100, 100, new DateTime(2025, 1, 1), 0x201, false);
        MouseProfile office = AddMouse("mouse:test:office", "公司 · G304", "Logitech G304",
            false, 100, 100, new DateTime(2025, 2, 1), 0x202, false);
        MouseProfile legacyMouse = AddMouse("mouse:test:legacy", "旧版单鼠标", "升级前累计",
            false, 100, 100, new DateTime(2024, 1, 1), 0x203, true);
        DateTime today = DateTime.Today;
        int[] leftDays = { 10, 12, 11, 13, 12, 14, 13 };
        int[] rightDays = { 8, 9, 10, 9, 10, 11, 11 };
        for (int i = 0; i < 7; i++) Clicks(home, today.AddDays(i - 6), leftDays[i], rightDays[i], 0, 0);
        Clicks(home, today, 0, 0, 12, 4);
        Clicks(office, today, 30, 20, 2, 1);
        Clicks(legacyMouse, today, 10, 8, 1, 0);
        Check(home.DayStamp[0] != 0 && home.DayLeft[0] == 10, "7 天点击环保留最早一天");

        using (Dashboard form = new Dashboard())
        {
            typeof(Dashboard).GetMethod("OnLoad", Private).Invoke(form, new object[] { EventArgs.Empty });
            object keys = Enum.ToObject(typeof(Dashboard).GetNestedType("TabId", BindingFlags.NonPublic), 3);
            SetField(form, "_tab", keys);
            SetField(form, "_s", 1f);
            form.ClientSize = new Size(1072, 724);
            typeof(Dashboard).GetMethod("ApplyChip", Private).Invoke(form, new object[] { Dashboard.KeySubviewHealthChipId });
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-20000, -20000);
            form.Show();
            Application.DoEvents();
            Render(form, Path.Combine(output, "keys-health-100.png"));

            SetField(form, "_s", 1.5f);
            form.ClientSize = new Size(1608, 1000);
            typeof(Dashboard).GetMethod("LayoutHealthHosts", Private).Invoke(form, null);
            Application.DoEvents();
            Render(form, Path.Combine(output, "keys-health-150.png"));

            SetField(form, "_s", 1.75f);
            form.ClientSize = new Size(1876, 1000);
            typeof(Dashboard).GetMethod("LayoutHealthHosts", Private).Invoke(form, null);
            Application.DoEvents();
            Render(form, Path.Combine(output, "keys-health-175.png"));

            SetField(form, "_s", 1f);
            form.ClientSize = new Size(1072, 724);
            typeof(Dashboard).GetMethod("ApplyChip", Private).Invoke(form, new object[] { Dashboard.HealthDeviceChipBase });
            typeof(Dashboard).GetMethod("ApplyChip", Private).Invoke(form, new object[] { Dashboard.MouseHealthChipBase });
            Application.DoEvents();
            Render(form, Path.Combine(output, "keys-health-all-100.png"));
            typeof(Dashboard).GetMethod("ApplyChip", Private).Invoke(form, new object[] { Dashboard.HealthDeviceChipBase + 1 });
            typeof(Dashboard).GetMethod("ApplyChip", Private).Invoke(form, new object[] { Dashboard.MouseHealthChipBase + 1 });

            // legacy 键盘归并。
            SetField(form, "_healthKeyboardId", "legacy");
            typeof(Dashboard).GetMethod("RefreshHealthSnapshot", Private).Invoke(form, null);
            Check((bool)typeof(Dashboard).GetMethod("CanMergeLegacyKeyboard", Private).Invoke(form, null), "legacy 键盘显示归入口");
            typeof(Dashboard).GetMethod("ApplyChip", Private).Invoke(form, new object[] { Dashboard.HealthMergeLegacyChipId });
            Check(PeripheralWear.FindKeyboard("legacy") == null, "合并后 legacy 键盘移除");
            KeyboardProfile merged = PeripheralWear.FindKeyboard("builtin:test");
            Check(merged != null && merged.ReadKey(32) == 123, "旧版空格计数并入当前键盘");

            // legacy 鼠标归并。
            SetField(form, "_healthMouseId", PeripheralMouseWear.LegacyId);
            typeof(Dashboard).GetMethod("RefreshHealthSnapshot", Private).Invoke(form, null);
            Check((bool)typeof(Dashboard).GetMethod("CanMergeLegacyMouse", Private).Invoke(form, null), "legacy 鼠标显示归入口");
            typeof(Dashboard).GetMethod("ApplyChip", Private).Invoke(form, new object[] { Dashboard.MouseHealthMergeChipId });
            Check(PeripheralMouseWear.Find(PeripheralMouseWear.LegacyId) == null, "合并后 legacy 鼠标移除");
            Check(home.Left == 95 && home.Right == 76, "旧版鼠标计数并入当前鼠标");

            // 设备信息宿主按需创建/销毁。
            typeof(Dashboard).GetMethod("ShowHealthInfo", Private).Invoke(form, new object[] { false });
            Check(typeof(Dashboard).GetField("_healthInfoHost", Private).GetValue(form) != null, "设备信息宿主创建");
            Render(form, Path.Combine(output, "keys-health-info.png"));
            typeof(Dashboard).GetMethod("CloseHealthPanels", Private).Invoke(form, new object[] { false });
            Check(typeof(Dashboard).GetField("_healthInfoHost", Private).GetValue(form) == null, "设备信息宿主销毁");

            // 换新确认条。
            SetField(form, "_healthResetTarget", 1);
            Render(form, Path.Combine(output, "keys-health-reset.png"));
            typeof(Dashboard).GetMethod("ApplyChip", Private).Invoke(form, new object[] { Dashboard.HealthResetCancelChipId });
            Check((int)typeof(Dashboard).GetField("_healthResetTarget", Private).GetValue(form) == 0, "取消换新关闭确认条");

            // 编辑宿主按需创建,切回按键统计时提交并销毁。
            typeof(Dashboard).GetMethod("ApplyChip", Private).Invoke(form, new object[] { Dashboard.HealthKeyboardEditChipId });
            Check(typeof(Dashboard).GetField("_healthEditHost", Private).GetValue(form) != null, "编辑键盘时创建宿主");
            Application.DoEvents();
            Render(form, Path.Combine(output, "keys-health-edit.png"));
            typeof(Dashboard).GetMethod("ApplyChip", Private).Invoke(form, new object[] { Dashboard.KeySubviewStatsChipId });
            Check(typeof(Dashboard).GetField("_healthEditHost", Private).GetValue(form) == null, "切回按键统计后销毁编辑宿主");

            form.Hide();
            typeof(Dashboard).GetMethod("OnFormClosed", Private).Invoke(form, new object[] { new FormClosedEventArgs(CloseReason.None) });
        }

        Console.WriteLine("KeysHealthRender: " + _checks + " 项断言通过");
    }

    private static void Render(Dashboard form, string path)
    {
        using (Bitmap bitmap = new Bitmap(Math.Max(1, form.ClientSize.Width), Math.Max(1, form.ClientSize.Height)))
        {
            form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
            bitmap.Save(path, ImageFormat.Png);
        }
    }
}
