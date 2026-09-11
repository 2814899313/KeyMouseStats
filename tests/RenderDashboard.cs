using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using KeyMouseStats;

internal static class RenderDashboard
{
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Set(Dashboard form, string name, object value)
    {
        typeof(Dashboard).GetField(name, Private).SetValue(form, value);
    }
    // Async theme image loading is decoupled via ThemeImages.Get+
    // background decode; render checks must wait deterministically instead
    // of racing the worker thread (resource is embedded, so it must resolve).
    private static void WaitFor(Form form, string resource)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (ThemeImages.Get(resource) == null)
        {
            if (DateTime.UtcNow > deadline) throw new Exception("Image never loaded: " + resource);
            Application.DoEvents();
            System.Threading.Thread.Sleep(10);
        }
    }
    private static void WaitTheme(Form form, string[] resources)
    {
        foreach (string r in resources) WaitFor(form, r);
    }
    [STAThread]
    private static void Main()
    {
        try { Run(); }
        catch (Exception error)
        {
            // The CLR's own unhandled-exception printer fails on this console, so write the
            // detail to a file instead of losing it.
            try
            {
                File.WriteAllText("render-failure.txt",
                    error.GetType().FullName + ": " + error.Message + Environment.NewLine + error.StackTrace,
                    new UTF8Encoding(false));
            }
            catch { }
            throw;
        }
    }

    private static void Run()
    {
        if(Dashboard.DistributionStart(new DateTime(2026,9,6),1)!=new DateTime(2026,8,31) || Dashboard.DistributionStart(new DateTime(2026,9,7),1)!=new DateTime(2026,9,7) || Dashboard.DistributionStart(new DateTime(2026,9,7),2)!=new DateTime(2026,9,1) || Dashboard.DistributionStart(new DateTime(2026,9,7),3)!=new DateTime(2026,1,1))throw new Exception("Distribution calendar boundaries");
        RectangleF ringTest=new RectangleF(0,0,100,100);long[] slices={1,1,0,2};
        if(Dashboard.DistributionHit(new PointF(50,0),ringTest,10,slices)!=0 || Dashboard.DistributionHit(new PointF(100,50),ringTest,10,slices)!=1 || Dashboard.DistributionHit(new PointF(50,100),ringTest,10,slices)!=3 || Dashboard.DistributionHit(new PointF(50,50),ringTest,10,slices)!=-1 || Dashboard.DistributionHit(new PointF(200,200),ringTest,10,slices)!=-1 || Dashboard.DistributionHit(new PointF(50,0),ringTest,10,new long[4])!=-1)throw new Exception("Ring hit testing");
        // Isolated sample data. Do not call Store.Load/Save or install any input hooks.
        Store.History.Clear();
        Store.Total = new Counters();
        Store.MouseDpi = 1600;
        Store.IdleThresholdSeconds = 60;
        ActivityMonitor.Status = "活跃";
        AppActivity.Rules.Clear();
        for (int i = 29; i >= 0; i--)
        {
            DayRecord day = new DayRecord();
            day.Date = DateTime.Today.AddDays(-i);
            day.Keys = 4700 + (i * 1793 % 6100);
            day.Clicks = 820 + (i * 277 % 1200);
            day.Left = day.Clicks * 8 / 10;
            day.Right = day.Clicks - day.Left - 25;
            day.Middle = 20; day.XButtons = 5;
            day.Wheel = 215 + i * 7;
            day.MoveMeters = 8.3 + (i * 11 % 80) / 10.0;
            day.ActiveSeconds = 6300; day.IdleSeconds = 1200;
            day.ActiveHours[9] = 3000; day.ActiveHours[10] = 2400; day.ActiveHours[11] = 900;
            day.Sessions.Add(new ActiveSession { Start = day.Date.AddHours(9).AddMinutes(10), End = day.Date.AddHours(10).AddMinutes(5), Seconds = 3300 });
            day.Sessions.Add(new ActiveSession { Start = day.Date.AddHours(10).AddMinutes(25), End = day.Date.AddHours(11).AddMinutes(15), Seconds = 3000 });
            day.IdlePeriods.Add(new ActiveSession { Start = day.Date.AddHours(10).AddMinutes(5), End = day.Date.AddHours(10).AddMinutes(25), Seconds = 1200 });
            string[] processes = { @"C:\Apps\Code.exe", @"C:\Games\cs2.exe", @"C:\Apps\chrome.exe", @"C:\Apps\chrome.exe",
                @"C:\Apps\WeChat.exe", @"C:\Windows\explorer.exe", @"C:\Apps\wps.exe", @"C:\Apps\Code.exe" };
            string[] titles = { "Dashboard.cs — 键鼠统计 — Visual Studio Code", "Counter-Strike 2", "设计课程 第 03 集 — 视频播放",
                "Windows API 开发文档", "微信", "项目文件夹", "季度报告.docx — WPS", "README.md — 项目说明" };
            int[] weights = { 40, 25, 10, 8, 6, 5, 4, 2 };
            long remainingKeys = day.Keys, remainingClicks = day.Clicks, remainingWheel = day.Wheel;
            day.HoldCount = 900; day.HoldTotalMs = 900 * 124; day.HoldMaxMs = 664;
            day.HoldActiveSeconds = 78;   // 区间并集:小于逐键之和(111.6 s)
            day.HoldBuckets[0] = 120; day.HoldBuckets[1] = 260; day.HoldBuckets[2] = 300;
            day.HoldBuckets[3] = 160; day.HoldBuckets[4] = 60;
            int[] holdKeys = { 87, 65, 83, 68, 32, 69, 70, 16 };
            for (int k = 0; k < holdKeys.Length; k++)
            {
                int samples = 120 - k * 10;
                day.Holds[holdKeys[k]] = new KeyHold { Count = samples, TotalMs = samples * (90 + k * 22), MaxMs = 90 + k * 22 + 40 };
            }
            for (int a = 0; a < processes.Length; a++)
            {
                AppUsage usage = new AppUsage { ProcessPath = processes[a], Title = titles[a],
                    Keys = a == 7 ? remainingKeys : day.Keys * weights[a] / 100,
                    Clicks = a == 7 ? remainingClicks : day.Clicks * weights[a] / 100,
                    Wheel = a == 7 ? remainingWheel : day.Wheel * weights[a] / 100 };
                remainingKeys -= usage.Keys; remainingClicks -= usage.Clicks; remainingWheel -= usage.Wheel;
                usage.HasKeyGroups = true;
                usage.KeyGroups[0] = usage.Keys * 35 / 100;
                usage.KeyGroups[1] = usage.Keys * 20 / 100;
                usage.KeyGroups[2] = usage.Keys * 12 / 100;
                usage.KeyGroups[3] = usage.Keys * 18 / 100;
                usage.KeyGroups[4] = usage.Keys - usage.KeyGroups[0] - usage.KeyGroups[1] - usage.KeyGroups[2] - usage.KeyGroups[3];
                day.Apps[usage.Id] = usage;
                if (a == 2) AppActivity.Rules[AppActivity.WindowRuleKey(usage)] = 2;
                if (a == 3) AppActivity.Rules[AppActivity.WindowRuleKey(usage)] = 0;
                if (a == 4 || a == 5) AppActivity.Rules[AppActivity.AppRuleKey(usage.ProcessPath)] = 3;
            }
            for (int h = 0; h < 24; h++)
            {
                day.HourKeys[h] = h >= 8 && h <= 22 ? (h * 173 + i * 43) % 800 : 0;
                day.HourClicks[h] = day.HourKeys[h] / 5;
            }
            int[] keys = { 32, 69, 65, 83, 84, 13, 8, 160, 162, 9 };
            for (int k = 0; k < keys.Length; k++) day.KeyCounts[keys[k]] = day.Keys / (k + 3);
            string[] combos = { "ctrl_c", "ctrl_v", "alt_tab", "ctrl_s", "win_shift_s", "ctrl_shift_s", "ctrl_z", "ctrl_f", "ctrl_tab", "ctrl_alt_win_shift_vk7b" };
            for (int k = 0; k < combos.Length; k++) day.ComboCounts[combos[k]] = 200 - k * 18;
            Store.History[day.Date] = day;
            Store.Total.Keys += day.Keys; Store.Total.Clicks += day.Clicks;
            Store.Total.Left += day.Left; Store.Total.Right += day.Right;
            Store.Total.Middle += day.Middle; Store.Total.XButtons += day.XButtons;
            Store.Total.MoveMeters += day.MoveMeters;
        }
        Store.RollDay(DateTime.Today);
        Directory.CreateDirectory("previews");
        using (Dashboard form = new Dashboard())
        {
            typeof(Dashboard).GetMethod("OnLoad", Private).Invoke(form, new object[] { EventArgs.Empty });
            Set(form, "_s", 1f);
            form.ClientSize = new Size(1072, 724);
            string[] names = { "overview", "trend", "hours", "keys", "apps" };
            for (int i = 0; i < 5; i++)
            {
                FieldInfo tab = typeof(Dashboard).GetField("_tab", Private);
                tab.SetValue(form, Enum.ToObject(tab.FieldType, i));
                Render(form, names[i], 1f);
                Render(form, names[i] + "-150", 1.5f);
            }
            // Exercise translated chip hit targets after multiple paints.
            FieldInfo tabField = typeof(Dashboard).GetField("_tab", Private);
            tabField.SetValue(form, Enum.ToObject(tabField.FieldType, 3));
            Render(form, "keys", 1f);
            typeof(Dashboard).GetMethod("OnMouseClick", Private).Invoke(form,
                new object[] { new MouseEventArgs(MouseButtons.Left, 1, 985, 174, 0) });
            if (!(bool)typeof(Dashboard).GetField("_showCombos", Private).GetValue(form)) throw new Exception("Combo view hit target failed");
            Render(form, "combos", 1f); Render(form, "combos-150", 1.5f);
            Set(form, "_showKeyboardHeatmap", true);
            for(int layout=1;layout<=5;layout++)
            {
                Store.KeyboardLayout=layout;
                Render(form,"keyboard-layout-"+layout,1f);
            }
            Store.KeyboardLayout=1;
            for(int theme=0;theme<ArtTheme.All.Length;theme++) { Store.ThemeId=theme; Render(form,"keyboard-theme-"+theme,1f); }
            Store.ThemeId=0;Render(form,"keyboard-150",1.5f);
            // 1.7.6:热力图口径切到「累计按住时长」,并验证按住时长的悬停明细。
            Set(form,"_keyboardHoldMode",true);
            Render(form,"keyboard-hold",1f);
            Set(form,"_mouse",new Point(350,428));
            Render(form,"keyboard-hold-hover",1f);
            Set(form,"_mouse",new Point(-100,-100));
            // 1.8.0:点击热力图上的一个键下钻(三格统计切成该键),再按 Esc 回到全部按键。
            Render(form,"keyboard-drill-base",1f);   // 先画一帧,填充命中矩形与扫描码表
            List<RectangleF> caps=(List<RectangleF>)typeof(Dashboard).GetField("_keyboardHoverRects",Private).GetValue(form);
            List<int> caps2=(List<int>)typeof(Dashboard).GetField("_keyboardScans",Private).GetValue(form);
            int keyIndex=-1;
            for(int i=0;i<caps2.Count;i++) if(caps2[i]==17) { keyIndex=i; break; }   // W 键
            if(keyIndex<0) throw new Exception("keyboard hit rectangles were not recorded");
            PointF cap=new PointF(caps[keyIndex].X+caps[keyIndex].Width/2,caps[keyIndex].Y+caps[keyIndex].Height/2);
            Point capClick=new Point((int)(cap.X+176),(int)(cap.Y+64));
            typeof(Dashboard).GetMethod("OnMouseClick",Private).Invoke(form,new object[]{new MouseEventArgs(MouseButtons.Left,1,capClick.X,capClick.Y,0)});
            if((int)typeof(Dashboard).GetField("_selectedKeyScan",Private).GetValue(form)!=17)
                throw new Exception("keyboard drill-down hit target failed");
            if(((string)typeof(Dashboard).GetField("_crumb",Private).GetValue(form)).Length==0)
                throw new Exception("keyboard drill-down did not set the breadcrumb");
            Render(form,"keyboard-drill",1f);
            typeof(Dashboard).GetMethod("OnKeyDown",Private).Invoke(form,new object[]{new KeyEventArgs(Keys.Escape)});
            if((int)typeof(Dashboard).GetField("_selectedKeyScan",Private).GetValue(form)!=-1)
                throw new Exception("escape did not leave the drilled key");
            Set(form,"_keyboardHoldMode",false);
            Set(form,"_showKeyboardHeatmap",false);
            // 1.8.0:键盘导航(Tab 选筛选项 / Enter 激活 / 方向键翻页)与 Ctrl+C 复制。
            typeof(Dashboard).GetMethod("OnKeyDown",Private).Invoke(form,new object[]{new KeyEventArgs(Keys.Tab)});
            int focus=(int)typeof(Dashboard).GetField("_chipFocus",Private).GetValue(form);
            if(focus<0) throw new Exception("tab did not focus a filter chip");
            List<int> chipIds=(List<int>)typeof(Dashboard).GetField("_chipIds",Private).GetValue(form);
            int heatIndex=-1;
            for(int i=0;i<chipIds.Count;i++) if(chipIds[i]==74) { heatIndex=i; break; }
            if(heatIndex<0) throw new Exception("the keyboard heatmap chip was not registered");
            typeof(Dashboard).GetField("_chipFocus",Private).SetValue(form,heatIndex);
            typeof(Dashboard).GetMethod("OnKeyDown",Private).Invoke(form,new object[]{new KeyEventArgs(Keys.Enter)});
            if(!(bool)typeof(Dashboard).GetField("_showKeyboardHeatmap",Private).GetValue(form))
                throw new Exception("enter did not activate the focused chip");
            Render(form,"keyboard-nav-focus",1f);
            Set(form,"_mouse",new Point(350,428));
            Render(form,"keys-hover-detail",1f);
            string copied=(string)typeof(Dashboard).GetMethod("CopyText",Private).Invoke(form,null);
            if(string.IsNullOrEmpty(copied)) throw new Exception("ctrl+c copied nothing");
            Set(form,"_mouse",new Point(-100,-100));
            Set(form,"_showKeyboardHeatmap",false);
            int before=(int)Enum.ToObject(tabField.FieldType,tabField.GetValue(form));
            typeof(Dashboard).GetMethod("OnKeyDown",Private).Invoke(form,new object[]{new KeyEventArgs(Keys.Right)});
            if((int)Enum.ToObject(tabField.FieldType,tabField.GetValue(form))==before)
                throw new Exception("the right arrow did not change page");
            // 1.8.0:换页淡入的关键帧预览。离屏渲染默认不做动效,这里用假时钟显式推进时间轴,
            // 于是同一段动画可以稳定地取到 0% / 50% / 100% 三帧。
            Motion.ResetForTests();
            Motion.UseFakeClock(100000);
            Motion.Live = true;
            FieldInfo paintedTab = typeof(Dashboard).GetField("_paintedTab", Private);
            tabField.SetValue(form, Enum.ToObject(tabField.FieldType, 3));
            paintedTab.SetValue(form, Enum.ToObject(paintedTab.FieldType, 5));   // 伪造「上一帧在别的页」
            Motion.Start("page", 150, Ease.Linear);
            Render(form, "page-transition-0", 1f);
            Motion.AdvanceFakeClock(75);
            Render(form, "page-transition-50", 1f);
            Motion.AdvanceFakeClock(75);
            Render(form, "page-transition-100", 1f);
            Motion.ResetForTests();
            Motion.Live = true;
            tabField.SetValue(form, Enum.ToObject(tabField.FieldType, 1));
            Render(form, "trend", 1f);
            Render(form, "trend", 1f);
            typeof(Dashboard).GetMethod("OnMouseClick", Private).Invoke(form,
                new object[] { new MouseEventArgs(MouseButtons.Left, 1, 514, 174, 0) });
            if ((int)typeof(Dashboard).GetField("_trendMetric", Private).GetValue(form) != 3)
                throw new Exception("Translated distance filter hit test failed");
            Render(form, "distance", 1f);
            typeof(Dashboard).GetMethod("OnMouseClick", Private).Invoke(form,
                new object[] { new MouseEventArgs(MouseButtons.Left, 1, 742, 174, 0) });
            if (!(bool)typeof(Dashboard).GetField("_trendHourly", Private).GetValue(form)) throw new Exception("Hourly trend switch hit target failed");
            Render(form, "trend-hourly", 1f); Render(form, "trend-hourly-150", 1.5f);
            // 1.8.0:图表入场生长的关键帧(时段分布的柱子从基线长出)。
            Motion.ResetForTests();
            Motion.UseFakeClock(200000);
            Motion.Live = true;
            tabField.SetValue(form, Enum.ToObject(tabField.FieldType, 2));
            Motion.Start("enter", 280, Ease.Linear);
            Render(form, "hours-enter-0", 1f);
            Motion.AdvanceFakeClock(140);
            Render(form, "hours-enter-50", 1f);
            Motion.AdvanceFakeClock(140);
            Render(form, "hours-enter-100", 1f);
            Motion.ResetForTests();
            Motion.Live = true;
            tabField.SetValue(form, Enum.ToObject(tabField.FieldType, 1));   // 回到趋势页,后续预览仍属于它
            Set(form, "_hourlyCompareDays", 7); Render(form, "trend-hourly-compare-week", 1f);
            Set(form, "_hourlyTrendMetric", 2); Render(form, "trend-hourly-active", 1f);
            Set(form, "_hourlyTrendDay", DateTime.Today.AddDays(-80)); Render(form, "trend-hourly-missing", 1f);
            Set(form, "_hourlyTrendDay", DateTime.Today); Set(form, "_trendHourly", false);
            DateTime sampleNow = DateTime.Today.AddHours(12).AddMinutes(30);
            double[] hourly = Dashboard.HourlyValues(Store.Today, DateTime.Today, 0, sampleNow);
            if (hourly.Length != 24 || double.IsNaN(hourly[12]) || !double.IsNaN(hourly[13])) throw new Exception("Current hour included; future must remain missing");
            if (!double.IsNaN(Dashboard.HourlyValues(null, DateTime.Today, 0, sampleNow)[0])) throw new Exception("Missing day must not become zero");
            if (!double.IsNaN(Dashboard.HourlyValues(new DayRecord { Keys = 3 }, DateTime.Today, 2, sampleNow)[0])) throw new Exception("Legacy count must not imply time coverage");
            if (Dashboard.HourlyValues(Store.Today, DateTime.Today.AddDays(-1), 2, sampleNow)[9] != 50) throw new Exception("Active seconds converted to hourly minutes");
            ICollection chips = (ICollection)typeof(Dashboard).GetField("_chips", Private).GetValue(form);
            ICollection ids = (ICollection)typeof(Dashboard).GetField("_chipIds", Private).GetValue(form);
            if (chips.Count != ids.Count) throw new Exception("Chip IDs must match each rendered frame");
            typeof(Dashboard).GetMethod("OnMouseClick", Private).Invoke(form,
                new object[] { new MouseEventArgs(MouseButtons.Left, 1, 85, 383, 0) });
            if (Convert.ToInt32(tabField.GetValue(form)) != 4) throw new Exception("Fifth navigation target failed");
            Render(form, "apps", 1f);
            typeof(Dashboard).GetMethod("OnMouseClick", Private).Invoke(form,
                new object[] { new MouseEventArgs(MouseButtons.Left, 1, 985, 174, 0) });
            if (!(bool)typeof(Dashboard).GetField("_appsByWindow", Private).GetValue(form)) throw new Exception("Window grouping hit target failed");
            Render(form, "app-windows", 1f);
            typeof(Dashboard).GetMethod("OnMouseClick", Private).Invoke(form,
                new object[] { new MouseEventArgs(MouseButtons.Left, 1, 970, 620, 0) });
            if ((int)typeof(Dashboard).GetField("_appPage", Private).GetValue(form) != 1) throw new Exception("App pagination hit target failed");
            Render(form, "app-windows-page2", 1f);

            // 1.7.4: the apps page gains a key-composition view (stacked segments + hover detail).
            Set(form, "_appsByWindow", false);
            Set(form, "_appPage", 0);
            Set(form, "_appKeyGroups", false);
            Render(form, "apps", 1f);
            ClickChip(form, 65);
            if (!(bool)typeof(Dashboard).GetField("_appKeyGroups", Private).GetValue(form))
                throw new Exception("Key composition chip hit target failed");
            Render(form, "app-keygroups", 1f); Render(form, "app-keygroups-150", 1.5f);
            int contentX = Convert.ToInt32(typeof(Dashboard).GetField("ContentX", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null));
            int contentY = Convert.ToInt32(typeof(Dashboard).GetField("ContentY", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null));
            Set(form, "_mouse", new Point(500 + contentX, 295 + contentY));
            Render(form, "app-keygroups-hover", 1f);
            Set(form, "_mouse", new Point(0, 0));

            // 1.8.0:应用对比——点两个「对比」chip,页脚给出并排对比;再点「清除对比」恢复提示。
            Set(form, "_appKeyGroups", false);
            Render(form, "apps", 1f);
            IList compareRects = (IList)typeof(Dashboard).GetField("_appCompareRects", Private).GetValue(form);
            if (compareRects.Count < 2) throw new Exception("the apps page did not register compare hit targets");
            for (int i = 0; i < 2; i++)
            {
                RectangleF rect = (RectangleF)compareRects[i];
                ClickContent(form, rect);
            }
            string compareSummary = (string)typeof(Dashboard).GetMethod("CompareSummary", Private).Invoke(form, null);
            if (string.IsNullOrEmpty(compareSummary)) throw new Exception("comparing two apps produced no summary");
            if (compareSummary.IndexOf("击键") < 0 || compareSummary.IndexOf('×') < 0)
                throw new Exception("the comparison summary dropped the keystroke ratio: " + compareSummary);
            // 没有观测时长的数据不该显示「0 秒 / 0 秒」这种噪音。
            if (compareSummary.IndexOf("时长") >= 0 && compareSummary.IndexOf("0 秒 / 0 秒") >= 0)
                throw new Exception("the comparison summary shows two zero durations: " + compareSummary);
            Render(form, "apps-compare", 1f);
            IList picked = (IList)typeof(Dashboard).GetField("_comparePicked", Private).GetValue(form);
            if (picked.Count != 2) throw new Exception("two compared apps were not kept");
            // 最多保留两个:再点第三个「对比」时,最早选的那个被替换掉(而不是被拒绝)。
            if (compareRects.Count > 2)
            {
                IList seeded = (IList)typeof(Dashboard).GetField("_comparePicked", Private).GetValue(form);
                object first = seeded[0];
                ClickContent(form, (RectangleF)compareRects[2]);
                IList after = (IList)typeof(Dashboard).GetField("_comparePicked", Private).GetValue(form);
                if (after.Count != 2) throw new Exception("the comparison kept more than two apps");
                bool droppedFirst = true;
                foreach (object row in after) if (ReferenceEquals(row, first)) droppedFirst = false;
                if (!droppedFirst) throw new Exception("the oldest compared app was not replaced");
                ClickContent(form, (RectangleF)compareRects[2]);   // 取消第三个 → 只剩最初选的第二个
                ClickContent(form, (RectangleF)compareRects[0]);   // 再选回第一个
                if (((IList)typeof(Dashboard).GetField("_comparePicked", Private).GetValue(form)).Count != 2)
                    throw new Exception("could not restore the two-app comparison");
            }
            RectangleF clearRect = (RectangleF)typeof(Dashboard).GetField("_appCompareClearRect", Private).GetValue(form);
            if (clearRect.Width <= 0) throw new Exception("the comparison clear target was not registered");
            // chip 必须落在页脚范围内,不能压住上一页/下一页。
            if (clearRect.X + clearRect.Width > 24 + 832 - 96)
                throw new Exception("the clear-comparison chip collides with the pager buttons");
            ClickContent(form, clearRect);
            if (((IList)typeof(Dashboard).GetField("_comparePicked", Private).GetValue(form)).Count != 0)
                throw new Exception("clearing the comparison did not work");
            if ((string)typeof(Dashboard).GetMethod("CompareSummary", Private).Invoke(form, null) != null)
                throw new Exception("the summary survived clearing the comparison");

            // 1.7.4: the keys page gains a hold-duration mode reusing the report data.
            tabField.SetValue(form, Enum.ToObject(tabField.FieldType, 3));
            Set(form, "_showKeyboardHeatmap", false);
            Set(form, "_showCombos", false);
            Set(form, "_showHoldDuration", false);
            Render(form, "keys", 1f);
            ClickChip(form, 78);
            if (!(bool)typeof(Dashboard).GetField("_showHoldDuration", Private).GetValue(form))
                throw new Exception("Hold duration chip hit target failed");
            Render(form, "keys-hold", 1f); Render(form, "keys-hold-150", 1.5f);

            // 1.7.4: each panel page hands the report its matching page and the panel's current range.
            MethodInfo reportTab = typeof(Dashboard).GetMethod("ReportTabForPage", Private);
            MethodInfo currentRange = typeof(Dashboard).GetMethod("CurrentRange", Private);
            object[] range = new object[] { null, null };
            tabField.SetValue(form, Enum.ToObject(tabField.FieldType, 4));
            Set(form, "_appKeyGroups", true);
            if ((int)reportTab.Invoke(form, null) != 15) throw new Exception("Key-composition mode must open the app × key report page");
            Set(form, "_appKeyGroups", false);
            if ((int)reportTab.Invoke(form, null) != 2) throw new Exception("Apps page must open the app-time report page");
            Set(form, "_appDays", 90);
            currentRange.Invoke(form, range);
            if ((DateTime)range[1] != DateTime.Today || (DateTime)range[0] != DateTime.Today.AddDays(-89))
                throw new Exception("Apps page must hand over its 90-day filter");
            Set(form, "_appDays", 1);
            currentRange.Invoke(form, range);
            if ((DateTime)range[0] != DateTime.Today || (DateTime)range[1] != DateTime.Today)
                throw new Exception("A single-day filter must hand over a one-day range");
            Set(form, "_showHoldDuration", true); Set(form, "_keyRange", 2);
            tabField.SetValue(form, Enum.ToObject(tabField.FieldType, 3));
            if ((int)reportTab.Invoke(form, null) != 14) throw new Exception("Hold mode must open the hold report page");
            currentRange.Invoke(form, range);
            if ((DateTime)range[0] != DateTime.Today.AddDays(-6)) throw new Exception("Keys page must hand over its 7-day filter");
            Set(form, "_showHoldDuration", false); Set(form, "_keyRange", 0);
            if ((int)reportTab.Invoke(form, null) != 9) throw new Exception("Keys page must open the key distribution page");

            // 1.7.4: every panel page offers the report entry (chip 45). Clicking it opens a modal
            // report, so only its registration is asserted here.
            for (int page = 0; page < 6; page++)
            {
                tabField.SetValue(form, Enum.ToObject(tabField.FieldType, page));
                Set(form, "_showHoldDuration", false);
                Set(form, "_appKeyGroups", false);
                Render(form, "report-entry-" + page, 1f);
                IList pageIds = (IList)typeof(Dashboard).GetField("_chipIds", Private).GetValue(form);
                bool entry = false;
                for (int i = 0; i < pageIds.Count; i++) if (Convert.ToInt32(pageIds[i]) == 45) { entry = true; break; }
                if (!entry) throw new Exception("Panel page " + page + " does not register the report entry chip");
            }
            tabField.SetValue(form, Enum.ToObject(tabField.FieldType, 2));
            Set(form, "_hourMetric", 2);
            Render(form, "hours-active", 1f);
            Render(form, "hours-active-150", 1.5f);
            using (SessionDetails details = new SessionDetails()) RenderDialog(details, "sessions");
            using (ActivitySettings settings = new ActivitySettings()) RenderDialog(settings, "activity-settings");
            long[] categoryTotals;
            AppActivity.Row ruleRow = AppActivity.Aggregate(new DayRecord[] { Store.Today }, true, out categoryTotals)[0];
            using (AppCategoryDialog rules = new AppCategoryDialog(ruleRow)) RenderDialog(rules, "category-rules");
            using (StatisticsReport report = new StatisticsReport(DateTime.Today))
            {
                ReportDeck tabs = (ReportDeck)typeof(StatisticsReport).GetField("_tabs", Private).GetValue(report);
                for (int page = 0; page < tabs.TabPages.Count; page++) { tabs.SelectedIndex = page; RenderDialog(report, "statistics-report-" + page); }
            }
            tabField.SetValue(form, Enum.ToObject(tabField.FieldType, 4));
            Set(form, "_onlyUnclassified", true); Set(form, "_appsByWindow", true);
            Render(form, "apps-pending-empty", 1f);
            Set(form, "_onlyUnclassified", false); Set(form, "_appsByWindow", false);
            Set(form, "_s", 1f);
            typeof(Dashboard).GetMethod("OnMouseClick", Private).Invoke(form,
                new object[] { new MouseEventArgs(MouseButtons.Left, 1, 85, 438, 0) });
            if (Convert.ToInt32(tabField.GetValue(form)) != 5) throw new Exception("Insights navigation failed");
            Render(form, "insights-calendar", 1f); Render(form, "insights-calendar-150", 1.5f);
            Set(form, "_calendarMetric", 1); Render(form, "insights-calendar-keys", 1f);
            Set(form, "_calendarMetric", 0);
            IDictionary dates = (IDictionary)typeof(Dashboard).GetField("_insightDates", Private).GetValue(form);
            if (dates.Count != 365) throw new Exception("Annual calendar must show exactly 365 dates");
            foreach (DictionaryEntry entry in dates)
            {
                if ((DateTime)entry.Value != DateTime.Today) continue;
                RectangleF cell = (RectangleF)entry.Key;
                typeof(Dashboard).GetMethod("OnMouseClick", Private).Invoke(form,
                    new object[] { new MouseEventArgs(MouseButtons.Left, 1, (int)(cell.X + 176 + 3), (int)(cell.Y + 64 + 3), 0) });
                break;
            }
            if ((int)typeof(Dashboard).GetField("_insightView", Private).GetValue(form) != 1) throw new Exception("Calendar drilldown failed");
            Render(form, "insights-timeline", 1f); Render(form, "insights-timeline-150", 1.5f);
            Set(form, "_insightView", 2);
            Render(form, "insights-week", 1f); Render(form, "insights-week-150", 1.5f);
            for (int theme = 0; theme < ArtTheme.All.Length; theme++)
            {
                Store.ThemeId = theme;
                if (theme == 5) WaitTheme(form, new[] { "NikkiDream", "NikkiPoseTrend", "NikkiPoseHours", "NikkiPoseKeys", "NikkiPoseApps", "NikkiPoseInsights", "NikkiEmotes" });
                else if (theme == 6) WaitTheme(form, new[] { "HaloIcons", "HaloOverview", "HaloTrend", "HaloHours", "HaloKeys", "HaloApps", "HaloInsights" });
                else if (theme == 7) WaitTheme(form, new[] { "ResidentIcons", "ResidentOverview", "ResidentTrend", "ResidentHours", "ResidentKeys", "ResidentApps", "ResidentInsights" });
                else if (theme == 8) WaitTheme(form, new[] { "MinecraftIcons", "MinecraftOverview", "MinecraftTrend", "MinecraftHours", "MinecraftKeys", "MinecraftApps", "MinecraftInsights" });
                else if (theme == 11) WaitTheme(form, new[] { "EuroTruckIcons", "EuroTruckWidget", "EuroTruckOverview", "EuroTruckTrend", "EuroTruckHours", "EuroTruckKeys", "EuroTruckApps", "EuroTruckInsights" });
                for (int page = 0; page < 6; page++)
                {
                    tabField.SetValue(form, Enum.ToObject(tabField.FieldType, page));
                    Render(form, "theme-" + theme + "-page-" + page, 1f);
                }
                Set(form, "_insightView", 1);
                Render(form, "theme-" + theme + "-timeline-150", 1.5f);
                Set(form, "_insightView", 2);
            }
            // Post-loop checks: the LRU cache may have evicted each art theme's
            // images while later themes rendered, so wait again before asserting.
            // Activate the theme first: ThemeImages only decodes the active theme's catalog,
            // so waiting while another theme is active can never resolve.
            Store.ThemeId = 5;
            WaitTheme(form, new[] { "NikkiDream", "NikkiPoseTrend", "NikkiPoseHours", "NikkiPoseKeys", "NikkiPoseApps", "NikkiPoseInsights", "NikkiEmotes" });
            if(!NikkiArt.HasArtwork)throw new Exception("Embedded Nikki artwork missing");
            if(!NikkiArt.HasEmotes)throw new Exception("Embedded emote atlas missing");
            for(int page=0;page<6;page++)if(!NikkiArt.HasPageArtwork(page))throw new Exception("Missing page portrait "+page);
            tabField.SetValue(form,Enum.ToObject(tabField.FieldType,3));Set(form,"_showKeyboardHeatmap",true);Store.KeyboardLayout=1;
            Render(form,"nikki-heatmap",1f);
            object plate=typeof(Dashboard).GetField("_keyboardPlate",Private).GetValue(form);
            Render(form,"nikki-heatmap-cached",1f);
            if(!object.ReferenceEquals(plate,typeof(Dashboard).GetField("_keyboardPlate",Private).GetValue(form)))throw new Exception("Static keyboard surface should be reused");
            long originalE=Store.Today.KeyCounts[69];Store.Today.KeyCounts[69]=originalE+100000;
            Render(form,"nikki-champion-narrow",1f);
            if(object.ReferenceEquals(plate,typeof(Dashboard).GetField("_keyboardPlate",Private).GetValue(form)))throw new Exception("Changed key counts must rebuild the keyboard surface");
            plate=typeof(Dashboard).GetField("_keyboardPlate",Private).GetValue(form);
            Set(form,"_mouse",new Point(350,428));Render(form,"nikki-champion-hover",1f);
            if(!object.ReferenceEquals(plate,typeof(Dashboard).GetField("_keyboardPlate",Private).GetValue(form)))throw new Exception("Hover must reuse the keyboard surface");
            Render(form,"nikki-champion-150",1.5f);
            if(object.ReferenceEquals(plate,typeof(Dashboard).GetField("_keyboardPlate",Private).GetValue(form)))throw new Exception("DPI changes must rebuild the keyboard surface");
            Set(form,"_mouse",new Point(-100,-100));Store.Today.KeyCounts[69]=originalE;Set(form,"_showKeyboardHeatmap",false);

            using(NikkiNotice notice=new NikkiNotice("示例提示：主题资源已嵌入程序。", "操作完成", MessageBoxButtons.OK)) RenderDialog(notice,"nikki-notice");
            using(DistanceSettings themedDistance=new DistanceSettings(new RawMouseInput())) RenderDialog(themedDistance,"nikki-distance");
            using(SessionDetails themedSessions=new SessionDetails()) RenderDialog(themedSessions,"nikki-sessions");
            using(ActivitySettings themedActivity=new ActivitySettings()) RenderDialog(themedActivity,"nikki-activity-settings");
            using(AppCategoryDialog themedRules=new AppCategoryDialog(ruleRow)) RenderDialog(themedRules,"nikki-rules");
            using(StatisticsReport themedReport=new StatisticsReport(DateTime.Today)) RenderDialog(themedReport,"nikki-report");
            tabField.SetValue(form,Enum.ToObject(tabField.FieldType,5));Set(form,"_insightView",0);Render(form,"nikki-calendar",1f);
            Store.ThemeId=6;
            WaitTheme(form, new[] { "HaloIcons", "HaloOverview", "HaloTrend", "HaloHours", "HaloKeys", "HaloApps", "HaloInsights" });
            if(!HaloArt.HasIcons)throw new Exception("Missing Halo icons");
            // 1.8.0:换主题的交叉淡化关键帧(用假时钟钉住时间轴;离屏渲染本来不做动效,
            // 所以这里直接按生产代码在可见时的做法记下旧底色)。
            Motion.ResetForTests();
            Motion.UseFakeClock(300000);
            Motion.Live = true;
            Set(form,"_paintedTheme",0);
            typeof(Dashboard).GetField("_themeScrim",Private).SetValue(form,Color.FromArgb(255,12,18,24));
            Motion.Start("themefade",220,Ease.Linear);
            Render(form,"theme-fade-0",1f);
            if(((Color)typeof(Dashboard).GetField("_themeScrim",Private).GetValue(form))==Color.Empty)
                throw new Exception("theme fade did not keep the previous background");
            Motion.AdvanceFakeClock(110);
            Render(form,"theme-fade-50",1f);
            Motion.AdvanceFakeClock(110);
            Render(form,"theme-fade-100",1f);
            if(((Color)typeof(Dashboard).GetField("_themeScrim",Private).GetValue(form))!=Color.Empty)
                throw new Exception("theme fade should release the scrim once it finishes");
            Motion.ResetForTests();
            Motion.Live = true;
            for(int page=0;page<6;page++)if(!HaloArt.HasScene(page))throw new Exception("Missing Halo scene "+page);
            tabField.SetValue(form,Enum.ToObject(tabField.FieldType,3));Set(form,"_showKeyboardHeatmap",true);
            Render(form,"halo-heatmap",1f);
            object haloPlate=typeof(Dashboard).GetField("_keyboardPlate",Private).GetValue(form);
            Store.ThemeId=5;Render(form,"nikki-after-halo",1f);
            if(object.ReferenceEquals(haloPlate,typeof(Dashboard).GetField("_keyboardPlate",Private).GetValue(form)))throw new Exception("Theme switch must rebuild key colors and emblems");
            Store.ThemeId=6;Render(form,"halo-heatmap-150",1.5f);Set(form,"_showKeyboardHeatmap",false);
            using(StatisticsReport report=new StatisticsReport(DateTime.Today))RenderDialog(report,"halo-report");
            using(ActivitySettings settings=new ActivitySettings())RenderDialog(settings,"halo-settings");
            using(AppCategoryDialog rules=new AppCategoryDialog(ruleRow))RenderDialog(rules,"halo-rules");
            Store.ThemeId=7;
            WaitTheme(form, new[] { "ResidentIcons", "ResidentOverview", "ResidentTrend", "ResidentHours", "ResidentKeys", "ResidentApps", "ResidentInsights" });
            tabField.SetValue(form,Enum.ToObject(tabField.FieldType,0));Set(form,"_overviewKeyboard",true);Render(form,"overview-keyboard",1f);Render(form,"overview-keyboard-150",1.5f);Set(form,"_mouse",new Point(370,413));Render(form,"overview-keyboard-hover",1f);Set(form,"_mouse",new Point(-100,-100));Set(form,"_overviewKeyboard",false);
            if(!ResidentArt.HasIcons)throw new Exception("Missing Resident icons");
            for(int page=0;page<6;page++)if(!ResidentArt.HasScene(page))throw new Exception("Missing Resident scene "+page);
            tabField.SetValue(form,Enum.ToObject(tabField.FieldType,3));Set(form,"_showKeyboardHeatmap",true);
            Render(form,"resident-heatmap",1f);Render(form,"resident-heatmap-150",1.5f);
            Set(form,"_showKeyboardHeatmap",false);
            using(ActivitySettings settings=new ActivitySettings())RenderDialog(settings,"resident-settings");
            using(StatisticsReport report=new StatisticsReport(DateTime.Today))RenderDialog(report,"resident-report");
            tabField.SetValue(form,Enum.ToObject(tabField.FieldType,0));
            long[] apmHistory=(long[])typeof(LiveRate).GetField("_trend",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            for(int i=0;i<300;i++)apmHistory[i]=(long)(80+60*Math.Sin(i/24.0));
            Render(form,"apm-five-minutes",1f);Render(form,"apm-five-minutes-150",1.5f);LiveRate.Reset();
            using(GameHud hud=new GameHud())
            {
                PropertyInfo styles=typeof(GameHud).GetProperty("CreateParams",Private);
                int normal=((CreateParams)styles.GetValue(hud,null)).ExStyle;
                if((normal&0x08080020)!=0x08080020)throw new Exception("HUD transparency/no-activation styles");
                RenderDialog(hud,"game-hud");hud.Editing=true;
                if((((CreateParams)styles.GetValue(hud,null)).ExStyle&0x20)!=0)throw new Exception("HUD editing must disable pass through");
                RenderDialog(hud,"game-hud-edit");hud.Editing=false;
                if((((CreateParams)styles.GetValue(hud,null)).ExStyle&0x20)==0)throw new Exception("HUD pass through restored");
            }
            Store.ThemeId=8;
            WaitTheme(form, new[] { "MinecraftIcons", "MinecraftOverview", "MinecraftTrend", "MinecraftHours", "MinecraftKeys", "MinecraftApps", "MinecraftInsights" });
            if(!MinecraftArt.HasIcons)throw new Exception("Minecraft icons missing");
            for(int i=0;i<6;i++)if(!MinecraftArt.HasScene(i))throw new Exception("Minecraft scene missing");
            tabField.SetValue(form,Enum.ToObject(tabField.FieldType,3));Set(form,"_showKeyboardHeatmap",true);
            Render(form,"minecraft-heatmap",1f);Render(form,"minecraft-heatmap-150",1.5f);Set(form,"_showKeyboardHeatmap",false);
            using(ActivitySettings settings=new ActivitySettings())RenderDialog(settings,"minecraft-settings");
            Store.ThemeId = 0;
            Store.History.Clear(); Store.Total = new Counters(); Store.MouseDpi = 0;
            Store.RollDay(DateTime.Today);
            tabField.SetValue(form, Enum.ToObject(tabField.FieldType, 3));
            Render(form, "combos-empty", 1f);
            tabField.SetValue(form, Enum.ToObject(tabField.FieldType, 4));
            Render(form, "apps-empty", 1f);
            tabField.SetValue(form, Enum.ToObject(tabField.FieldType, 0));
            Render(form, "empty", 1f);
            tabField.SetValue(form, Enum.ToObject(tabField.FieldType, 5));
            for (int v = 0; v < 3; v++) { Set(form, "_insightView", v); Render(form, "insights-empty-" + v, 1f); }
            // Native smoke check against a hidden test window; no input hooks or persisted user data.
            ActivityMonitor.Register(form.Handle);
            ActivityMonitor.Tick();
            if (ActivityMonitor.Status == "会话检测不可用" || ActivityMonitor.Status == "无法读取空闲状态")
                throw new Exception("Native idle/session API smoke check failed");
            ActivityMonitor.Unregister(form.Handle);
            typeof(Dashboard).GetMethod("OnFormClosed", Private).Invoke(form,
                new object[] { new FormClosedEventArgs(CloseReason.None) });
        }
        Console.WriteLine("Rendered six pages and all three insight views at 100% and 150%; empty states, navigation, filters, pagination and calendar drilldown passed.");
    }
    /// <summary>点击当前帧注册的 chip:坐标从 _chips / _chipIds 反查,不硬编码像素。</summary>
    private static void ClickChip(Dashboard form, int id)
    {
        IList chips = (IList)typeof(Dashboard).GetField("_chips", Private).GetValue(form);
        IList ids = (IList)typeof(Dashboard).GetField("_chipIds", Private).GetValue(form);
        int contentX = Convert.ToInt32(typeof(Dashboard).GetField("ContentX", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null));
        int contentY = Convert.ToInt32(typeof(Dashboard).GetField("ContentY", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null));
        for (int i = 0; i < ids.Count && i < chips.Count; i++)
        {
            if (Convert.ToInt32(ids[i]) != id) continue;
            RectangleF rect = (RectangleF)chips[i];
            typeof(Dashboard).GetMethod("OnMouseClick", Private).Invoke(form, new object[] { new MouseEventArgs(MouseButtons.Left, 1,
                (int)(rect.X + rect.Width / 2) + contentX, (int)(rect.Y + rect.Height / 2) + contentY, 0) });
            return;
        }
        throw new Exception("Chip " + id + " is not registered on the current page");
    }

    /// <summary>点击内容坐标系里的矩形:按当前 _s 换算回窗口坐标,再走 OnMouseClick 的 ToBase。</summary>
    private static void ClickContent(Dashboard form, RectangleF content)
    {
        int contentX = Convert.ToInt32(typeof(Dashboard).GetField("ContentX", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null));
        int contentY = Convert.ToInt32(typeof(Dashboard).GetField("ContentY", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null));
        float s = Convert.ToSingle(typeof(Dashboard).GetField("_s", Private).GetValue(form));
        if (s <= 0) s = 1f;
        int x = (int)Math.Round((content.X + content.Width / 2 + contentX) * s);
        int y = (int)Math.Round((content.Y + content.Height / 2 + contentY) * s);
        typeof(Dashboard).GetMethod("OnMouseClick", Private).Invoke(form,
            new object[] { new MouseEventArgs(MouseButtons.Left, 1, x, y, 0) });
    }

    private static void Render(Dashboard form, string name, float scale)
    {
        Set(form, "_s", scale);
        using (Bitmap bitmap = new Bitmap((int)(1072 * scale), (int)(724 * scale)))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            typeof(Dashboard).GetMethod("OnPaint", Private).Invoke(form,
                new object[] { new PaintEventArgs(graphics, new Rectangle(Point.Empty, bitmap.Size)) });
            bitmap.Save(Path.Combine("previews", name + ".png"), ImageFormat.Png);
        }
    }
    private static void RenderDialog(Form form, string name)
    {
        IntPtr formHandle = form.Handle;
        CreateChildHandles(form);
        form.PerformLayout();
        using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine("previews", name + ".png"), ImageFormat.Png);
        }
    }
    private static void CreateChildHandles(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            IntPtr childHandle = control.Handle;
            CreateChildHandles(control);
        }
    }
}
