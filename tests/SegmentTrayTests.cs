using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using KeyMouseStats;

/// <summary>
/// 「时段分布 → 连续段」的显示口径回归:
///   1. 段长四档的分界(5 / 30 / 60 分钟,严格小于);
///   2. 短段占比公式 —— 只把 &lt;5 分钟算短段,浮点四舍五入,与报告页共用同一处实现;
///   3. 筛选胶囊托盘在美术主题下的对比度 —— 在离屏位图上调用真实绘制函数,按亮部纯白验 ≥4.5:1;
///   4. 连续段离屏冒烟 —— 三种指标各画一次,柱块命中表与悬停明细必须真的被填;
///   5. 连续段翻页的定位边界(默认最新 / 钉住 / 钉住日被裁剪 / 空列表)。
/// 只用内存夹具:Store.DataDirectory 指向临时目录,本用例不调用 Save/Load,不触碰真实用户数据。
/// </summary>
internal static class SegmentTrayTests
{
    private static int checks;
    private static readonly string Dir = Path.Combine(Path.GetTempPath(), "SegmentTray-" + Guid.NewGuid().ToString("N"));
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) throw new Exception(what);
    }

    private static void SetField(object target, string name, object value)
    {
        target.GetType().GetField(name, Private).SetValue(target, value);
    }

    private static object GetField(object target, string name)
    {
        return target.GetType().GetField(name, Private).GetValue(target);
    }

    [STAThread]
    private static void Main()
    {
        // 隔离:即使以后有人在这里误加 Store.Save(),也只会写进临时目录。
        Store.DataDirectory = Dir;
        // Milestone 2 / 3.4:总览在「全新零数据」时会整页让位给冷启动引导,
        // 本用例测的是正常页面上的分段托盘,所以先塞一条最小记录让它进入常规布局。
        Store.History.Clear();
        Store.Total = new Counters();
        DayRecord seed = new DayRecord();
        seed.Date = DateTime.Today;
        seed.Keys = 1;
        Store.Today = seed;
        Store.History[DateTime.Today] = seed;
        Store.Total.Keys = 1;
        try { Run(); }
        catch (Exception error) { Console.WriteLine("FAILED: " + error.Message); Environment.ExitCode = 1; return; }
        finally { try { if (Directory.Exists(Dir)) Directory.Delete(Dir, true); } catch { } }
        Console.WriteLine("Segment tray: " + checks + " checks passed");
    }

    private static ActiveSession Session(DateTime start, double seconds)
    {
        ActiveSession session = new ActiveSession();
        session.Start = start;
        session.End = start.AddSeconds(seconds);
        session.Seconds = seconds;
        return session;
    }

    private static void Run()
    {
        // ---- 1. 段长分档:边界归属(严格小于,与四档卡上的文字一致)----
        Check(ReportDesign.SegmentBin(0) == 0, "0 秒落在 <5 分钟档");
        Check(ReportDesign.SegmentBin(299.9) == 0, "4 分 59.9 秒仍是 <5 分钟档");
        Check(ReportDesign.SegmentBin(300) == 1, "恰好 5 分钟进 5–30 分钟档");
        Check(ReportDesign.SegmentBin(1799) == 1, "29 分 59 秒仍是 5–30 分钟档");
        Check(ReportDesign.SegmentBin(1800) == 2, "恰好 30 分钟进 30–60 分钟档");
        Check(ReportDesign.SegmentBin(3599) == 2, "59 分 59 秒仍是 30–60 分钟档");
        Check(ReportDesign.SegmentBin(3600) == 3, "恰好 60 分钟进 >60 分钟档");
        Check(ReportDesign.SegmentBinNames.Length == 4 && ReportDesign.SegmentBinNames[0] == "< 5 分钟"
            && ReportDesign.SegmentBinNames[3] == "≥ 60 分钟", "四档标签与分界一致(恰好 3600 秒归最后一档)");

        // ---- 2. 分档计数:空值与非正时长不计入 ----
        DateTime day = DateTime.Today.AddDays(-1);
        List<ActiveSession> sessions = new List<ActiveSession>();
        sessions.Add(Session(day.AddHours(8), 120));      // <5 分钟
        sessions.Add(Session(day.AddHours(9), 600));      // 5–30 分钟
        sessions.Add(Session(day.AddHours(10), 2400));    // 30–60 分钟
        sessions.Add(Session(day.AddHours(12), 4200));    // >60 分钟
        sessions.Add(Session(day.AddHours(14), 0));       // 零时长:丢弃
        int[] bins = ReportDesign.SegmentBins(sessions);
        Check(bins[0] == 1 && bins[1] == 1 && bins[2] == 1 && bins[3] == 1, "四档各计一段,零时长不计入");
        int[] empty = ReportDesign.SegmentBins(null);
        Check(empty[0] == 0 && empty[3] == 0, "null 段列表返回全零");

        // ---- 3. 短段占比:只算 <5 分钟,浮点四舍五入 ----
        Check(ReportDesign.ShortShare(35, 48) == "73%", "35/48 短段占比 = 73%");
        Check(ReportDesign.ShortShare(47, 48) == "98%", "47/48 = 98%(浮点,不是整数除法的 97%)");
        Check(ReportDesign.ShortShare(1, 1) == "100%", "单段且为短段 = 100%");
        Check(ReportDesign.ShortShare(2, 3) == "67%", "2/3 = 67%");
        Check(ReportDesign.ShortShare(0, 4) == "0%", "没有短段 = 0%");
        Check(ReportDesign.ShortShare(0, 0) == "--", "没有样本 = --");
        // 旧口径 (bins[0]+bins[1])/n 会把 5–30 分钟也算碎片,新口径只看 bins[0]:
        // 4 段里只有 1 段短于 5 分钟,必须给出 25%,而不是 (1+1)/4 = 50%。
        Check(ReportDesign.ShortShare(bins[0], sessions.Count - 1) == "25%", "实时计算:4 段里 1 段短 = 25%");

        // ---- 4. 托盘对比度:直接在离屏位图上调用真实绘制函数,而不是只断言常量 ----
        // 位图先铺成纯白,模拟"原画最亮的那个像素";如果 PaintSegmentTray 被改回 7% 近白雾,
        // 白色底上几乎不会变暗,下面的亮度断言立刻失败。
        int artThemes = 0;
        for (int id = 0; id < ArtTheme.All.Length; id++)
        {
            Store.ThemeId = id;
            ArtTheme theme = ArtTheme.Current;
            if (!ThemeArt.Active)
            {
                // 纯色主题保持原样:7% 近白雾叠在主题自己的平整背景上,不给它加不透明底板。
                Color plateFlat = PaintTraySample(id, theme.Background);
                Color expected = ArtTheme.Composite(theme.Text, 18, theme.Background);
                Check(Math.Abs(plateFlat.R - expected.R) <= 2 && Math.Abs(plateFlat.G - expected.G) <= 2
                    && Math.Abs(plateFlat.B - expected.B) <= 2, "纯色主题托盘仍是 7% 轻雾底(与旧配方逐像素一致): " + theme.Name);
                continue;
            }
            artThemes++;
            // 美术主题:把亮部当纯白(最坏情况)。暗色美术主题的托盘必须把白底压到暗色卡片那一档;
            // 浅色美术主题(暖暖)本来就是浅底深字,交给下面的对比度断言把关。
            Color plate = PaintTraySample(id, Color.White);
            double brightness = 0.2126 * plate.R + 0.7152 * plate.G + 0.0722 * plate.B;
            if (ThemeArt.Dark)
                Check(brightness < 96, "暗色美术主题托盘把亮原画压到暗底(" + theme.Name + " 白底合成后亮度 = "
                    + brightness.ToString("0", CultureInfo.InvariantCulture) + "/255)");
            Color ink = Dashboard.FilterInkColor();
            Check(ink.A == 214, "美术主题未选中文字提升到 214/255: " + theme.Name);
            Color inkOnPlate = ArtTheme.Composite(ink, ink.A, plate);
            double ratio = ArtTheme.ContrastRatio(inkOnPlate, plate);
            Check(ratio >= 4.5, "美术主题未选中文字对比度 ≥4.5:1 (" + theme.Name + " = "
                + ratio.ToString("0.00", CultureInfo.InvariantCulture) + ":1)");
            if (id == 12)
            {
                // 回归线:旧配方(7% 近白底板 + Csub 字色)在最坏情况下必须不达标,
                // 否则上面的断言就没有区分力,改了等于没改。
                Color oldPlate = ArtTheme.Composite(theme.Text, 18, Color.White);
                double oldRatio = ArtTheme.ContrastRatio(theme.Muted, oldPlate);
                Check(oldRatio < 3.0, "旧配方(7% 底板 + Csub)在亮部仍不达标,证明对比度断言有区分力: "
                    + oldRatio.ToString("0.00", CultureInfo.InvariantCulture) + ":1");
            }
        }
        Check(artThemes == 9, "美术主题共 9 套(古剑/奥日/暖暖/光环/生化/我的世界/小丑牌/大侠/欧卡)");

        // ---- 4b. 连续段离屏冒烟:三种指标都画一遍,命中表与悬停明细必须真的被填 ----
        PaintSmoke();


        // ---- 5. 连续段翻页的定位边界 ----
        List<DateTime> dates = new List<DateTime>();
        dates.Add(new DateTime(2026, 11, 10));
        dates.Add(new DateTime(2026, 11, 12));
        dates.Add(new DateTime(2026, 11, 15));
        Check(Dashboard.ResolveIntensityIndex(dates, DateTime.MinValue) == 2, "默认跟随最新记录日");
        Check(Dashboard.ResolveIntensityIndex(dates, new DateTime(2026, 11, 12)) == 1, "翻页后按钉住的日期定位");
        Check(Dashboard.ResolveIntensityIndex(dates, new DateTime(2026, 11, 15)) == 2, "钉住最新日仍定位到最新");
        Check(Dashboard.ResolveIntensityIndex(dates, new DateTime(2026, 11, 11)) == 2, "钉住的日子被裁剪后退回最新");
        Check(Dashboard.ResolveIntensityIndex(new List<DateTime>(), DateTime.MinValue) == -1, "没有记录日返回 -1");
        Check(Dashboard.ResolveIntensityIndex(null, DateTime.MinValue) == -1, "空引用返回 -1");

        // ---- 6. 可翻页列表的筛选:优先有段的日子 ----
        Store.History.Clear();
        Store.Total = new Counters();
        Store.RollDay(DateTime.Today);
        DayRecord noSession = new DayRecord();
        noSession.Date = DateTime.Today.AddDays(-2);
        noSession.Keys = 100;                              // 有键鼠但没有连续段(老格式日)
        DayRecord withSession = new DayRecord();
        withSession.Date = DateTime.Today.AddDays(-1);
        withSession.Keys = 50;
        withSession.Sessions.Add(Session(DateTime.Today.AddDays(-1).AddHours(9), 600));
        Store.History[noSession.Date] = noSession;
        Store.History[withSession.Date] = withSession;
        List<DayRecord> browsable = Dashboard.IntensityDays();
        Check(browsable.Count == 1 && browsable[0].Date == withSession.Date.Date, "优先只收有连续段的日子");
        DayRecord alsoNoSession = new DayRecord();           // 同一天,但换成没有连续段的记录
        alsoNoSession.Date = withSession.Date;
        alsoNoSession.Keys = 10;
        Store.History[withSession.Date] = alsoNoSession;
        browsable = Dashboard.IntensityDays();
        Check(browsable.Count == 2, "一天都没有段时退回非空日列表");
        Store.History.Clear();
        Store.Total = new Counters();
        Store.RollDay(DateTime.Today);
        Check(Dashboard.IntensityDays().Count == 0, "只有空的今天时列表为空");

        // ---- 7. 全 13 套主题:选中态胶囊的字色对比度必须 ≥4.5:1 ----
        // 旧实现用「简单亮度 ≥0.55 就取 OnAccent」的固定阈值去猜字色。这个阈值在「生化危机」上
        // 差 0.0019 就判反了(Accent #DA7773 简单亮度 0.5481),近白字压上去只有 2.85:1。
        // 现在 ChipSelectedInk() 改成拿候选字色和真实底色实时算 WCAG 对比度择优,阈值消失,边界也消失。
        InkContrastAcrossThemes();

        // ---- 8. 日报页「保存 Markdown」不再被 _exportRect 抢占 ----
        // 真 Bug:Painter 在进入日报分支前就无条件写下 _exportRect=(912,680,120,28),而日报分支
        // 提前 return 从不清理它;OnMouseClick 里 Hit(_exportRect) 又排在 _chips 之前,
        // 于是点「保存 Markdown」的 912~1032 区域实际执行的是「导出 CSV」。
        SaveMarkdownHitTest();

        // ---- 9. 字符内容页的筛选控件必须走统一组件,不能再是私有小方块 ----
        TextPageChipGeometry();

        // ---- 10. 分布卡几何:键盘环顶弧不能压到 Top 10 ▾ 按钮 ----
        DistributionRingClearance();

        // ---- 11. 鼠标分时段聚合与全量累计的口径一致 ----
        MousePeriodAggregation();

        // ---- 12. 10 槽色板:任意两槽的 RGB 距离都要够大 ----
        KeyPaletteSpacing();

        // 收尾:把主题与字符日志的覆盖指回默认,别影响后面的用例。
        Store.ThemeId = 0;
        RimeTextStats.PathOverride = null;
    }

    /// <summary>
    /// 键盘环的外沿必须落在 Top 10 ▾ 按钮底边之下(留下 3px 净空),并且与鼠标环顶部齐平。
    /// 这条断言直接钉住上一轮 h24→h30 把按钮抬高、环没跟着下移造成的 3px 压盖回归。
    /// </summary>
    private static void DistributionRingClearance()
    {
        Store.ThemeId = 0;
        Store.History.Clear();
        Store.Total = new Counters();
        Store.RollDay(DateTime.Today);

        using (Dashboard form = NewDashboard(0))          // TabId.Overview
        {
            SetField(form, "_overviewKeyboard", true);
            Paint(form);
            RectangleF topChip = ChipById(form, 305);      // Top N ▾
            RectangleF ring = (RectangleF)GetField(form, "_distributionRing");
            float stroke = (float)GetField(form, "_distributionRingStroke");
            float ringTop = ring.Top - stroke / 2f;
            float ringBottom = ring.Bottom + stroke / 2f;
            Check(Math.Abs(topChip.Bottom - 343f) < 0.01f, "Top N 按钮底边 = 343(实际 " + topChip.Bottom + ")");
            Check(Math.Abs(ringTop - 346f) < 0.01f, "键盘环外沿顶部 = 346(实际 " + ringTop + ")");
            Check(ringTop >= topChip.Bottom, "键盘环外沿不得压到 Top N 按钮(" + ringTop + " >= " + topChip.Bottom + ")");
            Check(Math.Abs((ringTop - topChip.Bottom) - 3f) < 0.01f, "净空恰好 3px(实际 " + (ringTop - topChip.Bottom) + ")");
            Check(Math.Abs(stroke - 18f) < 0.01f, "键盘环笔宽 18");

            // 鼠标侧:同样的判定,顶部外沿必须与键盘一致(换模式时环不能上下跳)。
            SetField(form, "_overviewKeyboard", false);
            Paint(form);
            RectangleF mouseRing = (RectangleF)GetField(form, "_distributionRing");
            float mouseStroke = (float)GetField(form, "_distributionRingStroke");
            float mouseTop = mouseRing.Top - mouseStroke / 2f;
            Check(Math.Abs(mouseTop - 346f) < 0.01f, "鼠标环外沿顶部 + 6 下移后也落在 346(实际 " + mouseTop + ")");
            Check(Math.Abs(mouseTop - ringTop) < 0.01f, "两种模式的环顶边齐平");
            Check(mouseTop > topChip.Bottom, "鼠标环同样不压按钮");
            Check(ringBottom <= 540.01f, "键盘环底部不越过卡片底边 540(实际 " + ringBottom + ")");

            // 环的左外沿必须让开左侧的时段胶囊列(否则环会被画在 今日/本周/本月/本年 上面)。
            RectangleF periodChip = ChipById(form, 306);   // 今日
            float ringLeft = mouseRing.Left - mouseStroke / 2f;
            Check(ringLeft >= periodChip.Right,
                "鼠标环左外沿不压时段胶囊(环 " + ringLeft + " vs 胶囊右 " + periodChip.Right + ")");
            Check(Math.Abs((ringLeft - periodChip.Right) - 9f) < 0.01f,
                "环与胶囊列净空 9px(实际 " + (ringLeft - periodChip.Right) + ")");
            Check(mouseRing == ring, "两种模式的环共用同一个外接矩形(切模式不位移)");

            // 「其他」扇区必须出图例:造 15 个不同的键,Top 10 之外还有剩余,
            // 图例里就要出现一条 id = 350 + 10 的「其他」。
            DayRecord seed = Store.Today;
            seed.KeyCounts.Clear();
            long sum = 0;
            for (int vk = 0x41; vk <= 0x4F; vk++) { seed.KeyCounts[vk] = 1000 - (vk - 0x41) * 40; sum += seed.KeyCounts[vk]; }
            seed.Keys = sum;                       // 同时改总数,否则排行缓存会认为没变
            Store.History[seed.Date] = seed;
            SetField(form, "_overviewKeyboard", true);
            SetField(form, "_distributionPeriod", 0);   // 今日
            SetField(form, "_distributionTop", 10);
            Paint(form);
            RectangleF otherLegend = ChipById(form, 350 + 10);
            Check(otherLegend.Width > 0f, "「其他」扇区在图例里有一条可点击条目");
            Check(otherLegend.Y >= 306f && otherLegend.Bottom <= 540f, "「其他」图例行落在分布卡内");
            // 前 10 名的图例行也都要在卡内(11 条图例不能溢出卡片底边)。
            for (int i = 0; i < 10; i++)
            {
                RectangleF row = ChipById(form, 350 + i);
                Check(row.Bottom <= 540f, "第 " + (i + 1) + " 名图例行不溢出卡片底边(实际 " + row.Bottom + ")");
            }

            // 下钻:点第 1 名要切到「按键排行」并高亮该键;点「其他」只切页、不高亮单个键。
            SetField(form, "_tab", Enum.ToObject(typeof(Dashboard).GetNestedType("TabId", BindingFlags.NonPublic), 0));
            form.DrillIntoKeys(0);
            Check((int)GetField(form, "_tab") == 3, "下钻后落在「按键排行」(TabId.Keys = 3)");
            Check((int)GetField(form, "_keyView") == 0, "下钻后是按键排行视图");
            Check((string)GetField(form, "_selectedKeyName") == "A", "下钻高亮的是第 1 名「A」(上面种子的最高频键)");
            form.DrillIntoKeys(10);
            Check((string)GetField(form, "_selectedKeyName") == "", "点「其他」只切页,不高亮单个键");
        }

        Store.History.Clear();
        Store.Total = new Counters();
        Store.RollDay(DateTime.Today);
    }

    /// <summary>
    /// 鼠标环从 Store.History 按 _distributionPeriod 聚合之后,「今日」口径只该算今天,
    /// 未来日绝不能计入,且当窗口覆盖全部历史时结果必须与全量累计完全相等。
    /// </summary>
    private static void MousePeriodAggregation()
    {
        Store.ThemeId = 0;
        Store.History.Clear();
        Store.Total = new Counters();
        Store.RollDay(DateTime.Today);

        DayRecord today = new DayRecord { Date = DateTime.Today };
        today.Left = 11; today.Right = 7; today.Middle = 3; today.XButtons = 2;
        DayRecord yesterday = new DayRecord { Date = DateTime.Today.AddDays(-1) };
        yesterday.Left = 5; yesterday.Right = 4; yesterday.Middle = 3; yesterday.XButtons = 2;
        DayRecord future = new DayRecord { Date = DateTime.Today.AddDays(5) };
        future.Left = 99; future.Right = 99; future.Middle = 99; future.XButtons = 99;
        Store.History[future.Date] = future;
        Store.History[yesterday.Date] = yesterday;
        Store.History[today.Date] = today;

        using (Dashboard form = NewDashboard(0))
        {
            long left, right, middle, extra;

            SetField(form, "_distributionPeriod", 0);      // 今日
            form.MousePeriodValues(out left, out right, out middle, out extra);
            Check(left == 11 && right == 7 && middle == 3 && extra == 2,
                "今日口径只取今天(实际 " + left + "/" + right + "/" + middle + "/" + extra + ")");

            SetField(form, "_distributionPeriod", 3);      // 本年
            form.MousePeriodValues(out left, out right, out middle, out extra);
            DateTime yearStart = new DateTime(DateTime.Today.Year, 1, 1);
            bool yesterdayInside = yesterday.Date >= yearStart;
            long expectLeft = 11 + (yesterdayInside ? 5 : 0);
            long expectRight = 7 + (yesterdayInside ? 4 : 0);
            Check(left == expectLeft && right == expectRight,
                "本年口径把窗口内的日子全算上、未来日排除(实际 " + left + "/" + right + ",期望 " + expectLeft + "/" + expectRight + ")");
            Check(left < 99 && right < 99, "未来日(99)不得计入");

            // 口径一致:窗口覆盖全部历史时,分时段聚合必须等于全量累计。
            Store.Total = new Counters();
            foreach (DayRecord day in Store.History.Values)
                if (day.Date <= DateTime.Today && day.Date >= yearStart)
                {
                    Store.Total.Left += day.Left; Store.Total.Right += day.Right;
                    Store.Total.Middle += day.Middle; Store.Total.XButtons += day.XButtons;
                }
            form.MousePeriodValues(out left, out right, out middle, out extra);
            Check(left == Store.Total.Left && right == Store.Total.Right
                && middle == Store.Total.Middle && extra == Store.Total.XButtons,
                "分时段聚合与全量累计口径一致");
        }

        Store.History.Clear();
        Store.Total = new Counters();
        Store.RollDay(DateTime.Today);
    }

    /// <summary>
    /// 10 槽色板:任意两槽在 RGB 三通道上都要拉开足够距离。
    /// 旧实现把槽 6–9 定义成基色两两插值,默认主题下 I(槽1 Cyan) 与 H(槽8 Mix(Green,Cyan,.45))
    /// 只差 ΔB=40、ΔG=5 —— 9px 的图例圆点肉眼难辨。
    /// </summary>
    private static void KeyPaletteSpacing()
    {
        double worstHue = double.MaxValue;
        string worstWhere = "";
        for (int id = 0; id < ArtTheme.All.Length; id++)
        {
            Store.ThemeId = id;
            Color[] slots = new Color[KeyPalette.Slots];
            for (int i = 0; i < slots.Length; i++) slots[i] = KeyPalette.Slot(i);
            // 槽 0–5 是主题自己的六个基色,它们之间的相近属于主题配色,不归色板算法管;
            // 这里只对「至少有一端来自 6–9 溢出槽」的配对设要求 —— 那才是这次改动的责任区。
            // 用色相距离而不是 RGB 距离:旧实现把槽 8 定义成 Mix(Green,Cyan,.45),
            // 色相落在两个基色正中间,RGB 看着有点差、色相其实只隔几度,肉眼就是同一个青。
            double worst = double.MaxValue;
            string pair = "";
            for (int i = 0; i < slots.Length; i++)
                for (int j = i + 1; j < slots.Length; j++)
                {
                    if (i < 6 && j < 6) continue;
                    double distance = HueDistance(slots[i], slots[j]);
                    if (distance < worst) { worst = distance; pair = i + "-" + j; }
                }
            if (worst < worstHue) { worstHue = worst; worstWhere = ArtTheme.Current.Name + "(" + pair + ")"; }
            // 下界取 12°:把 6 个基色排到色相环上、再去填最大的 4 个空隙,
            // 「新色到最近基色的色相距离」的上限由主题自己的基色分布决定(空隙只有那么大)。
            // 「取最大空隙的中点」已经是这个约束下的最优解,再高就是跟主题配色过不去。
            Check(worst >= 12.0, "涉及溢出槽的色相距离 ≥12° (" + ArtTheme.Current.Name + " 最小 "
                + worst.ToString("0.0", CultureInfo.InvariantCulture) + "° @ " + pair + ")");
        }
        Check(KeyPalette.Slots == 10, "色板共 10 槽");

        // 审计点名的具体回归:默认主题「午夜蓝」下 I(槽1) 与 H(槽8) 必须肉眼可分。
        // 旧值 #4CC9F0 vs #4BCEC8 只差 ΔB=40、ΔG=5,9px 图例点上几乎同色。
        Store.ThemeId = 0;
        double ih = ColorDistance(KeyPalette.Slot(1), KeyPalette.Slot(8));
        Check(ih >= 60.0, "默认主题下 I(槽1) 与 H(槽8) 必须可区分 (实测 "
            + ih.ToString("0.0", CultureInfo.InvariantCulture) + ")");
        Console.WriteLine("palette(默认主题): 槽1=" + Hex(KeyPalette.Slot(1)) + " 槽8=" + Hex(KeyPalette.Slot(8))
            + " Δ=" + ih.ToString("0.0", CultureInfo.InvariantCulture)
            + "; 全主题最差溢出槽色相差 = " + worstHue.ToString("0.0", CultureInfo.InvariantCulture) + "° @ " + worstWhere);
    }

    private static string Hex(Color c)
    {
        return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
    }

    private static double ColorDistance(Color a, Color b)
    {
        double dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
        return Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    /// <summary>两色的色相距离(0–180°),色相接近但明度差很多的组合不该被当成"可区分"。</summary>
    private static double HueDistance(Color a, Color b)
    {
        double ha = Hue(a), hb = Hue(b);
        double diff = Math.Abs(ha - hb);
        return Math.Min(diff, 360.0 - diff);
    }

    private static double Hue(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;
        if (delta <= 1e-9) return 0;
        double hue;
        if (max == r) hue = (g - b) / delta + (g < b ? 6.0 : 0.0);
        else if (max == g) hue = (b - r) / delta + 2.0;
        else hue = (r - g) / delta + 4.0;
        return hue * 60.0;
    }

    /// <summary>13 套主题逐一验证选中态字色对比度,并复算旧阈值法证明本断言有区分力。</summary>
    private static void InkContrastAcrossThemes()
    {
        Check(ArtTheme.All.Length == 13, "主题表共 13 套");
        for (int id = 0; id < ArtTheme.All.Length; id++)
        {
            Store.ThemeId = id;
            ArtTheme theme = ArtTheme.Current;
            Color backdrop = Dashboard.ChipSelectedBackdrop();
            Color ink = Dashboard.ChipSelectedInk();
            double ratio = ArtTheme.ContrastRatio(ink, backdrop);
            Check(ratio >= 4.5, "选中态胶囊字色对比度 ≥4.5:1 (" + theme.Name + " = "
                + ratio.ToString("0.00", CultureInfo.InvariantCulture) + ":1)");
        }

        // 回归线:把旧阈值法原样复算一遍,它在「生化危机」上必须不达标 ——
        // 否则上面的断言对"改回阈值法"这件事就没有区分力。
        Store.ThemeId = 7;
        Color oldBackdrop = ArtTheme.Current.Accent;
        double simpleLuminance = (0.2126 * oldBackdrop.R + 0.7152 * oldBackdrop.G + 0.0722 * oldBackdrop.B) / 255.0;
        Color onAccent = ArtTheme.Current.OnAccent;
        double onAccentLuminance = (0.2126 * onAccent.R + 0.7152 * onAccent.G + 0.0722 * onAccent.B) / 255.0;
        Color oldInk = simpleLuminance >= 0.55 ? onAccent
            : onAccentLuminance >= 0.55 ? onAccent : Color.FromArgb(245, 247, 250);
        double oldRatio = ArtTheme.ContrastRatio(oldInk, oldBackdrop);
        Check(oldRatio < 3.0, "旧阈值法在「生化危机」上仍不达标,证明对比度断言有区分力: "
            + oldRatio.ToString("0.00", CultureInfo.InvariantCulture) + ":1");
    }

    /// <summary>
    /// 日报页命中测试:窗口坐标 (970,694) 是「保存 Markdown」(908,680,124,28) 的中心,
    /// 该点必须命中 chip 423(保存),而不是导出;同时 _exportRect 在日报页必须已被清空。
    /// </summary>
    private static void SaveMarkdownHitTest()
    {
        Store.ThemeId = 0;
        Store.History.Clear();
        Store.Total = new Counters();
        Store.RollDay(DateTime.Today);
        // 日报构建会去读上屏日志:先指到临时文件,保证连"读"都不碰用户的真实记录。
        SeedRimeLog(Path.Combine(Dir, "day-report"));

        using (Dashboard form = NewDashboard(7))       // TabId.Day = 7
        {
            Paint(form);
            // 「保存 Markdown」画在窗口坐标,命中区按内容坐标登记(减 ContentX/ContentY)。
            float centerX = 908f + 124f / 2f - 176f;
            float centerY = 680f + 28f / 2f - 64f;
            int hit = HitChipId(form, new PointF(centerX, centerY));
            Check(hit == 423, "日报页「保存 Markdown」中心点命中 423(保存),实际命中 = " + hit);
            Check(hit != Dashboard.ExportChipId, "日报页「保存 Markdown」不再误触导出 CSV");

            RectangleF export = (RectangleF)GetField(form, "_exportRect");
            Check(export.Width <= 0f && export.Height <= 0f, "日报页 _exportRect 已清空(实际 "
                + export.Width + " x " + export.Height + ")");
            Check(!((List<int>)GetField(form, "_chipIds")).Contains(Dashboard.ExportChipId),
                "日报页不登记导出 chip");

            // 反过来:非日报页必须仍然画出导出按钮,而且登记进 _chips(键盘 Tab 可达)。
            SetField(form, "_tab", Enum.ToObject(typeof(Dashboard).GetNestedType("TabId", BindingFlags.NonPublic), 0));
            Paint(form);
            Check(((List<int>)GetField(form, "_chipIds")).Contains(Dashboard.ExportChipId),
                "总览页登记了「导出 CSV」chip(键盘 Tab 可达)");
            RectangleF exported = (RectangleF)GetField(form, "_exportRect");
            Check(exported.Width > 0f, "总览页仍画出「导出 CSV」");
        }

        Store.History.Clear();
        Store.Total = new Counters();
        Store.RollDay(DateTime.Today);
    }

    /// <summary>
    /// 字符内容页:顶部区间筛选必须是 4 段等宽托盘(段高 26、间隙 3、整组右对齐到 Cx+Cw),
    /// 卡片内切换器必须是 30 高的标准 ToggleChip —— 不再是 24 高的私有小方块。
    /// </summary>
    private static void TextPageChipGeometry()
    {
        Store.ThemeId = 0;
        string log = Path.Combine(Dir, "text-chips");
        SeedRimeLog(log);
        try
        {
            using (Dashboard form = NewDashboard(6))       // TabId.Text = 6
            {
                SetField(form, "_textRange", 1);
                Paint(form);

                RectangleF[] segments = new RectangleF[4];
                for (int i = 0; i < 4; i++) segments[i] = ChipById(form, 400 + i);

                // 段高 = 托盘 32 - 上下内边距 3×2 = 26;旧私有实现是 24,这里一次性钉死。
                for (int i = 0; i < 4; i++)
                {
                    Check(Math.Abs(segments[i].Height - 26f) < 0.01f,
                        "区间筛选第 " + i + " 段高度 = 26(托盘内块),实际 " + segments[i].Height);
                    Check(Math.Abs(segments[i].Y - segments[0].Y) < 0.01f, "区间筛选 4 段共基线");
                }
                // 段宽 =(248 - 3×2 内边距 - 3×3 间隙)/ 4 = 58.25,相邻段间距 = 61.25
                Check(Math.Abs(segments[0].Width - 58.25f) < 0.01f, "区间筛选段宽 = 58.25");
                Check(Math.Abs((segments[1].X - segments[0].X) - 61.25f) < 0.01f, "区间筛选段间距 = 3");
                // 整组右对齐:末段右边缘 + 3px 内边距正好落在内容右边缘 Cx+Cw = 856
                Check(Math.Abs(segments[3].Right + 3f - (24f + 832f)) < 0.01f, "区间筛选整组右对齐到 Cx+Cw(右侧留 3px 内边距)");
                // 首段左边缘 = 托盘左边缘 + 内边距 3 → 托盘宽 248
                Check(Math.Abs((24f + 832f - 248f + 3f) - segments[0].X) < 0.01f, "托盘宽 248 + 内边距 3");

                // 卡片内切换器:标准 ToggleChip 高度 30(旧私有实现 24)
                Check(Math.Abs(ChipById(form, 410).Height - 30f) < 0.01f, "右卡模式 chip 高度 = 30");
                Check(Math.Abs(ChipById(form, 406).Height - 30f) < 0.01f, "图表切换 chip 高度 = 30");
                Check(Math.Abs(ChipById(form, 405).Height - 30f) < 0.01f, "「刷新」chip 高度 = 30");

                // 选中的区间段必须是"选中态":它的字色与 ChipSelectedInk 一致(不再是暗红 Cblue)。
                Store.ThemeId = 10;                        // 大侠:旧实现在这里只有 2.58:1
                SetField(form, "_textRange", 1);
                Paint(form);
                Color ink = Dashboard.ChipSelectedInk();
                double ratio = ArtTheme.ContrastRatio(ink, Dashboard.ChipSelectedBackdrop());
                Check(ratio >= 4.5, "大侠主题下字符页选中段字色 ≥4.5:1 ("
                    + ratio.ToString("0.00", CultureInfo.InvariantCulture) + ":1)");
            }
        }
        finally
        {
            RimeTextStats.PathOverride = null;
            try { if (Directory.Exists(Dir)) Directory.Delete(Dir, true); } catch { }
        }
    }

    /// <summary>在隔离临时目录里生成一份 Rime 上屏日志,并把 PathOverride 指过去(绝不碰用户真实日志)。</summary>
    private static void SeedRimeLog(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string[] phrases = { "今天", "统计", "键鼠", "输入", "效率", "简报", "数据" };
        StringBuilder csv = new StringBuilder();
        for (int i = 0; i < 40; i++)
        {
            DateTime time = DateTime.Today.AddHours(9).AddMinutes(i * 7);
            string text = phrases[i % phrases.Length];
            csv.Append(time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append(',')
               .Append(text.Length).Append(',').Append(text).AppendLine();
        }
        File.WriteAllText(path, csv.ToString(), new UTF8Encoding(false));
        RimeTextStats.PathOverride = path;
    }

    private static Dashboard NewDashboard(int tab)
    {
        Dashboard form = new Dashboard();
        typeof(Dashboard).GetMethod("OnLoad", Private).Invoke(form, new object[] { EventArgs.Empty });
        SetField(form, "_s", 1f);
        form.ClientSize = new Size(1072, 724);
        SetField(form, "_tab", Enum.ToObject(typeof(Dashboard).GetNestedType("TabId", BindingFlags.NonPublic), tab));
        return form;
    }

    private static RectangleF ChipById(Dashboard form, int id)
    {
        List<int> ids = (List<int>)GetField(form, "_chipIds");
        List<RectangleF> chips = (List<RectangleF>)GetField(form, "_chips");
        int index = ids.IndexOf(id);
        if (index < 0) throw new Exception("chip " + id + " 没有被登记(控件缺失)");
        return chips[index];
    }

    /// <summary>按内容坐标找第一个命中的 chip id,复刻 OnMouseClick 的 first-hit 规则。</summary>
    private static int HitChipId(Dashboard form, PointF contentPoint)
    {
        List<int> ids = (List<int>)GetField(form, "_chipIds");
        List<RectangleF> chips = (List<RectangleF>)GetField(form, "_chips");
        for (int i = 0; i < chips.Count && i < ids.Count; i++)
            if (chips[i].Contains(contentPoint)) return ids[i];
        return -1;
    }

    /// <summary>在指定底色的位图上调用真实绘制函数,返回托盘内部一点的颜色。</summary>
    private static Color PaintTraySample(int themeId, Color backdrop)
    {
        Store.ThemeId = themeId;
        using (Bitmap bitmap = new Bitmap(140, 48))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(backdrop);
            Dashboard.PaintSegmentTray(graphics, 4, 4, 132);
            return bitmap.GetPixel(70, 20);
        }
    }

    private static void Paint(Dashboard form)
    {
        using (Bitmap bitmap = new Bitmap(1072, 724))
        using (Graphics graphics = Graphics.FromImage(bitmap))
            typeof(Dashboard).GetMethod("OnPaint", Private).Invoke(form,
                new object[] { new PaintEventArgs(graphics, new Rectangle(0, 0, bitmap.Width, bitmap.Height)) });
    }

    /// <summary>
    /// 连续段的离屏冒烟:三种指标各画一次(真实走 PaintHours → PaintHourIntensity),
    /// 断言柱块命中表按段数填满、悬停气泡写下了明细 —— 只断言常量是抓不住绘制被改坏的。
    /// </summary>
    private static void PaintSmoke()
    {
        Store.ThemeId = 0;                       // 纯色主题:不依赖任何主题图片资源
        Store.History.Clear();
        Store.Total = new Counters();
        Store.RollDay(DateTime.Today);
        DayRecord day = Store.Today;
        day.Keys = 900; day.Clicks = 300; day.ActiveSeconds = 3600;
        for (int h = 0; h < 24; h++)
        {
            bool on = h >= 9 && h <= 12;
            day.HourKeys[h] = on ? 120 : 0;
            day.HourClicks[h] = on ? 40 : 0;
            day.ActiveHours[h] = on ? 2400 : 0;
        }
        day.Sessions.Add(Session(day.Date.AddHours(9), 600));
        day.Sessions.Add(Session(day.Date.AddHours(11), 1800));
        day.Sessions.Add(Session(day.Date.AddHours(20), 45));

        using (Dashboard form = new Dashboard())
        {
            typeof(Dashboard).GetMethod("OnLoad", Private).Invoke(form, new object[] { EventArgs.Empty });
            SetField(form, "_s", 1f);
            form.ClientSize = new Size(1072, 724);
            SetField(form, "_tab", Enum.ToObject(typeof(Dashboard).GetNestedType("TabId", BindingFlags.NonPublic), 2));
            SetField(form, "_hourView", 2);
            List<RectangleF> bars = (List<RectangleF>)GetField(form, "_segmentBars");
            for (int metric = 0; metric < 3; metric++)
            {
                SetField(form, "_hourMetric", metric);
                bars.Clear();
                Paint(form);
                Check(bars.Count == 3, "连续段柱块命中表按段数填满(指标 " + metric + ")");
            }
            SetField(form, "_hourMetric", 0);
            SetField(form, "_hover", 0);
            SetField(form, "_hoverDetail", "");
            Paint(form);
            Check(((string)GetField(form, "_hoverDetail")).Length > 0, "连续段悬停气泡写入了明细(供 Ctrl+C 复制)");
            SetField(form, "_hover", -1);
            SetField(form, "_hourView", 0);              // 柱子视图必须清空明细,不能留上一段的
            Paint(form);
            Check(((string)GetField(form, "_hoverDetail")).Length == 0, "柱子视图不残留连续段的悬停明细");
            typeof(Dashboard).GetMethod("OnFormClosed", Private).Invoke(form,
                new object[] { new FormClosedEventArgs(CloseReason.None) });
        }
        Store.History.Clear();
        Store.Total = new Counters();
        Store.RollDay(DateTime.Today);
    }
}
